using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using MassTransit;
using MediatR;
using MessageService.Application.Abstractions;
using MessageService.Application.Contracts;
using MessageService.Application.Conversations;
using MessageService.Application.Messages;
using MessageService.Application.Projections;

namespace MessageService.Infrastructure.Messaging;

/// <summary>
/// Base para consumidores que precisam ser idempotentes.
/// </summary>
/// <typeparam name="TEvent">Tipo do evento consumido.</typeparam>
/// <remarks>
/// <para>
/// Antes desta classe, os quatro consumidores do serviço repetiam o mesmo bloco
/// de nove linhas: consultar a inbox, decidir se processa, delegar ao MediatR,
/// marcar como processado, salvar. Duplicação de <i>protocolo</i> é
/// especialmente arriscada — basta um consumidor novo esquecer o passo de
/// deduplicação para que mensagens comecem a ser gravadas em duplicidade, e o
/// sintoma só aparece sob carga, quando o broker começa a reentregar.
/// </para>
/// <para>
/// Com o template aqui, um consumidor novo herda a idempotência: ele só pode
/// implementar o que é específico dele.
/// </para>
/// <para>
/// <b>Nota sobre a garantia real.</b> A verificação e a marcação acontecem na
/// mesma transação do trabalho de negócio, o que resolve a duplicação dentro de
/// um processo. Ainda existe uma janela teórica com duas réplicas consumindo a
/// <i>mesma</i> mensagem em paralelo: as duas leem a inbox vazia e as duas
/// processam. Nesse caso, a chave primária composta da inbox faz a segunda
/// transação falhar no commit — e o MassTransit reenfileira a mensagem, que na
/// nova tentativa encontra o registro e é descartada. O resultado final continua
/// correto; a defesa está no banco, não na checagem em memória.
/// </para>
/// </remarks>
public abstract class IdempotentConsumer<TEvent>(
    IMessageRepository repository,
    ISender sender,
    IClock clock)
    : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    /// <summary>
    /// Nome usado como parte da chave de deduplicação.
    /// </summary>
    /// <remarks>
    /// Precisa ser único por consumidor e <b>estável entre versões</b>. Renomear
    /// a classe muda esta chave e faz todos os eventos já processados parecerem
    /// novos — o que reprocessaria o histórico inteiro no próximo deploy.
    /// </remarks>
    protected virtual string ConsumerName => GetType().Name;

    /// <summary>Ponto de extensão: o trabalho específico do consumidor.</summary>
    protected abstract Task HandleAsync(TEvent message, CancellationToken cancellationToken);

    /// <summary>Envia um comando ou query pelo MediatR.</summary>
    protected Task SendAsync(IRequest request, CancellationToken cancellationToken)
    {
        return sender.Send(request, cancellationToken);
    }

    /// <summary>Executa o consumo com deduplicação.</summary>
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var cancellationToken = context.CancellationToken;

        if (await repository.HasProcessedAsync(context.Message.EventId, ConsumerName, cancellationToken))
        {
            // Já processado: descarta em silêncio. Não é erro — é o caminho
            // esperado sempre que o broker reentrega uma mensagem.
            return;
        }

        await HandleAsync(context.Message, cancellationToken);

        await repository.MarkProcessedAsync(
            context.Message.EventId,
            ConsumerName,
            clock.UtcNow,
            cancellationToken);

        // Persiste o registro de deduplicação junto com o efeito de negócio.
        await repository.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Persiste no PostgreSQL as mensagens publicadas pelo Chat Service.
/// </summary>
/// <remarks>
/// Fecha o ciclo do fluxo de envio: o Chat Service entrega a mensagem em tempo
/// real pelo SignalR (rápido, volátil) e publica o evento; este consumidor lhe dá
/// durabilidade (mais lento, permanente). O usuário vê a mensagem
/// instantaneamente; o histórico se consolida logo em seguida.
/// </remarks>
public sealed class MessageSentConsumer(IMessageRepository repository, ISender sender, IClock clock)
    : IdempotentConsumer<MessageSentEvent>(repository, sender, clock)
{
    /// <inheritdoc />
    protected override Task HandleAsync(MessageSentEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(new PersistMessageCommand(message), cancellationToken);
    }
}

/// <summary>Registra a entrada de um usuário numa conversa.</summary>
public sealed class ConversationJoinedConsumer(IMessageRepository repository, ISender sender, IClock clock)
    : IdempotentConsumer<ConversationJoinedEvent>(repository, sender, clock)
{
    /// <inheritdoc />
    protected override Task HandleAsync(ConversationJoinedEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new UpdateConversationMembershipCommand(message.ConversationId, message.UserId, Joined: true),
            cancellationToken);
    }
}

/// <summary>Registra a saída de um usuário de uma conversa.</summary>
public sealed class ConversationLeftConsumer(IMessageRepository repository, ISender sender, IClock clock)
    : IdempotentConsumer<ConversationLeftEvent>(repository, sender, clock)
{
    /// <inheritdoc />
    protected override Task HandleAsync(ConversationLeftEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new UpdateConversationMembershipCommand(message.ConversationId, message.UserId, Joined: false),
            cancellationToken);
    }
}

/// <summary>
/// Aplica as mensagens persistidas às projeções de leitura.
/// </summary>
/// <remarks>
/// Este consumidor é interno ao Message Service: o evento que ele consome é
/// publicado pela outbox do próprio serviço. Pode parecer um rodeio — por que
/// não atualizar a projeção direto no comando de persistência? —, mas a
/// separação é o que mantém a transação de escrita curta e torna a projeção
/// reconstruível de forma independente. Ver <c>MessageProjectionRequested</c>.
/// </remarks>
public sealed class MessageProjectionRequestedConsumer(IMessageRepository repository, ISender sender, IClock clock)
    : IdempotentConsumer<MessageProjectionRequested>(repository, sender, clock)
{
    /// <inheritdoc />
    protected override Task HandleAsync(MessageProjectionRequested message, CancellationToken cancellationToken)
    {
        return SendAsync(new ProjectMessageReadModelCommand(message), cancellationToken);
    }
}
