using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/contacts"), Authorize(Policy = Permissions.ContactsView)]
public sealed class ContactsController(IDirectoryService directory) : ControllerBase
{
    [HttpGet] public Task<PagedResult<ContactDto>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) => directory.ContactsAsync(search, page, pageSize, ct);
    [HttpGet("{id:guid}")] public Task<ContactDto> Get(Guid id, CancellationToken ct) => directory.ContactAsync(id, ct);
}

[ApiController, Route("api/users"), Authorize(Policy = Permissions.TicketsAssign)]
public sealed class UsersController(IDirectoryService directory) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<AgentDto>> List(CancellationToken ct) => directory.UsersAsync(ct);
}

[ApiController, Route("api/teams"), Authorize(Policy = Permissions.TicketsView)]
public sealed class TeamsController(IDirectoryService directory) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<TeamDto>> List(CancellationToken ct) => directory.TeamsAsync(ct);
}
