using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/dashboard"), Authorize(Policy = Permissions.TicketsView)]
public sealed class DashboardController(IDashboardService dashboard, ICurrentUser current) : ControllerBase
{
    // Agent breakdowns are only returned to callers who may already reassign work.
    [HttpGet] public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(current.Id, current.HasPermission(Permissions.TicketsAssign), ct);
}
