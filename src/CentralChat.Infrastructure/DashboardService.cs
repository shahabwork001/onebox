using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

/// <summary>
/// One row per agent who answered first on at least one conversation. Aggregated in SQL rather than
/// in memory: a dashboard must not load every conversation just to average a duration.
/// </summary>
public sealed class FirstResponseRow
{
    public Guid? AgentId { get; set; }
    public double? Seconds { get; set; }
    public int Conversations { get; set; }
}

public sealed class DashboardService(CentralChatDbContext db) : IDashboardService
{
    /// <summary>
    /// First response is measured per conversation, from the customer's first inbound message to the
    /// first outbound reply, and attributed to whoever sent that reply. The inner NOT EXISTS picks the
    /// earliest outbound message using the (ConversationId, ProviderTimestamp, Id) index.
    /// </summary>
    private const string FirstResponseSql = """
        SELECT o."SenderUserId"                                        AS "AgentId",
               AVG(EXTRACT(EPOCH FROM (o."ProviderTimestamp" - i.at))) AS "Seconds",
               COUNT(*)::int                                           AS "Conversations"
        FROM centralchat."ChatMessages" o
        JOIN (
            SELECT "ConversationId", MIN("ProviderTimestamp") AS at
            FROM centralchat."ChatMessages"
            WHERE "Direction" = 0
            GROUP BY "ConversationId"
        ) i ON i."ConversationId" = o."ConversationId"
        WHERE o."Direction" = 1
          AND o."ProviderTimestamp" > i.at
          AND NOT EXISTS (
              SELECT 1 FROM centralchat."ChatMessages" e
              WHERE e."ConversationId" = o."ConversationId"
                AND e."Direction" = 1
                AND e."ProviderTimestamp" < o."ProviderTimestamp")
        GROUP BY o."SenderUserId"
        """;

    public async Task<DashboardDto> GetAsync(Guid userId, bool privileged, CancellationToken ct)
    {
        var tickets = db.Tickets.AsNoTracking();
        var messages = db.ChatMessages.AsNoTracking();

        var totals = new DashboardTotals(
            Contacts: await db.Contacts.CountAsync(ct),
            Conversations: await db.Conversations.CountAsync(ct),
            Tickets: await tickets.CountAsync(ct),
            Unassigned: await tickets.CountAsync(x => x.AssignedAgentId == null && x.Status != TicketStatus.Resolved && x.Status != TicketStatus.Closed, ct),
            Open: await tickets.CountAsync(x => x.Status == TicketStatus.Open || x.Status == TicketStatus.New || x.Status == TicketStatus.Pending, ct),
            Resolved: await tickets.CountAsync(x => x.Status == TicketStatus.Resolved, ct),
            Closed: await tickets.CountAsync(x => x.Status == TicketStatus.Closed, ct),
            InboundMessages: await messages.CountAsync(x => x.Direction == MessageDirection.Inbound, ct),
            OutboundMessages: await messages.CountAsync(x => x.Direction == MessageDirection.Outbound, ct),
            AvgFirstResponseSeconds: null);

        var responses = await db.Set<FirstResponseRow>().FromSqlRaw(FirstResponseSql).ToListAsync(ct);

        // Weighted by conversation count so the workspace average is not skewed by a quiet agent.
        var answered = responses.Where(x => x.Seconds.HasValue).ToList();
        var conversationsAnswered = answered.Sum(x => x.Conversations);
        totals = totals with
        {
            AvgFirstResponseSeconds = conversationsAnswered == 0
                ? null
                : answered.Sum(x => x.Seconds!.Value * x.Conversations) / conversationsAnswered,
        };

        if (!privileged) return new DashboardDto(totals, []);

        var byAgent = await (
            from user in db.Users.AsNoTracking()
            where user.IsActive
            select new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                Claimed = tickets.Count(t => t.AssignedAgentId == user.Id),
                Open = tickets.Count(t => t.AssignedAgentId == user.Id && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
                Resolved = tickets.Count(t => t.AssignedAgentId == user.Id && t.Status == TicketStatus.Resolved),
            }).ToListAsync(ct);

        var agents = byAgent
            .Select(x => new AgentWorkload(
                x.Id,
                x.DisplayName,
                x.Email ?? string.Empty,
                x.Claimed,
                x.Open,
                x.Resolved,
                responses.FirstOrDefault(r => r.AgentId == x.Id)?.Seconds))
            .OrderByDescending(x => x.Open)
            .ThenBy(x => x.DisplayName)
            .ToList();

        return new DashboardDto(totals, agents);
    }
}
