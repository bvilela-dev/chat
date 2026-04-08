using BuildingBlocks.Contracts;
using MediatR;

namespace NotificationService.Application;

public sealed record NotifyOfflineUsersCommand(MessageSentEvent IntegrationEvent) : IRequest;

public sealed record TrackConversationParticipantCommand(Guid EventId, string ConsumerName, Guid ConversationId, Guid UserId, bool Joined) : IRequest;

public interface IConversationMembershipStore
{
    Task AddParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Guid>> GetParticipantsAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);
}

public interface IPresenceLookup
{
    Task<bool> IsOnlineAsync(Guid userId, CancellationToken cancellationToken);
}

public interface INotificationSender
{
    Task SendPushAsync(Guid userId, string message, CancellationToken cancellationToken);

    Task SendEmailAsync(Guid userId, string subject, string message, CancellationToken cancellationToken);
}

public interface INotificationTelemetry
{
    void RecordEvent(string eventName);
}

public sealed class NotifyOfflineUsersCommandHandler(IConversationMembershipStore membershipStore, IPresenceLookup presenceLookup, INotificationSender notificationSender, INotificationTelemetry telemetry) : IRequestHandler<NotifyOfflineUsersCommand>
{
    public async Task Handle(NotifyOfflineUsersCommand request, CancellationToken cancellationToken)
    {
        var participants = await membershipStore.GetParticipantsAsync(request.IntegrationEvent.ConversationId, cancellationToken);

        foreach (var participantId in participants.Where(participantId => participantId != request.IntegrationEvent.SenderId))
        {
            if (!await presenceLookup.IsOnlineAsync(participantId, cancellationToken))
            {
                var message = $"{request.IntegrationEvent.SenderName}: {request.IntegrationEvent.Content}";
                await notificationSender.SendPushAsync(participantId, message, cancellationToken);
                await notificationSender.SendEmailAsync(participantId, "New chat message", message, cancellationToken);
            }
        }

        telemetry.RecordEvent(nameof(MessageSentEvent));
    }
}

public sealed class TrackConversationParticipantCommandHandler(IConversationMembershipStore membershipStore, INotificationTelemetry telemetry) : IRequestHandler<TrackConversationParticipantCommand>
{
    public async Task Handle(TrackConversationParticipantCommand request, CancellationToken cancellationToken)
    {
        if (await membershipStore.HasProcessedAsync(request.EventId, request.ConsumerName, cancellationToken))
        {
            return;
        }

        if (request.Joined)
        {
            await membershipStore.AddParticipantAsync(request.ConversationId, request.UserId, cancellationToken);
        }
        else
        {
            await membershipStore.RemoveParticipantAsync(request.ConversationId, request.UserId, cancellationToken);
        }

        await membershipStore.MarkProcessedAsync(request.EventId, request.ConsumerName, cancellationToken);
        telemetry.RecordEvent(request.Joined ? nameof(ConversationJoinedEvent) : nameof(ConversationLeftEvent));
    }
}