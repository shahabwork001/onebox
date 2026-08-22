using CentralChat.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CentralChat.API.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public sealed class AuthController(IAuthService auth, ICurrentUser current) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")] public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);
    [AllowAnonymous, HttpPost("refresh")] public Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken ct) => auth.RefreshAsync(request, ct);
    /// <summary>
    /// Changing your own password belongs with authentication, not user administration: the users
    /// controller is gated on tickets.assign, which would have locked ordinary agents out of it.
    /// </summary>
    [Authorize, HttpPost("password")] public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, IUserAdminService admin, CancellationToken ct) { await admin.ChangeOwnPasswordAsync(current.Id, request.CurrentPassword, request.NewPassword, ct); return NoContent(); }
        [Authorize, HttpPost("logout")] public async Task<IActionResult> Logout(CancellationToken ct) { await auth.RevokeAsync(current.Id, ct); return NoContent(); }
}
