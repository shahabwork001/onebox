using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/conversations"), Authorize(Policy = Permissions.MessagesView)]
public sealed class ConversationsController(IConversationService conversations, ICurrentUser current) : ControllerBase
{
    private bool Privileged => current.HasPermission(Permissions.TicketsAssign);
    [HttpGet("{id:guid}")] public Task<ConversationDto> Get(Guid id, CancellationToken ct) => conversations.GetAsync(id, current.Id, Privileged, ct);
    [HttpGet("{id:guid}/messages")] public Task<IReadOnlyCollection<MessageDto>> Messages(Guid id, [FromQuery] Guid? before, [FromQuery] int limit = 50, CancellationToken ct = default) => conversations.MessagesAsync(id, before, limit, current.Id, Privileged, ct);
    [HttpPost("{id:guid}/messages"), Authorize(Policy = Permissions.MessagesSend)] public Task<MessageDto> Send(Guid id, SendMessageRequest request, CancellationToken ct) => conversations.SendAsync(id, request.Text, current.Id, Privileged, ct);
}
