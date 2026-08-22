using CentralChat.Domain;

namespace CentralChat.Application;

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserDto User);
public record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);
public record TicketListItem(Guid Id, string Number, TicketStatus Status, TicketPriority Priority, Guid ContactId, string ContactName, string PhoneNumber, Guid ConversationId, Guid? AssignedAgentId, DateTimeOffset LastActivityAt, string? LastMessage);
public record ConversationDto(Guid Id, Guid ContactId, string ContactName, string PhoneNumber, Guid? AssignedAgentId, Guid? TicketId);
public record MessageDto(Guid Id, Guid ConversationId, MessageDirection Direction, MessageType Type, string? Text, MessageStatus Status, DateTimeOffset Timestamp, string? ExternalMessageId, string? MimeType = null, bool MediaReady = false, long? MediaSizeBytes = null, string? SenderName = null);
public record SendMessageRequest(string Text);
public record AssignTicketRequest(Guid AgentId, string? Reason);
public record TicketStatusChangeRequest(string? Reason);
public record ContactDto(Guid Id, string DisplayName, string PhoneNumber, string? WhatsAppUserId, Guid? AssignedAgentId, DateTimeOffset? LastMessageAt, ContactStatus Status);
public record AgentDto(Guid Id, string Email, string DisplayName, bool IsActive, IReadOnlyCollection<string> Roles);
public record TeamDto(Guid Id, string Name, int MemberCount);
public record IngestWebhookResult(Guid EventId, bool Duplicate);
public record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int Total);

public record DashboardTotals(
    int Contacts, int Conversations, int Tickets,
    int Unassigned, int Open, int Resolved, int Closed,
    int InboundMessages, int OutboundMessages,
    double? AvgFirstResponseSeconds);

public record AgentWorkload(
    Guid AgentId, string DisplayName, string Email,
    int Claimed, int Open, int Resolved,
    double? AvgFirstResponseSeconds);

/// <summary>Agent rows are only populated for callers holding <c>tickets.assign</c>.</summary>
public record DashboardDto(DashboardTotals Totals, IReadOnlyCollection<AgentWorkload> Agents);

public record MediaContent(Stream Content, string MimeType, string FileName);

public record CreateUserRequest(string Email, string DisplayName, string Password, string Role);
public record UpdateUserRequest(string? DisplayName, string? Role, bool? IsActive);
public record SetPasswordRequest(string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

