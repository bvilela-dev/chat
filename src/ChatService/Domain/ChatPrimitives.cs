namespace ChatService.Domain;

public sealed record ChatConnection(Guid UserId, string ConnectionId);

public sealed record ConversationMembership(Guid ConversationId, Guid UserId);