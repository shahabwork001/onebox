using System.Text;
using CentralChat.Application;
using CentralChat.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CentralChat.API.Controllers;

[ApiController, Route("webhook"), AllowAnonymous, EnableRateLimiting("webhook")]
public sealed class WebhookController(IWebhookIngestionService ingestion, IOptions<MetaWhatsAppOptions> options) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.challenge")] string? challenge, [FromQuery(Name = "hub.verify_token")] string? token)
        => mode == "subscribe" && !string.IsNullOrEmpty(challenge) && CryptographicEquals(token, options.Value.VerifyToken) ? Content(challenge, "text/plain") : Forbid();

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8); var body = await reader.ReadToEndAsync(ct);
        if (!ingestion.ValidateSignature(body, Request.Headers["X-Hub-Signature-256"].FirstOrDefault())) return Unauthorized();
        var result = await ingestion.IngestAsync(body, ct); return Ok(new { accepted = true, eventId = result.EventId, duplicate = result.Duplicate });
    }
    private static bool CryptographicEquals(string? left, string? right) => left is not null && right is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
