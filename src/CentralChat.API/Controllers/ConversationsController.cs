using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/conversations"), Authorize(Policy = Permissions.MessagesView), EnableRateLimiting("api")]
public sealed class ConversationsController(IConversationService conversations, ICurrentUser current) : ControllerBase
{
    private bool Privileged => current.HasPermission(Permissions.TicketsAssign);
    [HttpGet("{id:guid}")] public Task<ConversationDto> Get(Guid id, CancellationToken ct) => conversations.GetAsync(id, current.Id, Privileged, ct);
    [HttpGet("{id:guid}/messages")] public Task<IReadOnlyCollection<MessageDto>> Messages(Guid id, [FromQuery] Guid? before, [FromQuery] int limit = 50, CancellationToken ct = default) => conversations.MessagesAsync(id, before, limit, current.Id, Privileged, ct);
    [HttpPost("{id:guid}/messages"), Authorize(Policy = Permissions.MessagesSend)] public Task<MessageDto> Send(Guid id, SendMessageRequest request, CancellationToken ct) => conversations.SendAsync(id, request.Text, current.Id, Privileged, ct);

    /// <summary>
    /// Multipart because the browser is uploading a file. The stored message is returned exactly as a
    /// text reply is, so the client appends it to the transcript the same way.
    /// </summary>
    [HttpPost("{id:guid}/attachments"), Authorize(Policy = Permissions.MessagesSend), RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<MessageDto> SendAttachment(Guid id, IFormFile file, [FromForm] string? caption, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("A file is required.");
        await using var content = file.OpenReadStream();
        return await conversations.SendAttachmentAsync(id, content, file.FileName, file.ContentType, caption, current.Id, Privileged, ct);
    }
}
