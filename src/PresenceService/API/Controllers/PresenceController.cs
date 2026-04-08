using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PresenceService.Application;

namespace PresenceService.API.Controllers;

[ApiController]
[Authorize]
[Route("api/presence")]
public sealed class PresenceController(ISender sender) : ControllerBase
{
    [HttpPost("online/{userId:guid}")]
    public Task<UserStatusDto> SetOnline(Guid userId, CancellationToken cancellationToken)
    {
        return sender.Send(new SetUserOnlineCommand(userId), cancellationToken);
    }

    [HttpPost("offline/{userId:guid}")]
    public Task<UserStatusDto> SetOffline(Guid userId, CancellationToken cancellationToken)
    {
        return sender.Send(new SetUserOfflineCommand(userId), cancellationToken);
    }

    [HttpGet("{userId:guid}")]
    public Task<UserStatusDto> GetStatus(Guid userId, CancellationToken cancellationToken)
    {
        return sender.Send(new GetUserStatusQuery(userId), cancellationToken);
    }
}