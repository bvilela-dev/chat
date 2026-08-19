using BuildingBlocks.Application;
using BuildingBlocks.Contracts;
using FluentValidation;
using MediatR;
using PresenceService.Application.Abstractions;

namespace PresenceService.Application.Presence;

/// <summary>Estado de presença exposto pela API.</summary>
public sealed record UserStatusDto(Guid UserId, bool IsOnline, DateTime? LastSeenAtUtc);

/// <summary>
/// Comando que marca o usuário autenticado como online.
/// </summary>
/// <param name="UserId">
/// Usuário, sempre derivado do token JWT — nunca de um parâmetro de rota.
/// </param>
/// <remarks>
/// <para>
/// <b>Correção de segurança: IDOR na presença.</b> A rota anterior era
/// <c>POST /api/presence/online/{userId}</c>, sem verificar se o
/// <c>userId</c> correspondia ao dono do token. Qualquer usuário autenticado
/// podia manipular o estado de presença de qualquer outro:
/// </para>
/// <code>
/// # Marcar um colega como offline, fazendo-o parecer indisponível
/// POST /presence/api/presence/offline/{guid-de-outra-pessoa}
///
/// # Ou como online, forjando disponibilidade
/// POST /presence/api/presence/online/{guid-de-outra-pessoa}
/// </code>
/// <para>
/// O impacto vai além do incômodo: presença alimenta a decisão do Notification
/// Service sobre enviar ou não notificação de mensagem. Manter uma vítima
/// permanentemente "online" suprimiria todas as notificações dela.
/// </para>
/// <para>
/// A correção seguiu o mesmo princípio aplicado às conversas: <b>remover o
/// parâmetro</b>. As rotas viraram <c>POST /api/presence/me/online</c> e
/// <c>/me/offline</c>. Sem identificador na entrada, não há o que falsificar.
/// </para>
/// </remarks>
public sealed record SetUserOnlineCommand(Guid UserId) : IRequest<UserStatusDto>;

/// <summary>Comando que marca o usuário autenticado como offline.</summary>
public sealed record SetUserOfflineCommand(Guid UserId) : IRequest<UserStatusDto>;

/// <summary>Valida o comando de entrada online.</summary>
public sealed class SetUserOnlineCommandValidator : AbstractValidator<SetUserOnlineCommand>
{
    /// <summary>Configura as regras.</summary>
    public SetUserOnlineCommandValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>Valida o comando de saída offline.</summary>
public sealed class SetUserOfflineCommandValidator : AbstractValidator<SetUserOfflineCommand>
{
    /// <summary>Configura as regras.</summary>
    public SetUserOfflineCommandValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>Marca o usuário como online e anuncia o fato no barramento.</summary>
public sealed class SetUserOnlineCommandHandler(
    IPresenceStore store,
    IPresenceEventPublisher publisher,
    IClock clock,
    IPresenceTelemetry telemetry)
    : IRequestHandler<SetUserOnlineCommand, UserStatusDto>
{
    /// <inheritdoc />
    public async Task<UserStatusDto> Handle(SetUserOnlineCommand request, CancellationToken cancellationToken)
    {
        var utcNow = clock.UtcNow;
        var presence = await store.SetOnlineAsync(request.UserId, utcNow, cancellationToken);

        await publisher.PublishAsync(
            new UserOnlineEvent(Guid.NewGuid(), utcNow, request.UserId),
            cancellationToken);

        telemetry.RecordCommand(nameof(SetUserOnlineCommand));

        return new UserStatusDto(presence.UserId, presence.IsOnline, presence.LastSeenAtUtc);
    }
}

/// <summary>Marca o usuário como offline e anuncia o fato no barramento.</summary>
public sealed class SetUserOfflineCommandHandler(
    IPresenceStore store,
    IPresenceEventPublisher publisher,
    IClock clock,
    IPresenceTelemetry telemetry)
    : IRequestHandler<SetUserOfflineCommand, UserStatusDto>
{
    /// <inheritdoc />
    public async Task<UserStatusDto> Handle(SetUserOfflineCommand request, CancellationToken cancellationToken)
    {
        // Um único instante para o estado e para o evento: usar leituras
        // separadas do relógio faria "ficou offline" e "visto por último"
        // divergirem por alguns milissegundos, sem motivo.
        var utcNow = clock.UtcNow;

        var presence = await store.SetOfflineAsync(request.UserId, utcNow, cancellationToken);

        await publisher.PublishAsync(
            new UserOfflineEvent(Guid.NewGuid(), utcNow, request.UserId, utcNow),
            cancellationToken);

        telemetry.RecordCommand(nameof(SetUserOfflineCommand));

        return new UserStatusDto(presence.UserId, presence.IsOnline, presence.LastSeenAtUtc);
    }
}
