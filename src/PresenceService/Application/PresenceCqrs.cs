using BuildingBlocks.Contracts;
using FluentValidation;
using MediatR;
using PresenceService.Domain;

namespace PresenceService.Application;

public sealed record UserStatusDto(Guid UserId, bool IsOnline, DateTime? LastSeenAtUtc);

public sealed record SetUserOnlineCommand(Guid UserId) : IRequest<UserStatusDto>;

public sealed record SetUserOfflineCommand(Guid UserId) : IRequest<UserStatusDto>;

public sealed record GetUserStatusQuery(Guid UserId) : IRequest<UserStatusDto>;

public interface IPresenceStore
{
    Task<UserPresence> SetOnlineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken);

    Task<UserPresence> SetOfflineAsync(Guid userId, DateTime occurredAtUtc, CancellationToken cancellationToken);

    Task<UserPresence> GetStatusAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IPresenceEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class, IIntegrationEvent;
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IPresenceTelemetry
{
    void RecordCommand(string commandName);
}

public sealed class SetUserOnlineCommandValidator : AbstractValidator<SetUserOnlineCommand>
{
    public SetUserOnlineCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class SetUserOfflineCommandValidator : AbstractValidator<SetUserOfflineCommand>
{
    public SetUserOfflineCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class GetUserStatusQueryValidator : AbstractValidator<GetUserStatusQuery>
{
    public GetUserStatusQueryValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
    }
}

public sealed class SetUserOnlineCommandHandler(IPresenceStore store, IPresenceEventPublisher publisher, IClock clock, IPresenceTelemetry telemetry) : IRequestHandler<SetUserOnlineCommand, UserStatusDto>
{
    public async Task<UserStatusDto> Handle(SetUserOnlineCommand request, CancellationToken cancellationToken)
    {
        var status = await store.SetOnlineAsync(request.UserId, clock.UtcNow, cancellationToken);
        await publisher.PublishAsync(new UserOnlineEvent(Guid.NewGuid(), clock.UtcNow, request.UserId), cancellationToken);
        telemetry.RecordCommand(nameof(SetUserOnlineCommand));
        return new UserStatusDto(status.UserId, status.IsOnline, status.LastSeenAtUtc);
    }
}

public sealed class SetUserOfflineCommandHandler(IPresenceStore store, IPresenceEventPublisher publisher, IClock clock, IPresenceTelemetry telemetry) : IRequestHandler<SetUserOfflineCommand, UserStatusDto>
{
    public async Task<UserStatusDto> Handle(SetUserOfflineCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var status = await store.SetOfflineAsync(request.UserId, now, cancellationToken);
        await publisher.PublishAsync(new UserOfflineEvent(Guid.NewGuid(), now, request.UserId, now), cancellationToken);
        telemetry.RecordCommand(nameof(SetUserOfflineCommand));
        return new UserStatusDto(status.UserId, status.IsOnline, status.LastSeenAtUtc);
    }
}

public sealed class GetUserStatusQueryHandler(IPresenceStore store) : IRequestHandler<GetUserStatusQuery, UserStatusDto>
{
    public async Task<UserStatusDto> Handle(GetUserStatusQuery request, CancellationToken cancellationToken)
    {
        var status = await store.GetStatusAsync(request.UserId, cancellationToken);
        return new UserStatusDto(status.UserId, status.IsOnline, status.LastSeenAtUtc);
    }
}