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

public sealed class WebhookIngestionService(CentralChatDbContext db, IOptions<MetaWhatsAppOptions> options, IRealtimeNotifier realtime, ITicketBroadcaster broadcast, ILogger<WebhookIngestionService> logger) : IWebhookIngestionService
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

    // Webhook events are consumed concurrently, and any of them may be the first to touch a channel,
    // contact, conversation or ticket. Losing that insert race surfaces as a unique-index violation, and
    // without a retry the event is dead-lettered and the customer's messages are silently dropped.
    // Re-running the traversal finds the rows the winner wrote, so a bounded retry converges.
    private const int ProcessAttempts = 4;

    public async Task ProcessAsync(Guid eventId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var notifications = await ProcessOnceAsync(eventId, ct);
                foreach (var n in notifications)
                {
                    // The open transcript needs the message; every queue view needs the updated row.
                    await realtime.ConversationAsync(n.ConversationId, "message.received", n.Message, ct);
                    await broadcast.UpsertedAsync(n.TicketId, ct);
                }
                logger.LogInformation("Processed webhook {WebhookEventId} with {MessageCount} new messages", eventId, notifications.Count);
                return;
            }
            catch (DbUpdateException ex) when (attempt < ProcessAttempts)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Webhook {WebhookEventId} lost an insert race on attempt {Attempt}; retrying", eventId, attempt);
            }
        }
    }

    private async Task<List<(Guid? AgentId, Guid ConversationId, Guid TicketId, MessageDto Message)>> ProcessOnceAsync(Guid eventId, CancellationToken ct)
    {
        var notifications = new List<(Guid? AgentId, Guid ConversationId, Guid TicketId, MessageDto Message)>();
        var mediaDownloads = new List<Guid>();
        var resolved = new ResolutionCache();
        var webhook = await db.WebhookEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct) ?? throw new InvalidOperationException($"Webhook event {eventId} was not found.");
        if (webhook.ProcessingStatus == WebhookProcessingStatus.Processed) return notifications;
        using var document = JsonDocument.Parse(webhook.Payload);
        if (!document.RootElement.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) { webhook.MarkProcessed(); await db.SaveChangesAsync(ct); return notifications; }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                var phoneNumberId = value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrWhiteSpace(phoneNumberId)) continue;
                var channel = await ResolveChannelAsync(resolved, phoneNumberId, ct);
                var profiles = ExtractProfiles(value);

                if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var incoming in messages.EnumerateArray())
                    {
                        var externalId = incoming.TryGetProperty("id", out var mid) ? mid.GetString() : null;
                        if (string.IsNullOrWhiteSpace(externalId) || !resolved.MessageIds.Add(externalId) || await db.ChatMessages.AnyAsync(x => x.ExternalMessageId == externalId, ct)) continue;
                        var waId = incoming.TryGetProperty("from", out var from) ? from.GetString() : null;
                        if (string.IsNullOrWhiteSpace(waId)) continue;
                        profiles.TryGetValue(waId, out var profile);
                        var contact = await ResolveContactAsync(resolved, channel, waId, profile, ct);
                        var conversation = await ResolveConversationAsync(resolved, contact, channel, ct);
                        var ticket = await ResolveTicketAsync(resolved, contact, conversation, ct);
                        var timestamp = ParseTimestamp(incoming);
                        var content = ExtractContent(incoming);
                        // Meta expects opt-out keywords to be honoured automatically, and a number that
                        // keeps marketing to someone who said stop is a number that gets blocked.
                        if (IsOptOutKeyword(content.Text)) contact.SetMarketingOptOut(true);
                        var message = new ChatMessage(conversation.Id, contact.Id, channel.Id, MessageDirection.Inbound, content.Type, content.Text, externalId, timestamp);
                        // The binary is fetched by a queued job so the message is readable at once and a
                        // failing download retries on its own instead of failing the whole webhook.
                        if (!string.IsNullOrWhiteSpace(content.MediaId)) { message.SetProviderMedia(content.MediaId, content.MimeType); mediaDownloads.Add(message.Id); }
                        db.ChatMessages.Add(message); contact.Touch(timestamp); conversation.Touch(timestamp); ticket.Touch(timestamp);
                        notifications.Add((contact.CurrentAssignedAgentId, conversation.Id, ticket.Id, new MessageDto(message.Id, conversation.Id, message.Direction, message.Type, message.TextBody, message.Status, timestamp, externalId, message.MimeType, message.HasStoredMedia, message.MediaSizeBytes)));
                    }
                }

                if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray()) await ApplyStatusAsync(status, ct);
                }
            }
        }
        foreach (var messageId in mediaDownloads)
            db.OutboxMessages.Add(new OutboxMessage { Type = "WhatsAppMediaDownloadRequested", Payload = JsonSerializer.Serialize(new { MessageId = messageId }) });

        webhook.MarkProcessed(); await db.SaveChangesAsync(ct);
        return notifications;
    }

    /// <summary>
    /// One Meta payload routinely carries several messages from the same sender, and everything they
    /// need — channel, contact, conversation, ticket — is only created once, at the end, by a single
    /// SaveChanges. Querying the DbSet mid-loop cannot see those pending inserts, so without this cache
    /// the second message would add a duplicate contact and the whole event would fail on its unique
    /// index. Resolving through the cache keeps one instance per payload.
    /// </summary>
    private sealed class ResolutionCache
    {
        public Dictionary<string, WhatsAppChannel> Channels { get; } = new(StringComparer.Ordinal);
        public Dictionary<(Guid ChannelId, string WaId), Contact> Contacts { get; } = [];
        public Dictionary<Guid, Conversation> Conversations { get; } = [];
        public Dictionary<Guid, Ticket> Tickets { get; } = [];
        public HashSet<string> MessageIds { get; } = new(StringComparer.Ordinal);
    }

    private async Task<WhatsAppChannel> ResolveChannelAsync(ResolutionCache cache, string phoneNumberId, CancellationToken ct)
    {
        if (cache.Channels.TryGetValue(phoneNumberId, out var cached)) return cached;
        var channel = await db.WhatsAppChannels.SingleOrDefaultAsync(x => x.PhoneNumberId == phoneNumberId, ct);
        if (channel is null) { channel = new WhatsAppChannel($"WhatsApp {phoneNumberId}", phoneNumberId); db.WhatsAppChannels.Add(channel); }
        cache.Channels[phoneNumberId] = channel;
        return channel;
    }

    private async Task<Contact> ResolveContactAsync(ResolutionCache cache, WhatsAppChannel channel, string waId, string? profile, CancellationToken ct)
    {
        if (cache.Contacts.TryGetValue((channel.Id, waId), out var cached)) return cached;
        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.ChannelId == channel.Id && x.WhatsAppUserId == waId, ct);
        if (contact is null) { contact = new Contact(channel.Id, NormalizePhone(waId), waId, profile); db.Contacts.Add(contact); }
        cache.Contacts[(channel.Id, waId)] = contact;
        return contact;
    }

    private async Task<Conversation> ResolveConversationAsync(ResolutionCache cache, Contact contact, WhatsAppChannel channel, CancellationToken ct)
    {
        if (cache.Conversations.TryGetValue(contact.Id, out var cached)) return cached;
        var conversation = await db.Conversations.SingleOrDefaultAsync(x => x.ContactId == contact.Id && x.ChannelId == channel.Id && x.Status == ConversationStatus.Open, ct);
        if (conversation is null) { conversation = new Conversation(contact.Id, channel.Id); db.Conversations.Add(conversation); }
        cache.Conversations[contact.Id] = conversation;
        return conversation;
    }

    private async Task<Ticket> ResolveTicketAsync(ResolutionCache cache, Contact contact, Conversation conversation, CancellationToken ct)
    {
        if (cache.Tickets.TryGetValue(contact.Id, out var cached)) return cached;
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.ContactId == contact.Id && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved, ct);
        if (ticket is null)
        {
            ticket = new Ticket(contact.Id, conversation.Id, $"WA-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20]);
            if (contact.CurrentAssignedAgentId.HasValue) ticket.Assign(contact.CurrentAssignedAgentId);
            db.Tickets.Add(ticket);
        }
        cache.Tickets[contact.Id] = ticket;
        return ticket;
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
    private static readonly HashSet<string> OptOutKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "stop", "unsubscribe", "cancel", "end", "quit", "optout", "opt out" };

    private static bool IsOptOutKeyword(string? text) =>
        !string.IsNullOrWhiteSpace(text) && OptOutKeywords.Contains(text.Trim().Trim('.', '!').ToLowerInvariant());

    private sealed record IncomingContent(MessageType Type, string? Text, string? MediaId, string? MimeType);

    /// <summary>
    /// Media messages name their payload after their own type — {"image": {"id", "mime_type", "caption"}} —
    /// and carry only a provider id, never the bytes. The caption becomes the message text so a photo
    /// with a note reads correctly in the transcript.
    /// </summary>
    private static IncomingContent ExtractContent(JsonElement m)
    {
        var typeName = m.TryGetProperty("type", out var t) ? t.GetString() ?? "unknown" : "unknown";
        var messageType = Enum.TryParse<MessageType>(typeName, true, out var parsedType) ? parsedType : MessageType.Unknown;

        if (typeName == "text")
            return new(messageType, m.TryGetProperty("text", out var textNode) && textNode.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() : null, null, null);

        if (m.TryGetProperty(typeName, out var payload) && payload.ValueKind == JsonValueKind.Object)
            return new(
                messageType,
                payload.TryGetProperty("caption", out var caption) ? caption.GetString() : null,
                payload.TryGetProperty("id", out var mediaId) ? mediaId.GetString() : null,
                payload.TryGetProperty("mime_type", out var mime) ? mime.GetString() : null);

        return new(messageType, null, null, null);
    }

    private static DateTimeOffset ParseTimestamp(JsonElement item) => item.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), CultureInfo.InvariantCulture, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UtcNow;
    private static string NormalizePhone(string value) => "+" + new string(value.Where(char.IsDigit).ToArray());
    private static string? ExtractFirstMessageId(string body) { try { using var doc = JsonDocument.Parse(body); return doc.RootElement.GetProperty("entry")[0].GetProperty("changes")[0].GetProperty("value").GetProperty("messages")[0].GetProperty("id").GetString(); } catch { return null; } }
}

public record WhatsAppSendResult(bool Success, string? ExternalMessageId, string? Error);
public record WhatsAppMedia(byte[] Content, string? MimeType);

/// <summary>
/// An approved template as Meta describes it. Marketing may only be sent this way, and a template that
/// is not APPROVED cannot be used, so status travels with the name rather than being assumed.
/// </summary>
public record WhatsAppTemplate(string Name, string Language, string Category, string Status, string? Body, int VariableCount);

public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendTextAsync(string phoneNumberId, string recipient, string text, CancellationToken cancellationToken);

    /// <summary>Meta hands out a short-lived URL for a media id; the binary needs the access token too.</summary>
    Task<WhatsAppMedia?> DownloadMediaAsync(string mediaId, CancellationToken cancellationToken);

    /// <summary>Uploads the binary, then sends a message referring to it by the id Meta returns.</summary>
    Task<WhatsAppSendResult> SendMediaAsync(string phoneNumberId, string recipient, Stream content, string mimeType, string fileName, MessageType kind, string? caption, CancellationToken cancellationToken);

    /// <summary>Templates live on the business account, not the phone number, and are approved by Meta.</summary>
    Task<IReadOnlyCollection<WhatsAppTemplate>> ListTemplatesAsync(CancellationToken cancellationToken);

    Task<WhatsAppSendResult> SendTemplateAsync(string phoneNumberId, string recipient, string templateName, string language, IReadOnlyList<string> variables, CancellationToken cancellationToken);
}

public sealed partial class MetaWhatsAppClient(HttpClient http, IOptions<MetaWhatsAppOptions> options) : IWhatsAppClient
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

    public async Task<WhatsAppSendResult> SendMediaAsync(string phoneNumberId, string recipient, Stream content, string mimeType, string fileName, MessageType kind, string? caption, CancellationToken ct)
    {
        // Meta will not accept a binary inline: it must be uploaded first and referenced by id.
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiVersion}/{phoneNumberId}/media");
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        form.Add(new StringContent("whatsapp"), "messaging_product");
        form.Add(new StringContent(mimeType), "type");
        form.Add(file, "file", fileName);
        upload.Content = form;

        using var uploadResponse = await http.SendAsync(upload, ct);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
        if (!uploadResponse.IsSuccessStatusCode) return new(false, null, $"Meta rejected the upload ({(int)uploadResponse.StatusCode}): {uploadBody[..Math.Min(uploadBody.Length, 400)]}");

        using var uploaded = JsonDocument.Parse(uploadBody);
        var mediaId = uploaded.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(mediaId)) return new(false, null, "Meta upload response did not contain a media id.");

        var typeName = MediaTypeName(kind);
        object descriptor = kind == MessageType.Document
            ? new { id = mediaId, caption, filename = fileName }
            : kind == MessageType.Audio
                ? new { id = mediaId }                       // Meta rejects a caption on audio
                : new { id = mediaId, caption };

        using var send = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiVersion}/{phoneNumberId}/messages");
        send.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        send.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = recipient.TrimStart('+'),
            ["type"] = typeName,
            [typeName] = descriptor,
        });

        using var sendResponse = await http.SendAsync(send, ct);
        var sendBody = await sendResponse.Content.ReadAsStringAsync(ct);
        if (!sendResponse.IsSuccessStatusCode) return new(false, null, $"Meta returned {(int)sendResponse.StatusCode}: {sendBody[..Math.Min(sendBody.Length, 400)]}");

        using var sent = JsonDocument.Parse(sendBody);
        var messageId = sent.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 ? messages[0].GetProperty("id").GetString() : null;
        return new(messageId is not null, messageId, messageId is null ? "Meta response did not contain a message id." : null);
    }

    public async Task<IReadOnlyCollection<WhatsAppTemplate>> ListTemplatesAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.BusinessAccountId))
            throw new ValidationException("MetaWhatsApp:BusinessAccountId is not configured, so templates cannot be listed.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.ApiVersion}/{_options.BusinessAccountId}/message_templates?limit=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ValidationException($"Meta rejected the template request ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 300)]}");

        using var document = JsonDocument.Parse(body);
        var templates = new List<WhatsAppTemplate>();
        if (!document.RootElement.TryGetProperty("data", out var data)) return templates;

        foreach (var template in data.EnumerateArray())
        {
            var name = template.GetProperty("name").GetString() ?? "";
            var language = template.TryGetProperty("language", out var l) ? l.GetString() ?? "en" : "en";
            var category = template.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            var status = template.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

            // The body component carries the text and its placeholders, which the caller has to fill.
            string? bodyText = null;
            if (template.TryGetProperty("components", out var components))
            {
                foreach (var component in components.EnumerateArray())
                {
                    if (component.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "BODY", StringComparison.OrdinalIgnoreCase))
                        bodyText = component.TryGetProperty("text", out var text) ? text.GetString() : null;
                }
            }

            var variables = bodyText is null ? 0 : TemplateVariables().Matches(bodyText).Count;
            templates.Add(new WhatsAppTemplate(name, language, category, status, bodyText, variables));
        }
        return templates;
    }

    public async Task<WhatsAppSendResult> SendTemplateAsync(string phoneNumberId, string recipient, string templateName, string language, IReadOnlyList<string> variables, CancellationToken ct)
    {
        object template = variables.Count == 0
            ? new { name = templateName, language = new { code = language } }
            : new
            {
                name = templateName,
                language = new { code = language },
                components = new[]
                {
                    new { type = "body", parameters = variables.Select(v => new { type = "text", text = v }).ToArray() },
                },
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiVersion}/{phoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = recipient.TrimStart('+'), type = "template", template });

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return new(false, null, $"Meta returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 400)]}");

        using var document = JsonDocument.Parse(body);
        var id = document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 ? messages[0].GetProperty("id").GetString() : null;
        return new(id is not null, id, id is null ? "Meta response did not contain a message id." : null);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\{\{\s*\d+\s*\}\}")]
    private static partial System.Text.RegularExpressions.Regex TemplateVariables();

    private static string MediaTypeName(MessageType kind) => kind switch
    {
        MessageType.Image => "image",
        MessageType.Video => "video",
        MessageType.Audio => "audio",
        MessageType.Sticker => "sticker",
        _ => "document",
    };

    public async Task<WhatsAppMedia?> DownloadMediaAsync(string mediaId, CancellationToken ct)
    {
        // Two hops: the id resolves to a signed URL on a Meta CDN host, which still requires the token.
        using var lookup = new HttpRequestMessage(HttpMethod.Get, $"{_options.ApiVersion}/{mediaId}");
        lookup.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var lookupResponse = await http.SendAsync(lookup, ct);
        if (!lookupResponse.IsSuccessStatusCode) return null;

        using var descriptor = JsonDocument.Parse(await lookupResponse.Content.ReadAsStringAsync(ct));
        var url = descriptor.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
        var mime = descriptor.RootElement.TryGetProperty("mime_type", out var m) ? m.GetString() : null;
        if (string.IsNullOrWhiteSpace(url)) return null;

        using var download = new HttpRequestMessage(HttpMethod.Get, url);
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        using var downloadResponse = await http.SendAsync(download, ct);
        if (!downloadResponse.IsSuccessStatusCode) return null;

        return new WhatsAppMedia(await downloadResponse.Content.ReadAsByteArrayAsync(ct), mime ?? downloadResponse.Content.Headers.ContentType?.MediaType);
    }
}

public sealed class DevelopmentWhatsAppClient : IWhatsAppClient
{
    // A 1x1 PNG, so the media path can be exercised locally without calling Meta.
    private static readonly byte[] Placeholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public Task<WhatsAppSendResult> SendTextAsync(string phoneNumberId, string recipient, string text, CancellationToken ct) => Task.FromResult(new WhatsAppSendResult(true, $"dev-{Guid.NewGuid():N}", null));

    public Task<WhatsAppMedia?> DownloadMediaAsync(string mediaId, CancellationToken ct) => Task.FromResult<WhatsAppMedia?>(new(Placeholder, "image/png"));

    public Task<WhatsAppSendResult> SendMediaAsync(string phoneNumberId, string recipient, Stream content, string mimeType, string fileName, MessageType kind, string? caption, CancellationToken ct)
        => Task.FromResult(new WhatsAppSendResult(true, $"dev-{Guid.NewGuid():N}", null));

    // Enough shape to exercise campaigns locally, including one template that is not approved.
    public Task<IReadOnlyCollection<WhatsAppTemplate>> ListTemplatesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyCollection<WhatsAppTemplate>>(
        [
            new("order_update", "en", "UTILITY", "APPROVED", "Hello {{1}}, your order {{2}} has shipped.", 2),
            new("seasonal_offer", "en", "MARKETING", "APPROVED", "Hi {{1}}, enjoy 20% off this week.", 1),
            new("pending_review", "en", "MARKETING", "PENDING", "Draft awaiting approval.", 0),
        ]);

    public Task<WhatsAppSendResult> SendTemplateAsync(string phoneNumberId, string recipient, string templateName, string language, IReadOnlyList<string> variables, CancellationToken ct)
        => Task.FromResult(new WhatsAppSendResult(true, $"dev-{Guid.NewGuid():N}", null));
}
