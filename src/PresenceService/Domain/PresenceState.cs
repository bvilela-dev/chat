namespace PresenceService.Domain;

public sealed record UserPresence(Guid UserId, bool IsOnline, DateTime? LastSeenAtUtc);