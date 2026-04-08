using System.Security.Claims;
using MediatR;
using MessageService.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessageService.API.Controllers;

public sealed record CreateDirectConversationRequest(Guid ParticipantId);

[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class ConversationCommandsController(ISender sender) : ControllerBase
{
    [HttpPost("direct")]
    public Task<ConversationReadDto> CreateDirectConversation([FromBody] CreateDirectConversationRequest request, CancellationToken cancellationToken)
    {
        var initiatorId = GetUserId();
        return sender.Send(new CreateDirectConversationCommand(initiatorId, request.ParticipantId), cancellationToken);
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedAccessException("Missing user identifier.");
    }
}