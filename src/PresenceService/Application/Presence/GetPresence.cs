using MediatR;
using PresenceService.Application.Abstractions;

namespace PresenceService.Application.Presence;

/// <summary>Query do estado de presença de um usuário.</summary>
/// <remarks>
/// Ao contrário dos comandos, a consulta aceita o identificador de outro
/// usuário — e isso é correto: saber se um contato está online é justamente a
/// função do recurso. A assimetria é o ponto: <b>ler</b> a presença alheia é
/// legítimo; <b>alterá-la</b> não é.
/// </remarks>
public sealed record GetUserStatusQuery(Guid UserId) : IRequest<UserStatusDto>;

/// <summary>Query que lista todos os usuários online.</summary>
public sealed record GetOnlineUsersQuery : IRequest<IReadOnlyCollection<UserStatusDto>>;

/// <summary>Consulta o estado de um usuário.</summary>
public sealed class GetUserStatusQueryHandler(IPresenceStore store)
    : IRequestHandler<GetUserStatusQuery, UserStatusDto>
{
    /// <inheritdoc />
    public async Task<UserStatusDto> Handle(GetUserStatusQuery request, CancellationToken cancellationToken)
    {
        var presence = await store.GetStatusAsync(request.UserId, cancellationToken);
        return new UserStatusDto(presence.UserId, presence.IsOnline, presence.LastSeenAtUtc);
    }
}

/// <summary>Lista os usuários online.</summary>
public sealed class GetOnlineUsersQueryHandler(IPresenceStore store)
    : IRequestHandler<GetOnlineUsersQuery, IReadOnlyCollection<UserStatusDto>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyCollection<UserStatusDto>> Handle(
        GetOnlineUsersQuery request,
        CancellationToken cancellationToken)
    {
        var users = await store.GetOnlineAsync(cancellationToken);
        return [.. users.Select(user => new UserStatusDto(user.UserId, user.IsOnline, user.LastSeenAtUtc))];
    }
}
