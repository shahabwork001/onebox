namespace CentralChat.Application;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task RevokeAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ITicketService
{
    Task<PagedResult<TicketListItem>> ListAsync(string scope, string? status, Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task ClaimAsync(Guid ticketId, Guid userId, CancellationToken cancellationToken);
    Task AssignAsync(Guid ticketId, Guid agentId, Guid changedBy, string? reason, CancellationToken cancellationToken);
    Task UnassignAsync(Guid ticketId, Guid changedBy, bool privileged, string? reason, CancellationToken cancellationToken);
    Task ResolveAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken cancellationToken);
    Task CloseAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken cancellationToken);
    Task ReopenAsync(Guid ticketId, Guid userId, bool privileged, string? reason, CancellationToken cancellationToken);
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

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(Guid userId, bool privileged, CancellationToken cancellationToken);
}

/// <summary>
/// Callers only ever handle an opaque storage key, so the backing store can move from local disk to
/// object storage without touching anything above it.
/// </summary>
public interface IMediaStore
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);
    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface IMediaService
{
    /// <summary>Fetches the provider's binary for a message and stores it. Safe to retry.</summary>
    Task DownloadAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Opens stored media once the caller is allowed to read the owning conversation.</summary>
    Task<MediaContent> OpenAsync(Guid messageId, Guid userId, bool privileged, CancellationToken cancellationToken);
}

