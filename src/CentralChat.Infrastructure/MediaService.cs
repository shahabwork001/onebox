using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CentralChat.Infrastructure;

public sealed class MediaService(
    CentralChatDbContext db,
    IWhatsAppClient whatsApp,
    IMediaStore store,
    IConversationService conversations,
    IRealtimeNotifier realtime,
    IOptions<MediaOptions> options,
    ILogger<MediaService> logger) : IMediaService
{
    private readonly MediaOptions _options = options.Value;

    public async Task DownloadAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.ChatMessages.SingleOrDefaultAsync(x => x.Id == messageId, ct);
        if (message is null) { logger.LogWarning("Media download skipped: message {MessageId} no longer exists", messageId); return; }
        if (message.HasStoredMedia) return;                       // already fetched by an earlier delivery
        if (string.IsNullOrWhiteSpace(message.MediaId)) return;   // nothing to fetch

        var media = await whatsApp.DownloadMediaAsync(message.MediaId, ct);
        if (media is null) throw new InvalidOperationException($"Provider returned no media for {message.MediaId}.");

        if (media.Content.LongLength > _options.MaxBytes)
        {
            // Refusing loudly beats filling the disk, and the message still reads with its caption.
            logger.LogWarning("Media for message {MessageId} is {Bytes} bytes, over the {Max} limit; not stored", messageId, media.Content.LongLength, _options.MaxBytes);
            return;
        }

        var mime = media.MimeType ?? message.MimeType;
        await using var content = new MemoryStream(media.Content, writable: false);
        var key = await store.SaveAsync(content, ExtensionFor(mime), ct);

        message.SetStoredMedia(key, mime, media.Content.LongLength);
        await db.SaveChangesAsync(ct);

        // The transcript already showed this message; tell open conversations the media is now viewable.
        await realtime.ConversationAsync(message.ConversationId, "message.updated",
            new { message.Id, message.ConversationId, MediaReady = true, message.MimeType, message.MediaSizeBytes }, ct);

        logger.LogInformation("Stored media for message {MessageId} as {Key}", messageId, key);
    }

    public async Task<MediaContent> OpenAsync(Guid messageId, Guid userId, bool privileged, CancellationToken ct)
    {
        var message = await db.ChatMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId, ct)
            ?? throw new NotFoundException("Message not found.");

        // Media inherits the conversation's ownership rules exactly; this throws for the wrong agent.
        await conversations.GetAsync(message.ConversationId, userId, privileged, ct);

        if (!message.HasStoredMedia) throw new NotFoundException("Media is not available for this message yet.");

        var content = await store.OpenAsync(message.MediaUrl!, ct)
            ?? throw new NotFoundException("Stored media is missing.");

        var mime = message.MimeType ?? "application/octet-stream";
        return new MediaContent(content, mime, $"{messageId}{ExtensionFor(mime)}");
    }

    private static string ExtensionFor(string? mimeType) => (mimeType?.Split(';')[0].Trim().ToLowerInvariant()) switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "video/mp4" => ".mp4",
        "video/3gpp" => ".3gp",
        "audio/mpeg" => ".mp3",
        "audio/ogg" => ".ogg",
        "audio/aac" => ".aac",
        "audio/amr" => ".amr",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        _ => string.Empty,
    };
}
