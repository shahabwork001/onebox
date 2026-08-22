using CentralChat.Application;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

/// <summary>
/// Ticket events used to carry an identifier, which left every client with no choice but to reload
/// every list on every event. With a hundred agents connected, one arriving message cost a hundred
/// reloads of data that had barely changed. Publishing the projected row instead lets a client update
/// the conversation in place, and reserves refetching for reconciliation.
/// </summary>
public sealed class TicketBroadcaster(CentralChatDbContext db, IRealtimeNotifier realtime) : ITicketBroadcaster
{
    public async Task UpsertedAsync(Guid ticketId, CancellationToken ct)
    {
        var row = await ProjectAsync(ticketId, ct);
        if (row is null) return;
        await realtime.WorkspaceAsync("ticket.upserted", row, ct);
    }

    public Task RemovedAsync(Guid ticketId, CancellationToken ct) =>
        realtime.WorkspaceAsync("ticket.removed", new { TicketId = ticketId }, ct);

    /// <summary>Same shape the list endpoint returns, so a client can drop it straight into its list.</summary>
    private Task<TicketListItem?> ProjectAsync(Guid ticketId, CancellationToken ct) =>
        (from t in db.Tickets.AsNoTracking()
         join c in db.Contacts.AsNoTracking() on t.ContactId equals c.Id
         where t.Id == ticketId
         let last = db.ChatMessages
             .Where(m => m.ConversationId == t.ConversationId)
             .OrderByDescending(m => m.ProviderTimestamp)
             .Select(m => m.TextBody)
             .FirstOrDefault()
         select new TicketListItem(
             t.Id, t.TicketNumber, t.Status, t.Priority,
             c.Id, c.DisplayName, c.PhoneNumber, t.ConversationId,
             t.AssignedAgentId, t.LastActivityAt, last))
        .SingleOrDefaultAsync(ct)!;
}
