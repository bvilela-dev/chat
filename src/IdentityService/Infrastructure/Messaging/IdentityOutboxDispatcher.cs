using System.Text.Json;
using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using IdentityService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Messaging;

/// <summary>
/// Serviço em segundo plano que publica no RabbitMQ os eventos pendentes na outbox.
/// </summary>
/// <remarks>
/// <para>
/// É a segunda metade do padrão Outbox. O <see cref="EfOutboxWriter"/> grava o
/// evento junto com o dado de negócio; este despachante o entrega ao broker.
/// </para>
/// <para>
/// <b>Garantia oferecida: entrega pelo menos uma vez.</b> Se o processo cair
/// entre publicar e marcar como processado, o evento será publicado de novo no
/// próximo ciclo. Esse é o compromisso consciente do padrão — a alternativa
/// (marcar antes de publicar) trocaria duplicação por <i>perda</i>, que é
/// bem pior. A duplicação é neutralizada do outro lado, pela tabela de inbox
/// dos consumidores.
/// </para>
/// <para>
/// <b>Limitação conhecida: múltiplas réplicas.</b> Com N instâncias do serviço,
/// todas leem as mesmas linhas pendentes e publicam o mesmo evento N vezes.
/// Funciona (a idempotência dos consumidores absorve), mas desperdiça banda. A
/// solução usual é <c>SELECT ... FOR UPDATE SKIP LOCKED</c>, que faz cada
/// réplica travar um lote distinto. Deixamos documentado como próximo passo em
/// vez de fingir que o problema não existe.
/// </para>
/// </remarks>
public sealed class IdentityOutboxDispatcher(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<IdentityOutboxDispatcher> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Intervalo entre varreduras da outbox.</summary>
    /// <remarks>
    /// 5 segundos é o teto de latência que um evento pode levar para sair. Como
    /// os eventos deste serviço (criação de usuário) não são sensíveis a
    /// latência, o intervalo é generoso de propósito, para não martelar o banco.
    /// Um fluxo sensível a tempo usaria polling mais curto ou, melhor,
    /// <c>LISTEN/NOTIFY</c> do PostgreSQL para ser acordado por evento.
    /// </remarks>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    /// <summary>Máximo de mensagens publicadas por ciclo.</summary>
    /// <remarks>
    /// Limitar o lote mantém a transação curta e o consumo de memória previsível.
    /// Havendo acúmulo, o próximo ciclo pega o restante em 5 segundos.
    /// </remarks>
    private const int BatchSize = 50;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer, em vez de Task.Delay em laço: não acumula desvio de
        // tempo e responde ao cancelamento de forma limpa no encerramento.
        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Encerramento normal da aplicação.
                break;
            }
            catch (Exception exception)
            {
                // O laço NUNCA pode morrer por uma exceção. Se ele parar, os
                // eventos deixam de ser publicados em silêncio e o sistema fica
                // inconsistente sem nenhum sintoma visível — o pior tipo de
                // falha. Registra e segue para o próximo ciclo.
                logger.LogError(exception, "Falha no ciclo de despacho da outbox do Identity Service.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchPendingMessagesAsync(CancellationToken cancellationToken)
    {
        // BackgroundService é singleton; DbContext é scoped. Resolver o contexto
        // diretamente do provedor raiz criaria um DbContext vivo pelo tempo todo
        // da aplicação — com o change tracker crescendo indefinidamente. Criar um
        // escopo por ciclo é a forma correta.
        using var scope = serviceScopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null)
            // Ordem cronológica: eventos do mesmo agregado precisam chegar na
            // ordem em que aconteceram.
            .OrderBy(message => message.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        foreach (var message in pendingMessages)
        {
            try
            {
                await PublishAsync(publishEndpoint, message.Type, message.Payload, cancellationToken);
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception exception)
            {
                // Falha em uma mensagem não interrompe as demais: uma linha
                // "envenenada" não deve bloquear a fila inteira.
                logger.LogError(
                    exception,
                    "Falha ao publicar a mensagem de outbox {OutboxMessageId} do tipo {MessageType}.",
                    message.Id,
                    message.Type);

                message.MarkFailed(exception.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Desserializa e publica um evento a partir do seu nome de tipo.
    /// </summary>
    /// <remarks>
    /// O <c>switch</c> explícito é intencional. A alternativa —
    /// <c>Type.GetType(nomeVindoDoBanco)</c> — instanciaria um tipo arbitrário a
    /// partir de conteúdo do banco, o que é um vetor de desserialização insegura
    /// caso alguém consiga escrever nessa tabela. Com a lista fechada, só os
    /// eventos que este serviço realmente publica podem ser materializados.
    /// </remarks>
    private async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        string messageType,
        string payload,
        CancellationToken cancellationToken)
    {
        switch (messageType)
        {
            case nameof(UserCreatedEvent):
                var userCreated = JsonSerializer.Deserialize<UserCreatedEvent>(payload, SerializerOptions)
                    ?? throw new InvalidOperationException($"Payload vazio para {messageType}.");
                await publishEndpoint.Publish(userCreated, cancellationToken);
                break;

            default:
                // Tipo desconhecido: provavelmente uma mensagem gravada por uma
                // versão mais nova do serviço durante um deploy gradual.
                // Lançar é o correto — a mensagem fica pendente e será publicada
                // quando esta instância for atualizada. Marcá-la como processada
                // aqui a descartaria para sempre.
                throw new NotSupportedException(
                    $"Tipo de evento '{messageType}' desconhecido por esta versão do despachante.");
        }
    }
}
