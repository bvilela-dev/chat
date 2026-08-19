using BuildingBlocks.AspNetCore;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresenceService.Application.Presence;

namespace PresenceService.API.Controllers;

/// <summary>
/// Estado de presença (online/offline) dos usuários.
/// </summary>
/// <remarks>
/// <para>
/// <b>Assimetria deliberada entre leitura e escrita.</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Escrita</b> só afeta o próprio usuário: as rotas <c>/me/online</c> e
///   <c>/me/offline</c> não aceitam identificador. É o que impede alguém de
///   manipular a presença alheia.
///   </description></item>
///   <item><description>
///   <b>Leitura</b> aceita o identificador de qualquer usuário — é exatamente a
///   função do recurso, mostrar quais contatos estão disponíveis.
///   </description></item>
/// </list>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/presence")]
[Produces("application/json")]
public sealed class PresenceController(ISender sender) : ControllerBase
{
    /// <summary>Marca o usuário autenticado como online (também funciona como heartbeat).</summary>
    /// <remarks>
    /// O frontend chama este endpoint periodicamente. Cada chamada renova o TTL
    /// do registro no Redis; o silêncio prolongado faz o usuário expirar
    /// naturalmente para offline, sem depender de um encerramento bem-comportado.
    /// </remarks>
    [HttpPost("me/online")]
    [ProducesResponseType<UserStatusDto>(StatusCodes.Status200OK)]
    public Task<UserStatusDto> SetSelfOnline(CancellationToken cancellationToken)
    {
        return sender.Send(new SetUserOnlineCommand(User.GetRequiredUserId()), cancellationToken);
    }

    /// <summary>Marca o usuário autenticado como offline.</summary>
    [HttpPost("me/offline")]
    [ProducesResponseType<UserStatusDto>(StatusCodes.Status200OK)]
    public Task<UserStatusDto> SetSelfOffline(CancellationToken cancellationToken)
    {
        return sender.Send(new SetUserOfflineCommand(User.GetRequiredUserId()), cancellationToken);
    }

    /// <summary>Lista os usuários atualmente online.</summary>
    [HttpGet("online")]
    [ProducesResponseType<IReadOnlyCollection<UserStatusDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyCollection<UserStatusDto>> GetOnline(CancellationToken cancellationToken)
    {
        return sender.Send(new GetOnlineUsersQuery(), cancellationToken);
    }

    /// <summary>Consulta o estado de presença de um usuário específico.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType<UserStatusDto>(StatusCodes.Status200OK)]
    public Task<UserStatusDto> GetStatus(Guid userId, CancellationToken cancellationToken)
    {
        return sender.Send(new GetUserStatusQuery(userId), cancellationToken);
    }
}
