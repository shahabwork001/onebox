using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class ConversationService(CentralChatDbContext db) : IConversationService
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
        return await query.OrderByDescending(x => x.ProviderTimestamp).ThenByDescending(x => x.Id).Take(limit).Select(x => new MessageDto(x.Id, x.ConversationId, x.Direction, x.Type, x.TextBody, x.Status, x.ProviderTimestamp, x.ExternalMessageId)).ToListAsync(ct);
    }

    public async Task<MessageDto> SendAsync(Guid id, string text, Guid userId, bool privileged, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new ValidationException("Message text is required and must not exceed 4096 characters.");
        var conversation = await GetAsync(id, userId, privileged, ct);
        var channelId = await db.Conversations.Where(x => x.Id == id).Select(x => x.ChannelId).SingleAsync(ct);
        var message = new ChatMessage(id, conversation.ContactId, channelId, MessageDirection.Outbound, MessageType.Text, text.Trim(), null, DateTimeOffset.UtcNow); message.SetSender(userId);
        db.ChatMessages.Add(message);
        db.OutboxMessages.Add(new OutboxMessage { Type = "OutboundWhatsAppMessageRequested", Payload = JsonSerializer.Serialize(new { MessageId = message.Id }) });
        await db.SaveChangesAsync(ct);
        return new(message.Id, id, message.Direction, message.Type, message.TextBody, message.Status, message.ProviderTimestamp, null);
    }
}
