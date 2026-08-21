using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class TicketService(CentralChatDbContext db, IRealtimeNotifier realtime) : ITicketService
{
    public async Task<PagedResult<TicketListItem>> ListAsync(string scope, string? status, Guid userId, int page, int pageSize, CancellationToken ct)
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
        await realtime.UserAsync(userId, "ticket.claimed", new { ticketId, assignedAgentId = userId }, ct);
        await realtime.UnassignedAsync("ticket.removed", new { ticketId }, ct);
    }

    public Task AssignAsync(Guid ticketId, Guid agentId, Guid changedBy, string? reason, CancellationToken ct) => ChangeAssignmentAsync(ticketId, agentId, changedBy, reason, ct);
    public Task UnassignAsync(Guid ticketId, Guid changedBy, string? reason, CancellationToken ct) => ChangeAssignmentAsync(ticketId, null, changedBy, reason, ct);
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
        if (previous.HasValue) await realtime.UserAsync(previous.Value, "ticket.assignment.removed", new { ticketId }, ct);
        if (agentId.HasValue) await realtime.UserAsync(agentId.Value, "ticket.assignment.added", new { ticketId }, ct); else await realtime.UnassignedAsync("ticket.created", new { ticketId }, ct);
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
        if (ticket.AssignedAgentId.HasValue) await realtime.UserAsync(ticket.AssignedAgentId.Value, "ticket.status.changed", payload, ct);
        else await realtime.UnassignedAsync("ticket.status.changed", payload, ct);
    }
}

public sealed class ConversationService(CentralChatDbContext db) : IConversationService
{
    public async Task<ConversationDto> GetAsync(Guid id, Guid userId, bool privileged, CancellationToken ct)
    {
        var result = await (from c in db.Conversations.AsNoTracking() join contact in db.Contacts.AsNoTracking() on c.ContactId equals contact.Id join t in db.Tickets.AsNoTracking() on c.Id equals t.ConversationId into tickets from t in tickets.DefaultIfEmpty() where c.Id == id select new ConversationDto(c.Id, contact.Id, contact.DisplayName, contact.PhoneNumber, contact.CurrentAssignedAgentId, t == null ? null : t.Id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Conversation not found.");
        if (!privileged && result.AssignedAgentId != userId) throw new ForbiddenException("This conversation is assigned to another agent.");
        return result;
    }

    public async Task<IReadOnlyCollection<MessageDto>> MessagesAsync(Guid id, Guid? beforeId, int limit, Guid userId, bool privileged, CancellationToken ct)
    {
        await GetAsync(id, userId, privileged, ct); limit = Math.Clamp(limit, 1, 100);
        var query = db.ChatMessages.AsNoTracking().Where(x => x.ConversationId == id);
        if (beforeId.HasValue) { var cursor = await db.ChatMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == beforeId, ct) ?? throw new NotFoundException("Message cursor not found."); query = query.Where(x => x.ProviderTimestamp < cursor.ProviderTimestamp); }
        return await query.OrderByDescending(x => x.ProviderTimestamp).ThenByDescending(x => x.Id).Take(limit).Select(x => new MessageDto(x.Id, x.ConversationId, x.Direction, x.Type, x.TextBody, x.Status, x.ProviderTimestamp, x.ExternalMessageId)).ToListAsync(ct);
    }

    public async Task<MessageDto> SendAsync(Guid id, string text, Guid userId, bool privileged, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new ValidationException("Message text is required and must not exceed 4096 characters.");
        var conversation = await GetAsync(id, userId, privileged, ct);
        var channelId = await db.Conversations.Where(x => x.Id == id).Select(x => x.ChannelId).SingleAsync(ct);
        var message = new ChatMessage(id, conversation.ContactId, channelId, MessageDirection.Outbound, MessageType.Text, text.Trim(), null, DateTimeOffset.UtcNow); message.SetSender(userId);
        db.ChatMessages.Add(message);
        db.OutboxMessages.Add(new OutboxMessage { Type = "OutboundWhatsAppMessageRequested", Payload = JsonSerializer.Serialize(new { MessageId = message.Id }) });
        await db.SaveChangesAsync(ct);
        return new(message.Id, id, message.Direction, message.Type, message.TextBody, message.Status, message.ProviderTimestamp, null);
    }
}
