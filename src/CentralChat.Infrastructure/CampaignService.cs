using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CentralChat.Infrastructure;

public sealed class CampaignService(
    CentralChatDbContext db,
    IWhatsAppClient whatsApp,
    ILogger<CampaignService> logger) : ICampaignService
{
    /// <summary>
    /// Only approved marketing and utility templates can actually be sent. Anything still pending or
    /// rejected is returned too, so the reason a template is unavailable is visible rather than absent.
    /// </summary>
    public async Task<IReadOnlyCollection<TemplateDto>> TemplatesAsync(CancellationToken ct)
    {
        var templates = await whatsApp.ListTemplatesAsync(ct);
        return templates
            .Select(x => new TemplateDto(x.Name, x.Language, x.Category, x.Status, x.Body, x.VariableCount,
                Usable: string.Equals(x.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Usable)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<CampaignAudienceDto> AudienceAsync(CancellationToken ct)
    {
        var total = await db.Contacts.CountAsync(ct);
        var optedOut = await db.Contacts.CountAsync(x => x.MarketingOptOut, ct);
        var blocked = await db.Contacts.CountAsync(x => !x.MarketingOptOut && x.Status != ContactStatus.Active, ct);
        return new CampaignAudienceDto(total, optedOut, blocked, total - optedOut - blocked);
    }

    public async Task<CampaignDto> CreateAsync(CreateCampaignRequest request, Guid createdBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ValidationException("A campaign name is required.");

        var template = (await whatsApp.ListTemplatesAsync(ct))
            .FirstOrDefault(x => x.Name == request.TemplateName && x.Language == request.TemplateLanguage)
            ?? throw new ValidationException($"Template '{request.TemplateName}' was not found on the business account.");

        if (!string.Equals(template.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Template '{template.Name}' is {template.Status.ToLowerInvariant()} and cannot be sent until Meta approves it.");

        // A template rejects the whole send if its placeholders are unfilled, so this is caught up front.
        var variables = request.Variables ?? [];
        if (variables.Count != template.VariableCount)
            throw new ValidationException($"Template '{template.Name}' needs {template.VariableCount} value(s); {variables.Count} were supplied.");

        var campaign = new Campaign(request.Name.Trim(), template.Name, template.Language, createdBy);
        db.Campaigns.Add(campaign);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = createdBy,
            Action = "campaign.created",
            EntityType = "Campaign",
            EntityId = campaign.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { Campaign = campaign.Name, Template = template.Name, template.Category }),
        });
        await db.SaveChangesAsync(ct);
        return await DescribeAsync(campaign.Id, ct);
    }

    public async Task<CampaignDto> StartAsync(Guid campaignId, IReadOnlyList<string> variables, Guid startedBy, CancellationToken ct)
    {
        var campaign = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == campaignId, ct)
            ?? throw new NotFoundException("Campaign not found.");

        // The audience is fixed here rather than resolved as the send runs, so a broadcast cannot grow
        // underneath itself, and every recipient is recorded before the first message leaves.
        var recipients = await db.Contacts.AsNoTracking()
            .Where(x => !x.MarketingOptOut && x.Status == ContactStatus.Active)
            .Select(x => new { x.Id, x.PhoneNumber })
            .ToListAsync(ct);

        if (recipients.Count == 0) throw new ValidationException("No contacts are eligible: every contact has opted out or is inactive.");

        campaign.Start(recipients.Count);
        foreach (var recipient in recipients)
        {
            var row = new CampaignRecipient(campaign.Id, recipient.Id, recipient.PhoneNumber);
            db.CampaignRecipients.Add(row);
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "CampaignMessageRequested",
                Payload = JsonSerializer.Serialize(new { RecipientId = row.Id, Variables = variables }),
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = startedBy,
            Action = "campaign.started",
            EntityType = "Campaign",
            EntityId = campaign.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new { campaign.Name, campaign.TemplateName, Recipients = recipients.Count }),
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Campaign {CampaignId} started for {Recipients} recipients", campaign.Id, recipients.Count);
        return await DescribeAsync(campaign.Id, ct);
    }

    public async Task<CampaignDto> SetPausedAsync(Guid campaignId, bool paused, Guid actingUserId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.SingleOrDefaultAsync(x => x.Id == campaignId, ct)
            ?? throw new NotFoundException("Campaign not found.");

        if (paused) campaign.Pause(); else campaign.Resume();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = paused ? "campaign.paused" : "campaign.resumed",
            EntityType = "Campaign",
            EntityId = campaign.Id.ToString(),
        });
        await db.SaveChangesAsync(ct);
        return await DescribeAsync(campaignId, ct);
    }

    public async Task<IReadOnlyCollection<CampaignDto>> ListAsync(CancellationToken ct)
    {
        // Counts for every listed campaign in one grouped query rather than two per row.
        var campaigns = await db.Campaigns.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        if (campaigns.Count == 0) return [];

        var ids = campaigns.Select(x => x.Id).ToList();
        var counts = await db.CampaignRecipients.AsNoTracking()
            .Where(x => ids.Contains(x.CampaignId))
            .GroupBy(x => new { x.CampaignId, x.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return campaigns
            .Select(campaign => Describe(campaign, counts
                .Where(c => c.CampaignId == campaign.Id)
                .ToDictionary(c => c.Status, c => c.Count)))
            .ToList();
    }

    public Task<CampaignDto> GetAsync(Guid campaignId, CancellationToken ct) => DescribeAsync(campaignId, ct);

    private async Task<CampaignDto> DescribeAsync(Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == campaignId, ct)
            ?? throw new NotFoundException("Campaign not found.");

        var counts = await db.CampaignRecipients.AsNoTracking()
            .Where(x => x.CampaignId == campaignId)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Describe(campaign, counts.ToDictionary(x => x.Status, x => x.Count));
    }

    private static CampaignDto Describe(Campaign campaign, Dictionary<CampaignRecipientStatus, int> counts)
    {
        int countOf(CampaignRecipientStatus status) => counts.GetValueOrDefault(status);

        // Delivered and read are also sent; read is also delivered. Reporting them cumulatively keeps
        // the funnel honest as later receipts move recipients along.
        return new CampaignDto(
            campaign.Id, campaign.Name, campaign.TemplateName, campaign.TemplateLanguage,
            campaign.Status, campaign.TotalRecipients,
            Pending: countOf(CampaignRecipientStatus.Pending),
            Sent: countOf(CampaignRecipientStatus.Sent) + countOf(CampaignRecipientStatus.Delivered) + countOf(CampaignRecipientStatus.Read),
            Delivered: countOf(CampaignRecipientStatus.Delivered) + countOf(CampaignRecipientStatus.Read),
            Read: countOf(CampaignRecipientStatus.Read),
            Failed: countOf(CampaignRecipientStatus.Failed),
            Skipped: countOf(CampaignRecipientStatus.Skipped),
            campaign.StartedAt, campaign.CompletedAt, campaign.FailureReason);
    }
}
