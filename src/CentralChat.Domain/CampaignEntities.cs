namespace CentralChat.Domain;

public enum CampaignStatus { Draft, Sending, Paused, Completed, Failed }

/// <summary>Per recipient, because a broadcast half-succeeds far more often than it fails outright.</summary>
public enum CampaignRecipientStatus { Pending, Sent, Delivered, Read, Failed, Skipped }

/// <summary>
/// A marketing broadcast. Marketing may only be sent as an approved template, so a campaign records
/// which template it uses rather than any message text of its own, and the audience is resolved once
/// at launch so the recipient list cannot shift underneath a send that is already running.
/// </summary>
public sealed class Campaign : Entity
{
    private Campaign() { }

    public Campaign(string name, string templateName, string templateLanguage, string templateVariables, Guid createdBy)
        => (Name, TemplateName, TemplateLanguage, TemplateVariables, CreatedBy) =
           (name, templateName, templateLanguage, templateVariables, createdBy);

    public string Name { get; private set; } = null!;
    public string TemplateName { get; private set; } = null!;
    public string TemplateLanguage { get; private set; } = null!;

    /// <summary>
    /// The values filling the template's placeholders, as JSON. They belong to the campaign rather than
    /// to the act of starting it: a template sent with its placeholders unfilled is rejected outright,
    /// so what was reviewed at creation has to be exactly what is sent.
    /// </summary>
    public string TemplateVariables { get; private set; } = "[]";
    public CampaignStatus Status { get; private set; } = CampaignStatus.Draft;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int TotalRecipients { get; private set; }
    public string? FailureReason { get; private set; }

    public void Start(int recipients)
    {
        if (Status != CampaignStatus.Draft) throw new InvalidOperationException($"A {Status} campaign cannot be started.");
        Status = CampaignStatus.Sending;
        TotalRecipients = recipients;
        StartedAt = DateTimeOffset.UtcNow;
        UpdatedAt = StartedAt.Value;
    }

    /// <summary>Pausing is the safety valve: quality ratings fall fast and a broadcast must be stoppable.</summary>
    public void Pause()
    {
        if (Status != CampaignStatus.Sending) throw new InvalidOperationException($"A {Status} campaign cannot be paused.");
        Status = CampaignStatus.Paused;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Resume()
    {
        if (Status != CampaignStatus.Paused) throw new InvalidOperationException($"A {Status} campaign cannot be resumed.");
        Status = CampaignStatus.Sending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        Status = CampaignStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }

    public void Fail(string reason)
    {
        Status = CampaignStatus.Failed;
        FailureReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }
}

public sealed class CampaignRecipient : Entity
{
    private CampaignRecipient() { }

    public CampaignRecipient(Guid campaignId, Guid contactId, string phoneNumber)
        => (CampaignId, ContactId, PhoneNumber) = (campaignId, contactId, phoneNumber);

    public Guid CampaignId { get; private set; }
    public Guid ContactId { get; private set; }
    public string PhoneNumber { get; private set; } = null!;
    public CampaignRecipientStatus Status { get; private set; } = CampaignRecipientStatus.Pending;
    public string? ExternalMessageId { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public void MarkSent(string externalMessageId)
    {
        Status = CampaignRecipientStatus.Sent;
        ExternalMessageId = externalMessageId;
        SentAt = DateTimeOffset.UtcNow;
        UpdatedAt = SentAt.Value;
    }

    public void MarkFailed(string error)
    {
        Status = CampaignRecipientStatus.Failed;
        Error = error[..Math.Min(error.Length, 500)];
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Opted out between being queued and being reached; not an error, just not sent.</summary>
    public void Skip(string reason)
    {
        Status = CampaignRecipientStatus.Skipped;
        Error = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyDeliveryStatus(CampaignRecipientStatus status)
    {
        // Delivery reports can arrive out of order; never walk a recipient backwards.
        if (status > Status) { Status = status; UpdatedAt = DateTimeOffset.UtcNow; }
    }
}
