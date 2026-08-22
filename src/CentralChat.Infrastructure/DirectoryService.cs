using CentralChat.Application;
using CentralChat.Domain;
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
        var items = await query.OrderByDescending(x => x.LastMessageAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new ContactDto(x.Id, x.DisplayName, x.PhoneNumber, x.WhatsAppUserId, x.CurrentAssignedAgentId, x.LastMessageAt, x.Status, x.MarketingOptOut)).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<ContactDto> ContactAsync(Guid id, CancellationToken ct) => await db.Contacts.AsNoTracking().Where(x => x.Id == id).Select(x => new ContactDto(x.Id, x.DisplayName, x.PhoneNumber, x.WhatsAppUserId, x.CurrentAssignedAgentId, x.LastMessageAt, x.Status, x.MarketingOptOut)).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Contact not found.");

    public async Task<ContactDto> SetMarketingOptOutAsync(Guid contactId, bool optedOut, Guid actingUserId, CancellationToken ct)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == contactId, ct) ?? throw new NotFoundException("Contact not found.");
        contact.SetMarketingOptOut(optedOut);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = optedOut ? "contact.marketing.optout" : "contact.marketing.optin",
            EntityType = "Contact",
            EntityId = contactId.ToString(),
        });
        await db.SaveChangesAsync(ct);
        return new(contact.Id, contact.DisplayName, contact.PhoneNumber, contact.WhatsAppUserId, contact.CurrentAssignedAgentId, contact.LastMessageAt, contact.Status, contact.MarketingOptOut);
    }

    public async Task<IReadOnlyCollection<AgentDto>> UsersAsync(bool includeInactive, CancellationToken ct)
    {
        // Assignment menus must not offer someone who has been deactivated; management screens ask for them.
        var query = db.Users.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var users = await query.OrderBy(x => x.DisplayName).ToListAsync(ct); var result = new List<AgentDto>(users.Count);
        foreach (var user in users) result.Add(new AgentDto(user.Id, user.Email!, user.DisplayName, user.IsActive, (await userManager.GetRolesAsync(user)).ToArray()));
        return result;
    }

    public async Task<IReadOnlyCollection<TeamDto>> TeamsAsync(CancellationToken ct) => await db.Teams.AsNoTracking().OrderBy(x => x.Name).Select(x => new TeamDto(x.Id, x.Name, db.TeamMembers.Count(m => m.TeamId == x.Id))).ToListAsync(ct);
}
