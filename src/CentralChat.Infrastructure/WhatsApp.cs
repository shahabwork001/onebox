using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CentralChat.Infrastructure;

public sealed class WebhookIngestionService(CentralChatDbContext db, IOptions<MetaWhatsAppOptions> options, IRealtimeNotifier realtime, ILogger<WebhookIngestionService> logger) : IWebhookIngestionService
{
    private readonly MetaWhatsAppOptions _options = options.Value;

    public bool ValidateSignature(string body, string? signature)
    {
        if (!_options.ValidateSignature) return true;
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(_options.AppSecret)) return false;
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.AppSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    public async Task<IngestWebhookResult> IngestAsync(string body, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var existing = await db.WebhookEvents.AsNoTracking().FirstOrDefaultAsync(x => x.PayloadHash == hash, ct);
        if (existing is not null) return new(existing.Id, true);
        var externalId = ExtractFirstMessageId(body);
        var item = new WebhookEvent("MetaWhatsApp", hash, body, externalId);
        db.WebhookEvents.Add(item);
        db.OutboxMessages.Add(new OutboxMessage { Type = "WhatsAppWebhookReceived", Payload = JsonSerializer.Serialize(new { WebhookEventId = item.Id }) });
        try { await db.SaveChangesAsync(ct); return new(item.Id, false); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); existing = await db.WebhookEvents.AsNoTracking().SingleAsync(x => x.PayloadHash == hash, ct); return new(existing.Id, true); }
    }

    public async Task ProcessAsync(Guid eventId, CancellationToken ct)
    {
        var webhook = await db.WebhookEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct) ?? throw new InvalidOperationException($"Webhook event {eventId} was not found.");
        if (webhook.ProcessingStatus == WebhookProcessingStatus.Processed) return;
        using var document = JsonDocument.Parse(webhook.Payload);
        if (!document.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) { webhook.MarkProcessed(); await db.SaveChangesAsync(ct); return; }

        var notifications = new List<(Guid? AgentId, Guid ConversationId, Guid TicketId, MessageDto Message)>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                var phoneNumberId = value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrWhiteSpace(phoneNumberId)) continue;
                var channel = await db.WhatsAppChannels.SingleOrDefaultAsync(x => x.PhoneNumberId == phoneNumberId, ct);
                if (channel is null) { channel = new WhatsAppChannel($"WhatsApp {phoneNumberId}", phoneNumberId); db.WhatsAppChannels.Add(channel); }
                var profiles = ExtractProfiles(value);

                if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var incoming in messages.EnumerateArray())
                    {
                        var externalId = incoming.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                        if (string.IsNullOrWhiteSpace(externalId) || await db.ChatMessages.AnyAsync(x => x.ExternalMessageId == externalId, ct)) continue;
                        var waId = incoming.TryGetProperty("from", out var from) ? from.GetString() : null;
                        if (string.IsNullOrWhiteSpace(waId)) continue;
                        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.ChannelId == channel.Id && x.WhatsAppUserId == waId, ct);
                        if (contact is null) { profiles.TryGetValue(waId, out var profile); contact = new Contact(channel.Id, NormalizePhone(waId), waId, profile); db.Contacts.Add(contact); }
                        var conversation = await db.Conversations.SingleOrDefaultAsync(x => x.ContactId == contact.Id && x.ChannelId == channel.Id && x.Status == ConversationStatus.Open, ct);
                        if (conversation is null) { conversation = new Conversation(contact.Id, channel.Id); db.Conversations.Add(conversation); }
                        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.ContactId == contact.Id && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved, ct);
                        if (ticket is null) { ticket = new Ticket(contact.Id, conversation.Id, $"WA-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20]); if (contact.CurrentAssignedAgentId.HasValue) ticket.Assign(contact.CurrentAssignedAgentId); db.Tickets.Add(ticket); }
                        var timestamp = ParseTimestamp(incoming);
                        var (type, body) = ExtractContent(incoming);
                        var message = new ChatMessage(conversation.Id, contact.Id, channel.Id, MessageDirection.Inbound, type, body, externalId, timestamp);
                        db.ChatMessages.Add(message); contact.Touch(timestamp); conversation.Touch(timestamp); ticket.Touch(timestamp);
                        notifications.Add((contact.CurrentAssignedAgentId, conversation.Id, ticket.Id, new MessageDto(message.Id, conversation.Id, message.Direction, message.Type, message.TextBody, message.Status, timestamp, externalId)));
                    }
                }

                if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray()) await ApplyStatusAsync(status, ct);
                }
            }
        }
        webhook.MarkProcessed(); await db.SaveChangesAsync(ct);
        foreach (var n in notifications)
        {
            await realtime.ConversationAsync(n.ConversationId, "message.received", n.Message, ct);
            if (n.AgentId.HasValue) await realtime.UserAsync(n.AgentId.Value, "message.received", new { n.TicketId, Message = n.Message }, ct);
            else await realtime.UnassignedAsync("ticket.created", new { n.TicketId, Message = n.Message }, ct);
        }
        logger.LogInformation("Processed webhook {WebhookEventId} with {MessageCount} new messages", eventId, notifications.Count);
    }

    private async Task ApplyStatusAsync(JsonElement status, CancellationToken ct)
    {
        var externalId = status.TryGetProperty("id", out var id) ? id.GetString() : null;
        var name = status.TryGetProperty("status", out var state) ? state.GetString() : null;
        if (externalId is null || name is null) return;
        var message = await db.ChatMessages.SingleOrDefaultAsync(x => x.ExternalMessageId == externalId, ct); if (message is null) return;
        var mapped = name switch { "sent" => MessageStatus.Sent, "delivered" => MessageStatus.Delivered, "read" => MessageStatus.Read, "failed" => MessageStatus.Failed, _ => message.Status };
        message.ApplyStatus(mapped, ParseTimestamp(status));
    }

    private static Dictionary<string, string?> ExtractProfiles(JsonElement value)
    {
        var result = new Dictionary<string, string?>();
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array) return result;
        foreach (var c in contacts.EnumerateArray()) { var id = c.TryGetProperty("wa_id", out var wa) ? wa.GetString() : null; var name = c.TryGetProperty("profile", out var p) && p.TryGetProperty("name", out var n) ? n.GetString() : null; if (id is not null) result[id] = name; }
        return result;
    }
    private static (MessageType, string?) ExtractContent(JsonElement m)
    {
        var typeName = m.TryGetProperty("type", out var t) ? t.GetString() : "unknown";
        var type = Enum.TryParse<MessageType>(typeName, true, out var parsed) ? parsed : MessageType.Unknown;
        string? body = typeName == "text" && m.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var b) ? b.GetString() : null;
        return (type, body);
    }
    private static DateTimeOffset ParseTimestamp(JsonElement item) => item.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;
    private static string NormalizePhone(string value) => "+" + new string(value.Where(char.IsDigit).ToArray());
    private static string? ExtractFirstMessageId(string body) { try { using var doc = JsonDocument.Parse(body); return doc.RootElement.GetProperty("entry")[0].GetProperty("changes")[0].GetProperty("value").GetProperty("messages")[0].GetProperty("id").GetString(); } catch { return null; } }
}

public record WhatsAppSendResult(bool Success, string? ExternalMessageId, string? Error);
public interface IWhatsAppClient { Task<WhatsAppSendResult> SendTextAsync(string phoneNumberId, string recipient, string text, CancellationToken cancellationToken); }

public sealed class MetaWhatsAppClient(HttpClient http, IOptions<MetaWhatsAppOptions> options) : IWhatsAppClient
{
    private readonly MetaWhatsAppOptions _options = options.Value;
    public async Task<WhatsAppSendResult> SendTextAsync(string phoneNumberId, string recipient, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiVersion}/{phoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = recipient.TrimStart('+'), type = "text", text = new { body = text } });
        using var response = await http.SendAsync(request, ct); var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new(false, null, $"Meta returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        using var doc = JsonDocument.Parse(body); var id = doc.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 ? messages[0].GetProperty("id").GetString() : null;
        return new(id is not null, id, id is null ? "Meta response did not contain a message id." : null);
    }
}

public sealed class DevelopmentWhatsAppClient : IWhatsAppClient
{
    public Task<WhatsAppSendResult> SendTextAsync(string phoneNumberId, string recipient, string text, CancellationToken ct) => Task.FromResult(new WhatsAppSendResult(true, $"dev-{Guid.NewGuid():N}", null));
}
