namespace CentralChat.Domain;

public sealed class ContactAssignmentHistory : Entity
{
    private ContactAssignmentHistory() { }
    public ContactAssignmentHistory(Guid contactId, Guid? ticketId, Guid? previousAgentId, Guid? newAgentId, Guid changedByUserId, AssignmentAction action, string? reason)
        => (ContactId, TicketId, PreviousAgentId, NewAgentId, ChangedByUserId, Action, Reason) = (contactId, ticketId, previousAgentId, newAgentId, changedByUserId, action, reason);
    public Guid ContactId { get; private set; }
    public Guid? TicketId { get; private set; }
    public Guid? PreviousAgentId { get; private set; }
    public Guid? NewAgentId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public AssignmentAction Action { get; private set; }
    public string? Reason { get; private set; }
}

public sealed class WebhookEvent : Entity
{
    private WebhookEvent() { }
    public WebhookEvent(string provider, string hash, string payload, string? externalEventId)
        => (Provider, PayloadHash, Payload, ExternalEventId, ReceivedAt) = (provider, hash, payload, externalEventId, DateTimeOffset.UtcNow);
    public string Provider { get; private set; } = null!;
    public string? ExternalEventId { get; private set; }
    public string PayloadHash { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public WebhookProcessingStatus ProcessingStatus { get; private set; } = WebhookProcessingStatus.Pending;
    public string? Error { get; private set; }
    public void MarkPublished() => ProcessingStatus = WebhookProcessingStatus.Published;
    public void MarkProcessed() { ProcessingStatus = WebhookProcessingStatus.Processed; ProcessedAt = DateTimeOffset.UtcNow; }
    public void MarkFailed(string error) { ProcessingStatus = WebhookProcessingStatus.Failed; Error = error; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = null!;
    public string Payload { get; init; } = null!;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}

public sealed class InboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Consumer { get; init; } = null!;
    public string MessageId { get; init; } = null!;
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? UserId { get; init; }
    public string Action { get; init; } = null!;
    public string EntityType { get; init; } = null!;
    public string EntityId { get; init; } = null!;
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; init; }
}

public sealed class Team : Entity { public string Name { get; set; } = null!; }
public sealed class TeamMember { public Guid TeamId { get; set; } public Guid UserId { get; set; } }
