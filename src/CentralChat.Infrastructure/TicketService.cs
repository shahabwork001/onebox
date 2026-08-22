using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class TicketService(CentralChatDbContext db, IRealtimeNotifier realtime, ITicketBroadcaster broadcast) : ITicketService
{
    public async Task<PagedResult<TicketListItem>> ListAsync(string scope, string? status, string? search, Guid userId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = from t in db.Tickets.AsNoTracking()
                    join c in db.Contacts.AsNoTracking() on t.ContactId equals c.Id
                    let last = db.ChatMessages.Where(m => m.ConversationId == t.ConversationId).OrderByDescending(m => m.ProviderTimestamp).Select(m => m.TextBody).FirstOrDefault()
                    select new { t, c, last };
        query = scope.ToLowerInvariant() switch { "mine" => query.Where(x => x.t.AssignedAgentId == userId), "unassigned" => query.Where(x => x.t.AssignedAgentId == null), _ => query };
        query = (status ?? "active").ToLowerInvariant() switch
        {
            "active" => query.Where(x => x.t.Status != TicketStatus.Resolved && x.t.Status != TicketStatus.Closed),
            "new" => query.Where(x => x.t.Status == TicketStatus.New),
            "open" => query.Where(x => x.t.Status == TicketStatus.Open),
            "pending" => query.Where(x => x.t.Status == TicketStatus.Pending),
            "resolved" => query.Where(x => x.t.Status == TicketStatus.Resolved),
            "closed" => query.Where(x => x.t.Status == TicketStatus.Closed),
            "all" => query,
            _ => throw new ValidationException($"Unknown ticket status filter '{status}'.")
        };

        // Filtering in the browser only ever saw the page already loaded, so searching for a customer
        // from last week silently found nothing. Message bodies are included because that is usually
        // what an agent remembers about a conversation.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.c.DisplayName, term) ||
                EF.Functions.ILike(x.c.PhoneNumber, term) ||
                EF.Functions.ILike(x.t.TicketNumber, term) ||
                db.ChatMessages.Any(m => m.ConversationId == x.t.ConversationId && m.TextBody != null && EF.Functions.ILike(m.TextBody, term)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.t.LastActivityAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TicketListItem(x.t.Id, x.t.TicketNumber, x.t.Status, x.t.Priority, x.c.Id, x.c.DisplayName, x.c.PhoneNumber, x.t.ConversationId, x.t.AssignedAgentId, x.t.LastActivityAt, x.last)).ToListAsync(ct);
        return new(rows, page, pageSize, total);
    }

    public async Task ClaimAsync(Guid ticketId, Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Tickets.Where(x => x.Id == ticketId && x.AssignedAgentId == null && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AssignedAgentId, userId).SetProperty(x => x.Status, TicketStatus.Open).SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 0) throw new ConflictException("Ticket has already been claimed or is no longer active.");
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticketId, ct);
        await db.Contacts.Where(x => x.Id == ticket.ContactId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentAssignedAgentId, userId).SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
        db.AssignmentHistory.Add(new ContactAssignmentHistory(ticket.ContactId, ticket.Id, null, userId, userId, AssignmentAction.Claimed, null));
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = "ticket.claimed", EntityType = "Ticket", EntityId = ticketId.ToString(), NewValues = JsonSerializer.Serialize(new { AssignedAgentId = userId }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await broadcast.UpsertedAsync(ticketId, ct);
    }

    public Task AssignAsync(Guid ticketId, Guid agentId, Guid changedBy, string? reason, CancellationToken ct) => ChangeAssignmentAsync(ticketId, agentId, changedBy, reason, ct);
    public async Task UnassignAsync(Guid ticketId, Guid changedBy, bool privileged, string? reason, CancellationToken ct)
    {
        // Releasing a ticket back to the queue is the assigned agent's own call; taking one off another
        // agent is an administrative act and still needs tickets.assign.
        if (!privileged)
        {
            var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct) ?? throw new NotFoundException("Ticket not found.");
            if (ticket.AssignedAgentId != changedBy) throw new ForbiddenException("Only the assigned agent can release this ticket.");
        }
        await ChangeAssignmentAsync(ticketId, null, changedBy, reason, ct);
    }
    public Task ResolveAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken ct) => ChangeStatusAsync(ticketId, TicketStatus.Resolved, userId, privileged, reason, ct);
    public Task CloseAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken ct) => ChangeStatusAsync(ticketId, TicketStatus.Closed, userId, privileged, reason, ct);
    public Task ReopenAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken ct) => ChangeStatusAsync(ticketId, TicketStatus.Open, userId, privileged, reason, ct);

    private async Task ChangeAssignmentAsync(Guid ticketId, Guid? agentId, Guid changedBy, string? reason, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == ticketId, ct) ?? throw new NotFoundException("Ticket not found.");
        var contact = await db.Contacts.SingleAsync(x => x.Id == ticket.ContactId, ct);
        var previous = ticket.AssignedAgentId;
        ticket.Assign(agentId); contact.Assign(agentId);
        var action = agentId is null ? AssignmentAction.Unassigned : previous is null ? AssignmentAction.Assigned : AssignmentAction.Reassigned;
        db.AssignmentHistory.Add(new ContactAssignmentHistory(contact.Id, ticket.Id, previous, agentId, changedBy, action, reason));
        db.AuditLogs.Add(new AuditLog { UserId = changedBy, Action = $"ticket.{action.ToString().ToLowerInvariant()}", EntityType = "Ticket", EntityId = ticketId.ToString(), OldValues = JsonSerializer.Serialize(new { AssignedAgentId = previous }), NewValues = JsonSerializer.Serialize(new { AssignedAgentId = agentId }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await broadcast.UpsertedAsync(ticketId, ct);
    }

    private async Task ChangeStatusAsync(Guid ticketId, TicketStatus target, Guid userId, bool privileged, string? reason, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == ticketId, ct) ?? throw new NotFoundException("Ticket not found.");
        if (!privileged && ticket.AssignedAgentId != userId) throw new ForbiddenException("Only the assigned agent can change this ticket's status.");
        var previous = ticket.Status;
        if (target == TicketStatus.Resolved) { if (!ticket.CanResolve) throw new ConflictException($"A {previous} ticket cannot be resolved."); ticket.Resolve(); }
        else if (target == TicketStatus.Closed) { if (!ticket.CanClose) throw new ConflictException("The ticket is already closed."); ticket.Close(); }
        else { if (!ticket.CanReopen) throw new ConflictException($"A {previous} ticket is already active."); ticket.Reopen(); }

        // Closing the last active ticket releases the contact so the next inbound message reaches the
        // unassigned queue; reopening restores the ticket's agent as the contact owner.
        if (target == TicketStatus.Closed && !await db.Tickets.AnyAsync(x => x.ContactId == ticket.ContactId && x.Id != ticket.Id && x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed, ct))
        {
            var contact = await db.Contacts.SingleAsync(x => x.Id == ticket.ContactId, ct);
            if (contact.CurrentAssignedAgentId.HasValue) { var released = contact.CurrentAssignedAgentId; contact.Assign(null); db.AssignmentHistory.Add(new ContactAssignmentHistory(contact.Id, ticket.Id, released, null, userId, AssignmentAction.Unassigned, reason ?? "Ticket closed.")); }
        }
        else if (target == TicketStatus.Open && ticket.AssignedAgentId.HasValue)
        {
            var contact = await db.Contacts.SingleAsync(x => x.Id == ticket.ContactId, ct);
            if (contact.CurrentAssignedAgentId != ticket.AssignedAgentId) { var owner = contact.CurrentAssignedAgentId; contact.Assign(ticket.AssignedAgentId); db.AssignmentHistory.Add(new ContactAssignmentHistory(contact.Id, ticket.Id, owner, ticket.AssignedAgentId, userId, owner is null ? AssignmentAction.Assigned : AssignmentAction.Reassigned, reason ?? "Ticket reopened.")); }
        }

        var action = target switch { TicketStatus.Resolved => "ticket.resolved", TicketStatus.Closed => "ticket.closed", _ => "ticket.reopened" };
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = action, EntityType = "Ticket", EntityId = ticketId.ToString(), OldValues = JsonSerializer.Serialize(new { Status = previous.ToString() }), NewValues = JsonSerializer.Serialize(new { Status = ticket.Status.ToString(), Reason = reason }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var payload = new { ticketId, status = ticket.Status.ToString(), previousStatus = previous.ToString(), conversationId = ticket.ConversationId };
        await realtime.ConversationAsync(ticket.ConversationId, "ticket.status.changed", payload, ct);
        await broadcast.UpsertedAsync(ticketId, ct);
    }
}
