using ChatService.API.Hubs;
using ChatService.Application;
using Microsoft.AspNetCore.SignalR;

namespace ChatService.API.Services;

public sealed class SignalRConversationNotifier(IHubContext<ChatHub> hubContext) : IConversationNotifier
{
    public Task BroadcastMessageAsync(Guid conversationId, ChatRealtimeMessage message, CancellationToken cancellationToken)
    {
        return hubContext.Clients.Group(conversationId.ToString()).SendAsync("messageReceived", message, cancellationToken);
    }

    public Task AddConnectionToConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken)
    {
        return hubContext.Groups.AddToGroupAsync(connectionId, conversationId.ToString(), cancellationToken);
    }

    public Task RemoveConnectionFromConversationAsync(string connectionId, Guid conversationId, CancellationToken cancellationToken)
    {
        return hubContext.Groups.RemoveFromGroupAsync(connectionId, conversationId.ToString(), cancellationToken);
    }
}