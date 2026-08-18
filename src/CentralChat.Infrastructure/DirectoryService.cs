using CentralChat.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class DirectoryService(CentralChatDbContext db, UserManager<ApplicationUser> userManager) : IDirectoryService
{
    public async Task<PagedResult<ContactDto>> ContactsAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Contacts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => EF.Functions.ILike(x.DisplayName, $"%{term}%") || x.PhoneNumber.Contains(term)); }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.LastMessageAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new ContactDto(x.Id, x.DisplayName, x.PhoneNumber, x.WhatsAppUserId, x.CurrentAssignedAgentId, x.LastMessageAt, x.Status)).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<ContactDto> ContactAsync(Guid id, CancellationToken ct) => await db.Contacts.AsNoTracking().Where(x => x.Id == id).Select(x => new ContactDto(x.Id, x.DisplayName, x.PhoneNumber, x.WhatsAppUserId, x.CurrentAssignedAgentId, x.LastMessageAt, x.Status)).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Contact not found.");

    public async Task<IReadOnlyCollection<AgentDto>> UsersAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(ct); var result = new List<AgentDto>(users.Count);
        foreach (var user in users) result.Add(new AgentDto(user.Id, user.Email!, user.DisplayName, user.IsActive, (await userManager.GetRolesAsync(user)).ToArray()));
        return result;
    }

    public async Task<IReadOnlyCollection<TeamDto>> TeamsAsync(CancellationToken ct) => await db.Teams.AsNoTracking().OrderBy(x => x.Name).Select(x => new TeamDto(x.Id, x.Name, db.TeamMembers.Count(m => m.TeamId == x.Id))).ToListAsync(ct);
}
