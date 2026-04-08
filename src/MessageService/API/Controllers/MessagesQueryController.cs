using MediatR;
using MessageService.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessageService.API.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class MessagesQueryController(ISender sender) : ControllerBase
{
    [HttpGet("{conversationId:guid}/messages")]
    public Task<IReadOnlyCollection<MessageReadDto>> GetMessages(Guid conversationId, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
    {
        return sender.Send(new GetMessagesByConversationQuery(conversationId, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), cancellationToken);
    }
}