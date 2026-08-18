using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CentralChat.Infrastructure;

[Authorize]
public sealed class CommunicationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId is not null) await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await Groups.AddToGroupAsync(Context.ConnectionId, "unassigned");
        await base.OnConnectedAsync();
    }

    public Task JoinConversation(Guid conversationId) => Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
}

public interface IRealtimeNotifier
{
    Task UserAsync(Guid userId, string name, object payload, CancellationToken cancellationToken);
    Task UnassignedAsync(string name, object payload, CancellationToken cancellationToken);
    Task ConversationAsync(Guid conversationId, string name, object payload, CancellationToken cancellationToken);
}

public sealed class RealtimeNotifier(IHubContext<CommunicationHub> hub) : IRealtimeNotifier
{
    public Task UserAsync(Guid userId, string name, object payload, CancellationToken ct) => hub.Clients.Group($"user:{userId}").SendAsync(name, payload, ct);
    public Task UnassignedAsync(string name, object payload, CancellationToken ct) => hub.Clients.Group("unassigned").SendAsync(name, payload, ct);
    public Task ConversationAsync(Guid id, string name, object payload, CancellationToken ct) => hub.Clients.Group($"conversation:{id}").SendAsync(name, payload, ct);
}
