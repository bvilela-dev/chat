namespace IdentityService.Domain;

public sealed class User
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private User()
    {
    }

    private User(Guid id, string name, string email, string passwordHash, DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens;

    public static User Register(string name, string email, string passwordHash, DateTime createdAtUtc)
    {
        return new User(Guid.NewGuid(), name.Trim(), email.Trim().ToLowerInvariant(), passwordHash, createdAtUtc);
    }

    public RefreshToken IssueRefreshToken(string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        var refreshToken = RefreshToken.Create(Id, token, expiresAtUtc, createdAtUtc);
        _refreshTokens.Add(refreshToken);
        return refreshToken;
    }

    public RefreshToken? GetActiveRefreshToken(string token, DateTime utcNow)
    {
        return _refreshTokens.SingleOrDefault(candidate => candidate.Token == token && candidate.IsActive(utcNow));
    }
}

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    private RefreshToken(Guid id, Guid userId, string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        Id = id;
        UserId = userId;
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Token { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public bool IsRevoked { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public static RefreshToken Create(Guid userId, string token, DateTime expiresAtUtc, DateTime createdAtUtc)
    {
        return new RefreshToken(Guid.NewGuid(), userId, token, expiresAtUtc, createdAtUtc);
    }

    public bool IsActive(DateTime utcNow)
    {
        return !IsRevoked && ExpiresAtUtc > utcNow;
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        IsRevoked = true;
        RevokedAtUtc = revokedAtUtc;
    }
}

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredOnUtc { get; private set; }

    public DateTime? ProcessedOnUtc { get; private set; }

    public string? Error { get; private set; }

    public int RetryCount { get; private set; }

    public static OutboxMessage Create(Guid id, string type, string payload, DateTime occurredOnUtc)
    {
        return new OutboxMessage
        {
            Id = id,
            Type = type,
            Payload = payload,
            OccurredOnUtc = occurredOnUtc
        };
    }

    public void MarkProcessed(DateTime processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Error = error;
        RetryCount++;
    }
}