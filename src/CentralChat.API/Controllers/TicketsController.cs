using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/tickets"), Authorize(Policy = Permissions.TicketsView)]
public sealed class TicketsController(ITicketService tickets, ICurrentUser current) : ControllerBase
{
    private bool Privileged => current.HasPermission(Permissions.TicketsAssign);
    [HttpGet] public Task<PagedResult<TicketListItem>> List([FromQuery] string scope = "all", [FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync(scope, status, current.Id, page, pageSize, ct);
    [HttpGet("mine")] public Task<PagedResult<TicketListItem>> Mine([FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync("mine", status, current.Id, page, pageSize, ct);
    [HttpGet("unassigned")] public Task<PagedResult<TicketListItem>> Unassigned([FromQuery] string? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync("unassigned", status, current.Id, page, pageSize, ct);
    [HttpPost("{id:guid}/claim"), Authorize(Policy = Permissions.TicketsClaim)] public async Task<IActionResult> Claim(Guid id, CancellationToken ct) { await tickets.ClaimAsync(id, current.Id, ct); return NoContent(); }
    [HttpPost("{id:guid}/assign"), Authorize(Policy = Permissions.TicketsAssign)] public async Task<IActionResult> Assign(Guid id, AssignTicketRequest request, CancellationToken ct) { await tickets.AssignAsync(id, request.AgentId, current.Id, request.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/reassign"), Authorize(Policy = Permissions.TicketsAssign)] public async Task<IActionResult> Reassign(Guid id, AssignTicketRequest request, CancellationToken ct) { await tickets.AssignAsync(id, request.AgentId, current.Id, request.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/unassign"), Authorize(Policy = Permissions.TicketsClaim)] public async Task<IActionResult> Unassign(Guid id, TicketStatusChangeRequest? request, CancellationToken ct) { await tickets.UnassignAsync(id, current.Id, Privileged, request?.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/resolve"), Authorize(Policy = Permissions.TicketsResolve)] public async Task<IActionResult> Resolve(Guid id, TicketStatusChangeRequest? request, CancellationToken ct) { await tickets.ResolveAsync(id, current.Id, Privileged, request?.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/close"), Authorize(Policy = Permissions.TicketsResolve)] public async Task<IActionResult> Close(Guid id, TicketStatusChangeRequest? request, CancellationToken ct) { await tickets.CloseAsync(id, current.Id, Privileged, request?.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/reopen"), Authorize(Policy = Permissions.TicketsResolve)] public async Task<IActionResult> Reopen(Guid id, TicketStatusChangeRequest? request, CancellationToken ct) { await tickets.ReopenAsync(id, current.Id, Privileged, request?.Reason, ct); return NoContent(); }
}
