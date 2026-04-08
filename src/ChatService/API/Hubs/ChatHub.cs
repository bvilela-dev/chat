using System.Security.Claims;
using ChatService.Application;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ChatService.API.Hubs;

public sealed record SendMessageRequest(Guid ConversationId, string Content);

[Authorize]
public sealed class ChatHub(ISender sender, IConnectionRegistry connectionRegistry, IChatTelemetry telemetry, ILogger<ChatHub> logger) : Hub
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

    public Task<ChatRealtimeMessage> SendMessage(SendMessageRequest request)
    {
        return ExecuteAsync(
            nameof(SendMessage),
            () => sender.Send(new SendMessageCommand(request.ConversationId, GetUserId(), GetUserName(), request.Content), Context.ConnectionAborted));
    }

    public Task JoinConversation(Guid conversationId)
    {
        return ExecuteAsync(
            nameof(JoinConversation),
            () => sender.Send(new JoinConversationCommand(conversationId, GetUserId(), Context.ConnectionId), Context.ConnectionAborted));
    }

    public Task LeaveConversation(Guid conversationId)
    {
        return ExecuteAsync(
            nameof(LeaveConversation),
            () => sender.Send(new LeaveConversationCommand(conversationId, GetUserId(), Context.ConnectionId), Context.ConnectionAborted));
    }

    private async Task ExecuteAsync(string operationName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{OperationName} failed for connection {ConnectionId} and user {UserId}", operationName, Context.ConnectionId, Context.UserIdentifier ?? GetUserId().ToString());
            throw;
        }
    }

    private async Task<TResponse> ExecuteAsync<TResponse>(string operationName, Func<Task<TResponse>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{OperationName} failed for connection {ConnectionId} and user {UserId}", operationName, Context.ConnectionId, Context.UserIdentifier ?? GetUserId().ToString());
            throw;
        }
    }

    private Guid GetUserId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : throw new HubException("Missing user identifier.");
    }

    private string GetUserName()
    {
        return Context.User?.FindFirstValue(ClaimTypes.Name)
            ?? Context.User?.FindFirstValue("unique_name")
            ?? Context.User?.FindFirstValue("name")
            ?? GetUserId().ToString();
    }
}