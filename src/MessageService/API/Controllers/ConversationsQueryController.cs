using MediatR;
using MessageService.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessageService.API.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class ConversationsQueryController(ISender sender) : ControllerBase
{
    [HttpGet("{userId:guid}/conversations")]
    public Task<IReadOnlyCollection<ConversationReadDto>> GetConversations(Guid userId, CancellationToken cancellationToken)
    {
        return sender.Send(new GetUserConversationsQuery(userId), cancellationToken);
    }
}