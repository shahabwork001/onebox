using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/contacts"), Authorize(Policy = Permissions.ContactsView), EnableRateLimiting("api")]
public sealed class ContactsController(IDirectoryService directory) : ControllerBase
{
    [HttpGet] public Task<PagedResult<ContactDto>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => directory.ContactsAsync(search, page, pageSize, ct);
    [HttpGet("{id:guid}")] public Task<ContactDto> Get(Guid id, CancellationToken ct) => directory.ContactAsync(id, ct);
}

[ApiController, Route("api/users"), Authorize(Policy = Permissions.TicketsAssign), EnableRateLimiting("api")]
public sealed class UsersController(IDirectoryService directory, IUserAdminService admin, ICurrentUser current) : ControllerBase
{
    /// <summary>Assignment menus want only people who can still take work; management wants everyone.</summary>
    [HttpGet] public Task<IReadOnlyCollection<AgentDto>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => directory.UsersAsync(includeInactive && current.HasPermission(Permissions.UsersManage), ct);

    [HttpPost, Authorize(Policy = Permissions.UsersManage)]
    public Task<AgentDto> Create(CreateUserRequest request, CancellationToken ct) => admin.CreateAsync(request, current.Id, ct);

    [HttpPatch("{id:guid}"), Authorize(Policy = Permissions.UsersManage)]
    public Task<AgentDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => admin.UpdateAsync(id, request, current.Id, ct);

    [HttpPost("{id:guid}/password"), Authorize(Policy = Permissions.UsersManage)]
    public async Task<IActionResult> SetPassword(Guid id, SetPasswordRequest request, CancellationToken ct)
    {
        await admin.SetPasswordAsync(id, request.Password, current.Id, ct);
        return NoContent();
    }
}

[ApiController, Route("api/teams"), Authorize(Policy = Permissions.TicketsView), EnableRateLimiting("api")]
public sealed class TeamsController(IDirectoryService directory) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<TeamDto>> List(CancellationToken ct) => directory.TeamsAsync(ct);
}
