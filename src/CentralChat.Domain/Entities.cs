namespace CentralChat.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;
}

public sealed class WhatsAppChannel : Entity
{
    private WhatsAppChannel() { }
    public WhatsAppChannel(string name, string phoneNumberId, string? businessAccountId = null)
        => (Name, PhoneNumberId, BusinessAccountId) = (name, phoneNumberId, businessAccountId);
    public string Name { get; private set; } = null!;
    public string PhoneNumberId { get; private set; } = null!;
    public string? BusinessAccountId { get; private set; }
    public string? DisplayPhoneNumber { get; private set; }
    public bool IsActive { get; private set; } = true;
}

public sealed class Contact : Entity
{
    private Contact() { }
    public Contact(Guid channelId, string phoneNumber, string waId, string? profileName)
        => (ChannelId, PhoneNumber, WhatsAppUserId, ProfileName, DisplayName) = (channelId, phoneNumber, waId, profileName, profileName ?? phoneNumber);
    public Guid ChannelId { get; private set; }
    public string PhoneNumber { get; private set; } = null!;
    public string WhatsAppUserId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? ProfileName { get; private set; }
    public string? Email { get; private set; }
    public string? ExternalCustomerId { get; private set; }
    public string Source { get; private set; } = "WhatsApp";
    public ContactStatus Status { get; private set; } = ContactStatus.Active;
    public Guid? CurrentAssignedAgentId { get; private set; }
    public DateTimeOffset? LastMessageAt { get; private set; }
    public void Assign(Guid? agentId) { CurrentAssignedAgentId = agentId; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Touch(DateTimeOffset timestamp) { LastMessageAt = timestamp; UpdatedAt = DateTimeOffset.UtcNow; }
}

public sealed class Conversation : Entity
{
    private Conversation() { }
    public Conversation(Guid contactId, Guid channelId) => (ContactId, ChannelId) = (contactId, channelId);
    public Guid ContactId { get; private set; }
    public Guid ChannelId { get; private set; }
    public ConversationStatus Status { get; private set; } = ConversationStatus.Open;
    public DateTimeOffset? LastMessageAt { get; private set; }
    public void Touch(DateTimeOffset timestamp) { LastMessageAt = timestamp; UpdatedAt = DateTimeOffset.UtcNow; }
}

public sealed class Ticket : Entity
{
    private Ticket() { }
    public Ticket(Guid contactId, Guid conversationId, string number)
        => (ContactId, ConversationId, TicketNumber, LastActivityAt) = (contactId, conversationId, number, DateTimeOffset.UtcNow);
    public string TicketNumber { get; private set; } = null!;
    public Guid ContactId { get; private set; }
    public Guid ConversationId { get; private set; }
    public TicketStatus Status { get; private set; } = TicketStatus.New;
    public TicketPriority Priority { get; private set; } = TicketPriority.Normal;
    public Guid? AssignedAgentId { get; private set; }
    public Guid? AssignedTeamId { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public void Assign(Guid? agentId, Guid? teamId = null) { AssignedAgentId = agentId; AssignedTeamId = teamId; Status = TicketStatus.Open; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Touch(DateTimeOffset timestamp) { LastActivityAt = timestamp; UpdatedAt = DateTimeOffset.UtcNow; }
}

public sealed class ChatMessage : Entity
{
    private ChatMessage() { }
    public ChatMessage(Guid conversationId, Guid contactId, Guid channelId, MessageDirection direction, MessageType type, string? body, string? externalId, DateTimeOffset providerTimestamp)
        => (ConversationId, ContactId, ChannelId, Direction, Type, TextBody, ExternalMessageId, ProviderTimestamp, Status) =
           (conversationId, contactId, channelId, direction, type, body, externalId, providerTimestamp, direction == MessageDirection.Inbound ? MessageStatus.Received : MessageStatus.Queued);
    public Guid ConversationId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid ChannelId { get; private set; }
    public string? ExternalMessageId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public MessageType Type { get; private set; }
    public string? TextBody { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public MessageStatus Status { get; private set; }
    public string? MimeType { get; private set; }
    public string? MediaUrl { get; private set; }
    public DateTimeOffset ProviderTimestamp { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string? RawPayload { get; private set; }
    public void SetSender(Guid userId) => SenderUserId = userId;
    public void MarkSent(string externalId) { ExternalMessageId = externalId; Status = MessageStatus.Sent; SentAt = DateTimeOffset.UtcNow; UpdatedAt = DateTimeOffset.UtcNow; }
    public void MarkFailed(string reason) { Status = MessageStatus.Failed; FailureReason = reason; FailedAt = DateTimeOffset.UtcNow; UpdatedAt = DateTimeOffset.UtcNow; }
    public void ApplyStatus(MessageStatus status, DateTimeOffset at) { Status = status; if (status == MessageStatus.Delivered) DeliveredAt = at; if (status == MessageStatus.Read) ReadAt = at; UpdatedAt = DateTimeOffset.UtcNow; }
}
