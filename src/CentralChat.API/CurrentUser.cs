using System.Security.Claims;
using CentralChat.Application;

namespace CentralChat.API;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public Guid Id => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
    public bool HasPermission(string permission) => User.HasClaim("permission", permission);
}
