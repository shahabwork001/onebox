using System.Text.Json;
using CentralChat.Application;
using CentralChat.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CentralChat.Infrastructure;

public sealed class UserAdminService(CentralChatDbContext db, UserManager<ApplicationUser> users) : IUserAdminService
{
    public async Task<AgentDto> CreateAsync(CreateUserRequest request, Guid actingUserId, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var role = NormaliseRole(request.Role);

        if (string.IsNullOrWhiteSpace(email)) throw new ValidationException("An email address is required.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new ValidationException("A display name is required.");
        if (await users.FindByEmailAsync(email) is not null) throw new ConflictException("An account with that email already exists.");

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName };
        var created = await users.CreateAsync(user, request.Password ?? string.Empty);
        // Identity owns password and address rules; surfacing its wording beats inventing a second set.
        if (!created.Succeeded) throw new ValidationException(Describe(created));

        await users.AddToRoleAsync(user, role);
        await AuditAsync(actingUserId, "user.created", user.Id, new { user.Email, user.DisplayName, Role = role }, ct);
        return await DescribeAsync(user);
    }

    public async Task<AgentDto> UpdateAsync(Guid userId, UpdateUserRequest request, Guid actingUserId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");
        var before = new { user.DisplayName, user.IsActive, Roles = await users.GetRolesAsync(user) };

        if (!string.IsNullOrWhiteSpace(request.DisplayName)) user.DisplayName = request.DisplayName.Trim();

        if (request.IsActive.HasValue && request.IsActive.Value != user.IsActive)
        {
            // Removing your own access leaves nobody able to undo it, so it is refused outright.
            if (userId == actingUserId && !request.IsActive.Value)
                throw new ForbiddenException("You cannot deactivate your own account.");
            user.IsActive = request.IsActive.Value;
        }

        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new ValidationException(Describe(updated));

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = NormaliseRole(request.Role);
            var current = await users.GetRolesAsync(user);
            if (!current.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                if (userId == actingUserId) throw new ForbiddenException("You cannot change your own role.");
                await users.RemoveFromRolesAsync(user, current);
                await users.AddToRoleAsync(user, role);
            }
        }

        await AuditAsync(actingUserId, "user.updated", user.Id, new { Before = before, After = new { user.DisplayName, user.IsActive, Roles = await users.GetRolesAsync(user) } }, ct);
        return await DescribeAsync(user);
    }

    public async Task SetPasswordAsync(Guid userId, string password, Guid actingUserId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password ?? string.Empty);
        if (!reset.Succeeded) throw new ValidationException(Describe(reset));

        // Any session issued before the reset should stop working.
        await db.RefreshTokens.Where(x => x.UserId == user.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);

        await AuditAsync(actingUserId, "user.password.reset", user.Id, new { user.Email }, ct);
    }

    public async Task ChangeOwnPasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User not found.");
        var changed = await users.ChangePasswordAsync(user, currentPassword ?? string.Empty, newPassword ?? string.Empty);
        if (!changed.Succeeded) throw new ValidationException(Describe(changed));
        await AuditAsync(userId, "user.password.changed", user.Id, new { user.Email }, ct);
    }

    private string NormaliseRole(string? role)
    {
        var match = RolePermissions.Defaults.Keys.FirstOrDefault(x => string.Equals(x, role?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ValidationException($"Role must be one of {string.Join(", ", RolePermissions.Defaults.Keys)}.");
    }

    private async Task<AgentDto> DescribeAsync(ApplicationUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.IsActive, (await users.GetRolesAsync(user)).ToArray());

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(x => x.Description));

    private async Task AuditAsync(Guid actingUserId, string action, Guid subjectId, object detail, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = action,
            EntityType = "User",
            EntityId = subjectId.ToString(),
            NewValues = JsonSerializer.Serialize(detail),
        });
        await db.SaveChangesAsync(ct);
    }
}
