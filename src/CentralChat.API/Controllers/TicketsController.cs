using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/tickets"), Authorize(Policy = Permissions.TicketsView)]
public sealed class TicketsController(ITicketService tickets, ICurrentUser current) : ControllerBase
{
    [HttpGet] public Task<PagedResult<TicketListItem>> List([FromQuery] string scope = "all", [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync(scope, current.Id, page, pageSize, ct);
    [HttpGet("mine")] public Task<PagedResult<TicketListItem>> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync("mine", current.Id, page, pageSize, ct);
    [HttpGet("unassigned")] public Task<PagedResult<TicketListItem>> Unassigned([FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => tickets.ListAsync("unassigned", current.Id, page, pageSize, ct);
    [HttpPost("{id:guid}/claim"), Authorize(Policy = Permissions.TicketsClaim)] public async Task<IActionResult> Claim(Guid id, CancellationToken ct) { await tickets.ClaimAsync(id, current.Id, ct); return NoContent(); }
    [HttpPost("{id:guid}/assign"), Authorize(Policy = Permissions.TicketsAssign)] public async Task<IActionResult> Assign(Guid id, AssignTicketRequest request, CancellationToken ct) { await tickets.AssignAsync(id, request.AgentId, current.Id, request.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/reassign"), Authorize(Policy = Permissions.TicketsAssign)] public async Task<IActionResult> Reassign(Guid id, AssignTicketRequest request, CancellationToken ct) { await tickets.AssignAsync(id, request.AgentId, current.Id, request.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/unassign"), Authorize(Policy = Permissions.TicketsAssign)] public async Task<IActionResult> Unassign(Guid id, CancellationToken ct) { await tickets.UnassignAsync(id, current.Id, null, ct); return NoContent(); }
}
