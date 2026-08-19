using System.Text.Json;
using BuildingBlocks.Application;
using MassTransit;
using MessageService.Application.Contracts;
using MessageService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessageService.Infrastructure.Messaging;

/// <summary>
/// Publica no RabbitMQ os eventos pendentes na outbox do Message Service.
/// </summary>
/// <remarks>
/// Mesma mecânica do despachante do Identity Service. Aqui o evento publicado é
/// <see cref="MessageProjectionRequested"/>, consumido pelo próprio serviço para
/// atualizar as projeções de leitura.
/// </remarks>
public sealed class MessageOutboxDispatcher(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<MessageOutboxDispatcher> logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Intervalo entre varreduras.</summary>
    /// <remarks>
    /// Mais curto (2s) que o do Identity Service (5s) de propósito: este evento
    /// alimenta o histórico visível na interface, então o atraso aqui é
    /// percebido diretamente pelo usuário — é a "consistência eventual" de que
    /// fala a documentação do read model.
    /// </remarks>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    private const int BatchSize = 50;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // O laço não pode morrer: sem ele, as projeções param de ser
                // atualizadas e o histórico congela sem nenhum erro visível.
                logger.LogError(exception, "Falha no ciclo de despacho da outbox do Message Service.");
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
        using var scope = serviceScopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<MessageDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null)
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
    /// Materializa e publica o evento a partir do nome do tipo gravado na outbox.
    /// </summary>
    /// <remarks>
    /// Lista fechada de tipos: evita desserialização de tipo arbitrário a partir
    /// de conteúdo do banco, que seria um vetor de execução de código.
    /// </remarks>
    private static async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        string messageType,
        string payload,
        CancellationToken cancellationToken)
    {
        switch (messageType)
        {
            case nameof(MessageProjectionRequested):
                var projection = JsonSerializer.Deserialize<MessageProjectionRequested>(payload, SerializerOptions)
                    ?? throw new InvalidOperationException($"Payload vazio para {messageType}.");
                await publishEndpoint.Publish(projection, cancellationToken);
                break;

            default:
                throw new NotSupportedException(
                    $"Tipo de evento '{messageType}' desconhecido por esta versão do despachante.");
        }
    }
}
