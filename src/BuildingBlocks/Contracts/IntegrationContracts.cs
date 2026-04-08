namespace BuildingBlocks.Contracts;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
}

public abstract record IntegrationEvent(Guid EventId, DateTime OccurredAtUtc) : IIntegrationEvent;

public sealed record UserCreatedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    string Name,
    string Email) : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record MessageSentEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid MessageId,
    Guid ConversationId,
    Guid SenderId,
    string SenderName,
    string Content) : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record ConversationJoinedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ConversationId,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record ConversationLeftEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ConversationId,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record UserOnlineEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId) : IntegrationEvent(EventId, OccurredAtUtc);

public sealed record UserOfflineEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    DateTime LastSeenAtUtc) : IntegrationEvent(EventId, OccurredAtUtc);

public static class MessagingConstants
{
    public const string ChatPersistQueue = "chat.persist";
    public const string MessageProjectionQueue = "message.projection";
    public const string NotificationQueue = "notification.message-sent";
    public const string PresenceOnlineQueue = "presence.online";
    public const string PresenceOfflineQueue = "presence.offline";
    public const string UserCreatedQueue = "identity.user-created";
    public const string ChatRedisChannel = "chat-messages";
}