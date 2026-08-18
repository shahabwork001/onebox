using CentralChat.Domain;

namespace CentralChat.Application;

public static class Permissions
{
    public const string ContactsView = "contacts.view";
    public const string TicketsView = "tickets.view";
    public const string TicketsClaim = "tickets.claim";
    public const string TicketsAssign = "tickets.assign";
    public const string MessagesView = "messages.view";
    public const string MessagesSend = "messages.send";
    public const string UsersManage = "users.manage";
    public const string SettingsManage = "settings.manage";
}

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserDto User);
public record UserDto(Guid Id, string Email, string DisplayName, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);
public record TicketListItem(Guid Id, string Number, TicketStatus Status, TicketPriority Priority, Guid ContactId, string ContactName, string PhoneNumber, Guid ConversationId, Guid? AssignedAgentId, DateTimeOffset LastActivityAt, string? LastMessage);
public record ConversationDto(Guid Id, Guid ContactId, string ContactName, string PhoneNumber, Guid? AssignedAgentId, Guid? TicketId);
public record MessageDto(Guid Id, Guid ConversationId, MessageDirection Direction, MessageType Type, string? Text, MessageStatus Status, DateTimeOffset Timestamp, string? ExternalMessageId);
public record SendMessageRequest(string Text);
public record AssignTicketRequest(Guid AgentId, string? Reason);
public record ContactDto(Guid Id, string DisplayName, string PhoneNumber, string? WhatsAppUserId, Guid? AssignedAgentId, DateTimeOffset? LastMessageAt, ContactStatus Status);
public record AgentDto(Guid Id, string Email, string DisplayName, bool IsActive, IReadOnlyCollection<string> Roles);
public record TeamDto(Guid Id, string Name, int MemberCount);
public record IngestWebhookResult(Guid EventId, bool Duplicate);
public record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int Total);

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task RevokeAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ITicketService
{
    Task<PagedResult<TicketListItem>> ListAsync(string scope, Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task ClaimAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken);
    Task AssignAsync(Guid ticketId, Guid agentId, Guid changedBy, string? reason, CancellationToken cancellationToken);
    Task UnassignAsync(Guid ticketId, Guid changedBy, string? reason, CancellationToken cancellationToken);
}

public interface IConversationService
{
    Task<ConversationDto> GetAsync(Guid id, Guid userId, bool privileged, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MessageDto>> MessagesAsync(Guid id, Guid? beforeId, int limit, Guid userId, bool privileged, CancellationToken cancellationToken);
    Task<MessageDto> SendAsync(Guid id, string text, Guid userId, bool privileged, CancellationToken cancellationToken);
}

public interface IWebhookIngestionService
{
    bool ValidateSignature(string body, string? signature);
    Task<IngestWebhookResult> IngestAsync(string body, CancellationToken cancellationToken);
    Task ProcessAsync(Guid webhookEventId, CancellationToken cancellationToken);
}

public interface IDirectoryService
{
    Task<PagedResult<ContactDto>> ContactsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<ContactDto> ContactAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AgentDto>> UsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TeamDto>> TeamsAsync(CancellationToken cancellationToken);
}

public interface ICurrentUser
{
    Guid Id { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
}

public sealed class NotFoundException(string message) : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class ValidationException(string message) : Exception(message);
