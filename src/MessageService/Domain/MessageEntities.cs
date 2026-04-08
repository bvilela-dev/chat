namespace MessageService.Domain;

public sealed class Conversation
{
    private Conversation()
    {
    }

    private Conversation(Guid id, bool isGroup, DateTime createdAtUtc)
    {
        Id = id;
        IsGroup = isGroup;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public bool IsGroup { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static Conversation Create(Guid id, bool isGroup, DateTime createdAtUtc)
    {
        return new Conversation(id, isGroup, createdAtUtc);
    }
}

public sealed class Message
{
    private Message()
    {
    }

    private Message(Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        SenderId = senderId;
        SenderName = senderName;
        Content = content;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid SenderId { get; private set; }

    public string SenderName { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public static Message Create(Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        return new Message(id, conversationId, senderId, senderName, content, createdAtUtc);
    }
}

public sealed class ConversationParticipant
{
    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTime JoinedAtUtc { get; private set; }

    public static ConversationParticipant Create(Guid conversationId, Guid userId, DateTime joinedAtUtc)
    {
        return new ConversationParticipant
        {
            ConversationId = conversationId,
            UserId = userId,
            JoinedAtUtc = joinedAtUtc
        };
    }
}

public sealed class MessageReadModel
{
    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid SenderId { get; private set; }

    public string SenderName { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public static MessageReadModel Create(Guid id, Guid conversationId, Guid senderId, string senderName, string content, DateTime createdAtUtc)
    {
        return new MessageReadModel
        {
            Id = id,
            ConversationId = conversationId,
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            CreatedAtUtc = createdAtUtc
        };
    }
}

public sealed class ConversationReadModel
{
    public Guid Id { get; private set; }

    public string LastMessage { get; private set; } = string.Empty;

    public DateTime? LastMessageAtUtc { get; private set; }

    public void Update(string lastMessage, DateTime lastMessageAtUtc)
    {
        LastMessage = lastMessage;
        LastMessageAtUtc = lastMessageAtUtc;
    }

    public static ConversationReadModel Create(Guid id, string lastMessage, DateTime lastMessageAtUtc)
    {
        var model = new ConversationReadModel { Id = id };
        model.Update(lastMessage, lastMessageAtUtc);
        return model;
    }
}

public sealed class ConversationParticipantReadModel
{
    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public static ConversationParticipantReadModel Create(Guid conversationId, Guid userId)
    {
        return new ConversationParticipantReadModel
        {
            ConversationId = conversationId,
            UserId = userId
        };
    }
}

public sealed class OutboxMessage
{
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

public sealed class InboxMessage
{
    public Guid EventId { get; private set; }

    public string ConsumerName { get; private set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; private set; }

    public static InboxMessage Create(Guid eventId, string consumerName, DateTime processedAtUtc)
    {
        return new InboxMessage
        {
            EventId = eventId,
            ConsumerName = consumerName,
            ProcessedAtUtc = processedAtUtc
        };
    }
}