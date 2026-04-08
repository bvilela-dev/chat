using BuildingBlocks.Contracts;
using FluentValidation;
using MediatR;

namespace ChatService.Application;

public sealed record ValidatedUser(Guid Id, string Name, string Email);

public sealed record ChatRealtimeMessage(Guid MessageId, Guid ConversationId, Guid SenderId, string SenderName, string Content, DateTime CreatedAtUtc);

public sealed record SendMessageCommand(Guid ConversationId, Guid UserId, string Content) : IRequest<ChatRealtimeMessage>;

public sealed record JoinConversationCommand(Guid ConversationId, Guid UserId, string ConnectionId) : IRequest;

public sealed record LeaveConversationCommand(Guid ConversationId, Guid UserId, string ConnectionId) : IRequest;

public interface IIdentityValidationClient
{
    Task<ValidatedUser?> ValidateAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IConversationNotifier
{
    Task BroadcastMessageAsync(Guid conversationId, ChatRealtimeMessage message, CancellationToken cancellationToken);

    Task AddConnectionToConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken);

    Task RemoveConnectionFromConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken);
}

public interface IConnectionRegistry
{
    Task RegisterConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken);

    Task RemoveConnectionAsync(Guid userId, string connectionId, CancellationToken cancellationToken);
}

public interface IChatEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent;
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IChatTelemetry
{
    void IncrementCommand(string commandName);

    void ConnectionOpened();

    void ConnectionClosed();
}

public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Content).NotEmpty().MaximumLength(4000);
    }
}

public sealed class JoinConversationCommandValidator : AbstractValidator<JoinConversationCommand>
{
    public JoinConversationCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.ConnectionId).NotEmpty();
    }
}

public sealed class LeaveConversationCommandValidator : AbstractValidator<LeaveConversationCommand>
{
    public LeaveConversationCommandValidator()
    {
        RuleFor(command => command.ConversationId).NotEmpty();
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.ConnectionId).NotEmpty();
    }
}

public sealed class SendMessageCommandHandler(IIdentityValidationClient validationClient, IChatEventPublisher publisher, IConversationNotifier notifier, IClock clock, IChatTelemetry telemetry)
    : IRequestHandler<SendMessageCommand, ChatRealtimeMessage>
{
    public async Task<ChatRealtimeMessage> Handle(SendMessageCommand request, CancellationToken cancellationToken)
    {
        var user = await validationClient.ValidateAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("User validation failed.");

        var createdAtUtc = clock.UtcNow;
        var messageId = Guid.NewGuid();
        var message = new ChatRealtimeMessage(messageId, request.ConversationId, user.Id, user.Name, request.Content.Trim(), createdAtUtc);

        await publisher.PublishAsync(
            new MessageSentEvent(Guid.NewGuid(), createdAtUtc, messageId, request.ConversationId, user.Id, user.Name, request.Content.Trim()),
            cancellationToken);

        await notifier.BroadcastMessageAsync(request.ConversationId, message, cancellationToken);
        telemetry.IncrementCommand(nameof(SendMessageCommand));

        return message;
    }
}

public sealed class JoinConversationCommandHandler(IIdentityValidationClient validationClient, IConversationNotifier notifier, IChatEventPublisher publisher, IClock clock, IChatTelemetry telemetry)
    : IRequestHandler<JoinConversationCommand>
{
    public async Task Handle(JoinConversationCommand request, CancellationToken cancellationToken)
    {
        var user = await validationClient.ValidateAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("User validation failed.");

        await notifier.AddConnectionToConversationAsync(request.ConnectionId, request.ConversationId, cancellationToken);
        await publisher.PublishAsync(new ConversationJoinedEvent(Guid.NewGuid(), clock.UtcNow, request.ConversationId, user.Id), cancellationToken);
        telemetry.IncrementCommand(nameof(JoinConversationCommand));
    }
}

public sealed class LeaveConversationCommandHandler(IConversationNotifier notifier, IChatEventPublisher publisher, IClock clock, IChatTelemetry telemetry)
    : IRequestHandler<LeaveConversationCommand>
{
    public async Task Handle(LeaveConversationCommand request, CancellationToken cancellationToken)
    {
        await notifier.RemoveConnectionFromConversationAsync(request.ConnectionId, request.ConversationId, cancellationToken);
        await publisher.PublishAsync(new ConversationLeftEvent(Guid.NewGuid(), clock.UtcNow, request.ConversationId, request.UserId), cancellationToken);
        telemetry.IncrementCommand(nameof(LeaveConversationCommand));
    }
}