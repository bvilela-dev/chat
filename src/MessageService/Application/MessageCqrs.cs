using AutoMapper;
using BuildingBlocks.Contracts;
using FluentValidation;
using MediatR;
using MessageService.Domain;

namespace MessageService.Application;

public sealed record MessageReadDto(Guid Id, Guid ConversationId, Guid SenderId, string SenderName, string Content, DateTime CreatedAtUtc);

public sealed record ConversationReadDto(Guid Id, string LastMessage, DateTime? LastMessageAtUtc, bool IsGroup, Guid? CounterpartUserId);

public sealed record ConversationSummary(Guid Id, string LastMessage, DateTime? LastMessageAtUtc, bool IsGroup, Guid? CounterpartUserId);

public sealed record MessageProjectionRequested(Guid EventId, DateTime OccurredAtUtc, Guid MessageId, Guid ConversationId, Guid SenderId, string SenderName, string Content, DateTime MessageCreatedAtUtc) : IIntegrationEvent;

public sealed record PersistMessageCommand(MessageSentEvent IntegrationEvent) : IRequest;

public sealed record CreateDirectConversationCommand(Guid InitiatorId, Guid ParticipantId) : IRequest<ConversationReadDto>;

public sealed record ProjectMessageReadModelCommand(MessageProjectionRequested Projection) : IRequest;

public sealed record UpdateConversationMembershipCommand(Guid EventId, string ConsumerName, Guid ConversationId, Guid UserId, bool Joined) : IRequest;

public sealed record GetMessagesByConversationQuery(Guid ConversationId, int Page = 1, int PageSize = 50) : IRequest<IReadOnlyCollection<MessageReadDto>>;

public sealed record GetUserConversationsQuery(Guid UserId) : IRequest<IReadOnlyCollection<ConversationReadDto>>;

public interface IMessageRepository
{
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken);

    Task<bool> MessageExistsAsync(Guid messageId, CancellationToken cancellationToken);

    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken);

    Task<ConversationSummary?> GetDirectConversationAsync(Guid firstUserId, Guid secondUserId, CancellationToken cancellationToken);

    Task AddConversationAsync(Conversation conversation, CancellationToken cancellationToken);

    Task CreateDirectConversationAsync(Guid conversationId, Guid initiatorId, Guid participantId, DateTime createdAtUtc, CancellationToken cancellationToken);

    Task<bool> HasParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    Task AddParticipantAsync(Guid conversationId, Guid userId, DateTime joinedAtUtc, CancellationToken cancellationToken);

    Task RemoveParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);

    void AddMessage(Message message);

    void EnqueueOutbox(IIntegrationEvent integrationEvent);

    Task UpsertProjectionAsync(MessageProjectionRequested projection, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<MessageReadModel>> GetMessagesByConversationAsync(Guid conversationId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ConversationSummary>> GetUserConversationsAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IMessageTelemetry
{
    void RecordConsumedEvent(string eventName);

    void RecordProjectionLag(TimeSpan lag);
}

public sealed class MessageMappingProfile : Profile
{
    public MessageMappingProfile()
    {
        CreateMap<MessageReadModel, MessageReadDto>();
    }
}

public sealed class GetMessagesByConversationQueryValidator : AbstractValidator<GetMessagesByConversationQuery>
{
    public GetMessagesByConversationQueryValidator()
    {
        RuleFor(query => query.ConversationId).NotEmpty();
        RuleFor(query => query.Page).GreaterThan(0);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetUserConversationsQueryValidator : AbstractValidator<GetUserConversationsQuery>
{
    public GetUserConversationsQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty();
    }
}

public sealed class CreateDirectConversationCommandValidator : AbstractValidator<CreateDirectConversationCommand>
{
    public CreateDirectConversationCommandValidator()
    {
        RuleFor(command => command.InitiatorId).NotEmpty();
        RuleFor(command => command.ParticipantId).NotEmpty();
        RuleFor(command => command).Must(command => command.InitiatorId != command.ParticipantId)
            .WithMessage("You cannot create a direct conversation with yourself.");
    }
}

public sealed class CreateDirectConversationCommandHandler(IMessageRepository repository, IClock clock) : IRequestHandler<CreateDirectConversationCommand, ConversationReadDto>
{
    public async Task<ConversationReadDto> Handle(CreateDirectConversationCommand request, CancellationToken cancellationToken)
    {
        var existingConversation = await repository.GetDirectConversationAsync(request.InitiatorId, request.ParticipantId, cancellationToken);
        if (existingConversation is not null)
        {
            await repository.AddParticipantAsync(existingConversation.Id, request.InitiatorId, clock.UtcNow, cancellationToken);
            await repository.AddParticipantAsync(existingConversation.Id, request.ParticipantId, clock.UtcNow, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            return new ConversationReadDto(
                existingConversation.Id,
                existingConversation.LastMessage,
                existingConversation.LastMessageAtUtc,
                existingConversation.IsGroup,
                existingConversation.CounterpartUserId);
        }

        var conversationId = Guid.NewGuid();
        await repository.CreateDirectConversationAsync(conversationId, request.InitiatorId, request.ParticipantId, clock.UtcNow, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new ConversationReadDto(conversationId, string.Empty, null, false, request.ParticipantId);
    }
}

public sealed class PersistMessageCommandHandler(IMessageRepository repository, IClock clock, IMessageTelemetry telemetry) : IRequestHandler<PersistMessageCommand>
{
    public async Task Handle(PersistMessageCommand request, CancellationToken cancellationToken)
    {
        if (await repository.MessageExistsAsync(request.IntegrationEvent.MessageId, cancellationToken))
        {
            return;
        }

        var conversation = await repository.GetConversationAsync(request.IntegrationEvent.ConversationId, cancellationToken);
        if (conversation is null)
        {
            conversation = Conversation.Create(request.IntegrationEvent.ConversationId, false, request.IntegrationEvent.OccurredAtUtc);
            await repository.AddConversationAsync(conversation, cancellationToken);
        }

        repository.AddMessage(Message.Create(
            request.IntegrationEvent.MessageId,
            request.IntegrationEvent.ConversationId,
            request.IntegrationEvent.SenderId,
            request.IntegrationEvent.SenderName,
            request.IntegrationEvent.Content,
            request.IntegrationEvent.OccurredAtUtc));

        if (!await repository.HasParticipantAsync(request.IntegrationEvent.ConversationId, request.IntegrationEvent.SenderId, cancellationToken))
        {
            await repository.AddParticipantAsync(request.IntegrationEvent.ConversationId, request.IntegrationEvent.SenderId, request.IntegrationEvent.OccurredAtUtc, cancellationToken);
        }

        repository.EnqueueOutbox(new MessageProjectionRequested(
            Guid.NewGuid(),
            clock.UtcNow,
            request.IntegrationEvent.MessageId,
            request.IntegrationEvent.ConversationId,
            request.IntegrationEvent.SenderId,
            request.IntegrationEvent.SenderName,
            request.IntegrationEvent.Content,
            request.IntegrationEvent.OccurredAtUtc));

        telemetry.RecordConsumedEvent(nameof(MessageSentEvent));
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ProjectMessageReadModelCommandHandler(IMessageRepository repository, IClock clock, IMessageTelemetry telemetry) : IRequestHandler<ProjectMessageReadModelCommand>
{
    public async Task Handle(ProjectMessageReadModelCommand request, CancellationToken cancellationToken)
    {
        await repository.UpsertProjectionAsync(request.Projection, cancellationToken);
        telemetry.RecordConsumedEvent(nameof(MessageProjectionRequested));
        telemetry.RecordProjectionLag(clock.UtcNow - request.Projection.MessageCreatedAtUtc);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class UpdateConversationMembershipCommandHandler(IMessageRepository repository, IClock clock, IMessageTelemetry telemetry) : IRequestHandler<UpdateConversationMembershipCommand>
{
    public async Task Handle(UpdateConversationMembershipCommand request, CancellationToken cancellationToken)
    {
        if (request.Joined)
        {
            if (!await repository.HasParticipantAsync(request.ConversationId, request.UserId, cancellationToken))
            {
                await repository.AddParticipantAsync(request.ConversationId, request.UserId, clock.UtcNow, cancellationToken);
            }
        }
        telemetry.RecordConsumedEvent(request.Joined ? nameof(ConversationJoinedEvent) : nameof(ConversationLeftEvent));
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetMessagesByConversationQueryHandler(IMessageRepository repository, IMapper mapper) : IRequestHandler<GetMessagesByConversationQuery, IReadOnlyCollection<MessageReadDto>>
{
    public async Task<IReadOnlyCollection<MessageReadDto>> Handle(GetMessagesByConversationQuery request, CancellationToken cancellationToken)
    {
        var messages = await repository.GetMessagesByConversationAsync(request.ConversationId, request.Page, request.PageSize, cancellationToken);
        return mapper.Map<IReadOnlyCollection<MessageReadDto>>(messages);
    }
}

public sealed class GetUserConversationsQueryHandler(IMessageRepository repository) : IRequestHandler<GetUserConversationsQuery, IReadOnlyCollection<ConversationReadDto>>
{
    public async Task<IReadOnlyCollection<ConversationReadDto>> Handle(GetUserConversationsQuery request, CancellationToken cancellationToken)
    {
        var conversations = await repository.GetUserConversationsAsync(request.UserId, cancellationToken);
        return conversations
            .Where(conversation => conversation.IsGroup || conversation.CounterpartUserId is not null)
            .GroupBy(conversation => conversation.IsGroup
                ? $"group:{conversation.Id}"
                : $"direct:{conversation.CounterpartUserId}")
            .Select(group => group
                .OrderByDescending(conversation => conversation.LastMessageAtUtc ?? DateTime.MinValue)
                .First())
            .OrderByDescending(conversation => conversation.LastMessageAtUtc ?? DateTime.MinValue)
            .Select(conversation => new ConversationReadDto(
                conversation.Id,
                conversation.LastMessage,
                conversation.LastMessageAtUtc,
                conversation.IsGroup,
                conversation.CounterpartUserId))
            .ToArray();
    }
}