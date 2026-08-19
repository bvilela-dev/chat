using BuildingBlocks.Contracts;
using MediatR;
using NotificationService.Application.Abstractions;

namespace NotificationService.Application.Notifications;

/// <summary>
/// Comando que notifica os participantes offline de uma nova mensagem.
/// </summary>
public sealed record NotifyOfflineUsersCommand(MessageSentEvent IntegrationEvent) : IRequest;

/// <summary>
/// Percorre os participantes da conversa e notifica quem não está conectado.
/// </summary>
/// <remarks>
/// <b>A regra de negócio central:</b> só recebe notificação quem está offline.
/// Quem está com o chat aberto já viu a mensagem chegar em tempo real pelo
/// SignalR — notificá-lo seria ruído duplicado, o comportamento que faz usuários
/// desativarem notificações de um aplicativo.
/// </remarks>
public sealed class NotifyOfflineUsersCommandHandler(
    IConversationMembershipStore membershipStore,
    IPresenceLookup presenceLookup,
    INotificationSender notificationSender,
    INotificationTelemetry telemetry)
    : IRequestHandler<NotifyOfflineUsersCommand>
{
    /// <inheritdoc />
    public async Task Handle(NotifyOfflineUsersCommand request, CancellationToken cancellationToken)
    {
        var integrationEvent = request.IntegrationEvent;

        var participants = await membershipStore.GetParticipantsAsync(
            integrationEvent.ConversationId,
            cancellationToken);

        // Nunca notificar o próprio remetente.
        var recipients = participants
            .Where(participantId => participantId != integrationEvent.SenderId)
            .ToArray();

        if (recipients.Length == 0)
        {
            telemetry.RecordEvent(nameof(MessageSentEvent));
            return;
        }

        var notificationText = $"{integrationEvent.SenderName}: {integrationEvent.Content}";

        foreach (var recipientId in recipients)
        {
            if (await presenceLookup.IsOnlineAsync(recipientId, cancellationToken))
            {
                continue;
            }

            // As notificações são enviadas em sequência, de propósito.
            //
            // Paralelizar com Task.WhenAll pareceria mais rápido, mas em uma
            // conversa em grupo grande dispararia dezenas de chamadas simultâneas
            // ao provedor de push — que responderia com 429 e faria a mensagem
            // inteira ser reprocessada. O caminho correto para volume alto é
            // enfileirar cada notificação como uma mensagem própria, e não
            // aumentar a concorrência aqui dentro.
            await notificationSender.SendPushAsync(recipientId, notificationText, cancellationToken);
            telemetry.RecordNotificationSent("push");

            await notificationSender.SendEmailAsync(
                recipientId,
                subject: "Nova mensagem no chat",
                notificationText,
                cancellationToken);
            telemetry.RecordNotificationSent("email");
        }

        telemetry.RecordEvent(nameof(MessageSentEvent));
    }
}
