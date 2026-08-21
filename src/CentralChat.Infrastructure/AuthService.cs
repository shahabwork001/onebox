using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CentralChat.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CentralChat.Infrastructure;

public static class RolePermissions
{
    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["SuperAdmin"] = [Permissions.ContactsView, Permissions.TicketsView, Permissions.TicketsClaim, Permissions.TicketsAssign, Permissions.TicketsResolve, Permissions.MessagesView, Permissions.MessagesSend, Permissions.UsersManage, Permissions.SettingsManage],
        ["Admin"] = [Permissions.ContactsView, Permissions.TicketsView, Permissions.TicketsClaim, Permissions.TicketsAssign, Permissions.TicketsResolve, Permissions.MessagesView, Permissions.MessagesSend, Permissions.UsersManage],
        ["TeamLead"] = [Permissions.ContactsView, Permissions.TicketsView, Permissions.TicketsClaim, Permissions.TicketsAssign, Permissions.TicketsResolve, Permissions.MessagesView, Permissions.MessagesSend],
        ["Agent"] = [Permissions.ContactsView, Permissions.TicketsView, Permissions.TicketsClaim, Permissions.TicketsResolve, Permissions.MessagesView, Permissions.MessagesSend]
    };
}

public sealed class AuthService(UserManager<ApplicationUser> users, CentralChatDbContext db, IOptions<JwtOptions> options) : IAuthService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email) ?? throw new ForbiddenException("Invalid credentials.");
        if (!user.IsActive || !await users.CheckPasswordAsync(user, request.Password)) throw new ForbiddenException("Invalid credentials.");
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= DateTimeOffset.UtcNow) throw new ForbiddenException("Refresh token is invalid or expired.");
        stored.RevokedAt = DateTimeOffset.UtcNow;
        var user = await users.FindByIdAsync(stored.UserId.ToString()) ?? throw new ForbiddenException("User no longer exists.");
        return await IssueAsync(user, ct);
    }

    public async Task RevokeAsync(Guid userId, CancellationToken ct)
    {
        await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var roleIds = await db.Roles.Where(x => x.Name != null && roles.Contains(x.Name)).Select(x => x.Id).ToListAsync(ct);
        var permissions = await (from rp in db.RolePermissions join permission in db.Permissions on rp.PermissionId equals permission.Id where roleIds.Contains(rp.RoleId) select permission.Name).Distinct().ToListAsync(ct);
        var expires = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Email, user.Email!), new(ClaimTypes.Name, user.DisplayName) };
        claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
        claims.AddRange(permissions.Select(x => new Claim("permission", x)));
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expires.UtcDateTime, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256));
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = Hash(refresh), ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays) });
        await db.SaveChangesAsync(ct);
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), refresh, expires, new UserDto(user.Id, user.Email!, user.DisplayName, roles.ToArray(), permissions));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
