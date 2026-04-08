using System.Security.Claims;
using ChatService.Application;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChatService.API.Hubs;

public sealed record SendMessageRequest(Guid ConversationId, string Content);

[Authorize]
public sealed class ChatHub(ISender sender, IConnectionRegistry connectionRegistry, IChatTelemetry telemetry) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        await connectionRegistry.RegisterConnectionAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
        telemetry.ConnectionOpened();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        await connectionRegistry.RemoveConnectionAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
        telemetry.ConnectionClosed();
        await base.OnDisconnectedAsync(exception);
    }

    public Task<ChatRealtimeMessage> SendMessage(SendMessageRequest request, CancellationToken cancellationToken)
    {
        return sender.Send(new SendMessageCommand(request.ConversationId, GetUserId(), request.Content), cancellationToken);
    }

    public Task JoinConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        return sender.Send(new JoinConversationCommand(conversationId, GetUserId(), Context.ConnectionId), cancellationToken);
    }

    public Task LeaveConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        return sender.Send(new LeaveConversationCommand(conversationId, GetUserId(), Context.ConnectionId), cancellationToken);
    }

    private Guid GetUserId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : throw new HubException("Missing user identifier.");
    }
}