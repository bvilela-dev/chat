using BuildingBlocks.Contracts;
using MassTransit;
using MediatR;
using NotificationService.Application.Abstractions;
using NotificationService.Application.Notifications;

namespace NotificationService.Infrastructure.Messaging;

/// <summary>
/// Base idempotente para os consumidores do Notification Service.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo padrão do Message Service, com uma diferença de armazenamento: aqui o
/// registro de deduplicação fica em Redis com TTL, não numa tabela do PostgreSQL.
/// </para>
/// <para>
/// A escolha se justifica pelo que está em jogo. No Message Service, duplicar
/// significa gravar a mesma mensagem duas vezes no histórico — um erro
/// permanente e visível, que exige a garantia transacional do banco. Aqui,
/// duplicar significa enviar uma notificação repetida: incômodo, mas efêmero.
/// Redis com TTL entrega a proteção necessária, com custo muito menor e sem
/// exigir um banco relacional só para isso.
/// </para>
/// <para>
/// <b>Diferença importante na ordem das operações:</b> a marcação acontece
/// <i>depois</i> do envio. Se o serviço cair no meio, a notificação será
/// reenviada — o que é preferível a marcá-la como enviada antes e o usuário nunca
/// recebê-la. Em notificação, duplicar é melhor que perder.
/// </para>
/// </remarks>
public abstract class IdempotentNotificationConsumer<TEvent>(
    IConversationMembershipStore membershipStore,
    ISender sender)
    : IConsumer<TEvent>
    where TEvent : class, IIntegrationEvent
{
    /// <summary>Nome usado na chave de deduplicação.</summary>
    protected virtual string ConsumerName => GetType().Name;

    /// <summary>Trabalho específico do consumidor.</summary>
    protected abstract Task HandleAsync(TEvent message, CancellationToken cancellationToken);

    /// <summary>Envia um comando pelo MediatR.</summary>
    protected Task SendAsync(IRequest request, CancellationToken cancellationToken)
    {
        return sender.Send(request, cancellationToken);
    }

    /// <summary>Executa o consumo com deduplicação.</summary>
    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var cancellationToken = context.CancellationToken;

        if (await membershipStore.HasProcessedAsync(context.Message.EventId, ConsumerName, cancellationToken))
        {
            return;
        }

        await HandleAsync(context.Message, cancellationToken);

        await membershipStore.MarkProcessedAsync(context.Message.EventId, ConsumerName, cancellationToken);
    }
}

/// <summary>Notifica os participantes offline quando uma mensagem é enviada.</summary>
public sealed class MessageSentConsumer(IConversationMembershipStore membershipStore, ISender sender)
    : IdempotentNotificationConsumer<MessageSentEvent>(membershipStore, sender)
{
    /// <inheritdoc />
    protected override Task HandleAsync(MessageSentEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(new NotifyOfflineUsersCommand(message), cancellationToken);
    }
}

/// <summary>Registra a entrada de um participante na projeção local.</summary>
public sealed class ConversationJoinedConsumer(IConversationMembershipStore membershipStore, ISender sender)
    : IdempotentNotificationConsumer<ConversationJoinedEvent>(membershipStore, sender)
{
    /// <inheritdoc />
    protected override Task HandleAsync(ConversationJoinedEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new TrackConversationParticipantCommand(message.ConversationId, message.UserId, Joined: true),
            cancellationToken);
    }
}

/// <summary>Registra a saída de um participante na projeção local.</summary>
public sealed class ConversationLeftConsumer(IConversationMembershipStore membershipStore, ISender sender)
    : IdempotentNotificationConsumer<ConversationLeftEvent>(membershipStore, sender)
{
    /// <inheritdoc />
    protected override Task HandleAsync(ConversationLeftEvent message, CancellationToken cancellationToken)
    {
        return SendAsync(
            new TrackConversationParticipantCommand(message.ConversationId, message.UserId, Joined: false),
            cancellationToken);
    }
}
