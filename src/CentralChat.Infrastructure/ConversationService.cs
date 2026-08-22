using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CentralChat.Infrastructure;

public sealed class ConversationService(CentralChatDbContext db, ITicketBroadcaster broadcast, IMediaStore media, IOptions<MediaOptions> mediaOptions) : IConversationService
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
        return await query.OrderByDescending(x => x.ProviderTimestamp).ThenByDescending(x => x.Id).Take(limit).Select(x => new MessageDto(
                x.Id, x.ConversationId, x.Direction, x.Type, x.TextBody, x.Status, x.ProviderTimestamp,
                x.ExternalMessageId, x.MimeType, x.MediaUrl != null, x.MediaSizeBytes,
                // A shared inbox needs to show which colleague replied, not just that someone did.
                x.SenderUserId == null ? null : db.Users.Where(u => u.Id == x.SenderUserId).Select(u => u.DisplayName).FirstOrDefault())).ToListAsync(ct);
    }

    public async Task<MessageDto> SendAsync(Guid id, string text, Guid userId, bool privileged, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new ValidationException("Message text is required and must not exceed 4096 characters.");
        var conversation = await GetAsync(id, userId, privileged, ct);
        await EnsureWithinSessionWindowAsync(id, ct);
        var channelId = await db.Conversations.Where(x => x.Id == id).Select(x => x.ChannelId).SingleAsync(ct);
        var message = new ChatMessage(id, conversation.ContactId, channelId, MessageDirection.Outbound, MessageType.Text, text.Trim(), null, DateTimeOffset.UtcNow); message.SetSender(userId);
        db.ChatMessages.Add(message);
        db.OutboxMessages.Add(new OutboxMessage { Type = "OutboundWhatsAppMessageRequested", Payload = JsonSerializer.Serialize(new { MessageId = message.Id }) });

        // Replying is activity too. Without this the conversation keeps sorting by the customer's last
        // message and drifts down the list the moment an agent answers it.
        var conversationRow = await db.Conversations.SingleAsync(x => x.Id == id, ct);
        conversationRow.Touch(message.ProviderTimestamp);
        var ticket = await db.Tickets.SingleOrDefaultAsync(
            x => x.ContactId == conversation.ContactId && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved, ct);
        ticket?.Touch(message.ProviderTimestamp);

        await db.SaveChangesAsync(ct);
        if (ticket is not null) await broadcast.UpsertedAsync(ticket.Id, ct);
        var senderName = await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);

        return new(message.Id, id, message.Direction, message.Type, message.TextBody, message.Status, message.ProviderTimestamp, null, message.MimeType, message.HasStoredMedia, message.MediaSizeBytes);
    }

    public async Task<MessageDto> SendAttachmentAsync(Guid id, Stream content, string fileName, string mimeType, string? caption, Guid userId, bool privileged, CancellationToken ct)
    {
        var conversation = await GetAsync(id, userId, privileged, ct);
        await EnsureWithinSessionWindowAsync(id, ct);

        // WhatsApp caps well below this; the limit here is to stop a bad upload filling the disk.
        var maxBytes = mediaOptions.Value.MaxBytes;
        if (content.CanSeek && content.Length > maxBytes)
            throw new ValidationException($"Attachments must be {maxBytes / (1024 * 1024)} MB or smaller.");
        if (string.IsNullOrWhiteSpace(mimeType)) throw new ValidationException("The file type could not be determined.");

        var kind = KindOf(mimeType);
        // Stored before anything is queued, so the outbound worker always has a file to read.
        var key = await media.SaveAsync(content, ExtensionOf(fileName), ct);

        var channelId = await db.Conversations.Where(x => x.Id == id).Select(x => x.ChannelId).SingleAsync(ct);
        var message = new ChatMessage(id, conversation.ContactId, channelId, MessageDirection.Outbound, kind, string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(), null, DateTimeOffset.UtcNow);
        message.SetSender(userId);
        message.SetStoredMedia(key, mimeType, content.CanSeek ? content.Length : 0);
        db.ChatMessages.Add(message);
        db.OutboxMessages.Add(new OutboxMessage { Type = "OutboundWhatsAppMessageRequested", Payload = JsonSerializer.Serialize(new { MessageId = message.Id }) });

        var conversationRow = await db.Conversations.SingleAsync(x => x.Id == id, ct);
        conversationRow.Touch(message.ProviderTimestamp);
        var ticket = await db.Tickets.SingleOrDefaultAsync(
            x => x.ContactId == conversation.ContactId && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved, ct);
        ticket?.Touch(message.ProviderTimestamp);

        await db.SaveChangesAsync(ct);
        if (ticket is not null) await broadcast.UpsertedAsync(ticket.Id, ct);
        var senderName = await db.Users.Where(x => x.Id == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);

        return new(message.Id, id, message.Direction, message.Type, message.TextBody, message.Status, message.ProviderTimestamp, null, message.MimeType, message.HasStoredMedia, message.MediaSizeBytes, senderName);
    }

    /// <summary>WhatsApp routes by media kind, and anything it does not recognise travels as a document.</summary>
    private static MessageType KindOf(string mimeType) => mimeType.Split('/')[0].ToLowerInvariant() switch
    {
        "image" => MessageType.Image,
        "video" => MessageType.Video,
        "audio" => MessageType.Audio,
        _ => MessageType.Document,
    };

    private static string ExtensionOf(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[(dot + 1)..] : string.Empty;
    }

    /// <summary>
    /// WhatsApp only accepts a free-form message within 24 hours of the customer's last one; outside
    /// that it requires an approved template. Meta would reject the send anyway, but only after the
    /// message had been stored and queued, leaving the agent an unexplained failed tick. Refusing here
    /// means they are told why while the text is still in the composer.
    /// </summary>
    private static readonly TimeSpan SessionWindow = TimeSpan.FromHours(24);

    private async Task EnsureWithinSessionWindowAsync(Guid conversationId, CancellationToken ct)
    {
        var lastInbound = await db.ChatMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.Direction == MessageDirection.Inbound)
            .MaxAsync(x => (DateTimeOffset?)x.ProviderTimestamp, ct);

        if (lastInbound is null)
            throw new ConflictException("WhatsApp does not allow starting a conversation with a free-form message. An approved message template is required.");

        if (DateTimeOffset.UtcNow - lastInbound.Value > SessionWindow)
            throw new ConflictException("This conversation is outside WhatsApp's 24-hour reply window. An approved message template is required to reply.");
    }
}
