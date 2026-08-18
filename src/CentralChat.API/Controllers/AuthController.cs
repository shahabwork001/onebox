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
    [Authorize, HttpPost("logout")] public async Task<IActionResult> Logout(CancellationToken ct) { await auth.RevokeAsync(current.Id, ct); return NoContent(); }
}
