using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CentralChat.API.Controllers;

/// <summary>
/// Broadcasts are gated on settings.manage rather than a messaging permission: sending marketing to the
/// whole contact list is an administrative act with a bill and a deliverability risk attached, not a
/// conversation an agent has.
/// </summary>
[ApiController, Route("api/campaigns"), Authorize(Policy = Permissions.SettingsManage), EnableRateLimiting("api")]
public sealed class CampaignsController(ICampaignService campaigns, ICurrentUser current) : ControllerBase
{
    [HttpGet("templates")] public Task<IReadOnlyCollection<TemplateDto>> Templates(CancellationToken ct) => campaigns.TemplatesAsync(ct);
    [HttpGet("audience")] public Task<CampaignAudienceDto> Audience(CancellationToken ct) => campaigns.AudienceAsync(ct);
    [HttpGet] public Task<IReadOnlyCollection<CampaignDto>> List(CancellationToken ct) => campaigns.ListAsync(ct);
    [HttpGet("{id:guid}")] public Task<CampaignDto> Get(Guid id, CancellationToken ct) => campaigns.GetAsync(id, ct);

    [HttpPost] public Task<CampaignDto> Create(CreateCampaignRequest request, CancellationToken ct) => campaigns.CreateAsync(request, current.Id, ct);

    /// <summary>Variables were fixed when the campaign was created, so starting takes no arguments.</summary>
    [HttpPost("{id:guid}/start")]
    public Task<CampaignDto> Start(Guid id, CancellationToken ct) => campaigns.StartAsync(id, current.Id, ct);

    [HttpPost("{id:guid}/pause")] public Task<CampaignDto> Pause(Guid id, CancellationToken ct) => campaigns.SetPausedAsync(id, true, current.Id, ct);
    [HttpPost("{id:guid}/resume")] public Task<CampaignDto> Resume(Guid id, CancellationToken ct) => campaigns.SetPausedAsync(id, false, current.Id, ct);
}
