using System.Security.Claims;
using CentralChat.Application;
using CentralChat.Domain;
using CentralChat.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CentralChat.API;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public Guid Id => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
    public bool HasPermission(string permission) => User.HasClaim("permission", permission);
}

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { context.Response.Headers.Append("X-Correlation-Id", context.TraceIdentifier); await next(context); }
        catch (Exception ex)
        {
            var (status, title) = ex switch { ValidationException => (400, "Validation failed"), ForbiddenException => (403, "Forbidden"), NotFoundException => (404, "Not found"), ConflictException => (409, "Conflict"), _ => (500, "Unexpected server error") };
            if (status == 500) logger.LogError(ex, "Unhandled error for request {TraceId}", context.TraceIdentifier); else logger.LogInformation(ex, "Request rejected with {StatusCode}", status);
            context.Response.StatusCode = status; await Results.Problem(statusCode: status, title: title, detail: status == 500 ? "An unexpected error occurred." : ex.Message, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, bool seed)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>(); await db.Database.MigrateAsync();
        if (!seed) return;
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "SuperAdmin", "Admin", "TeamLead", "Agent" }) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
        foreach (var permissionName in RolePermissions.Defaults.Values.SelectMany(x => x).Distinct()) if (!await db.Permissions.AnyAsync(x => x.Name == permissionName)) db.Permissions.Add(new PermissionRecord { Name = permissionName });
        await db.SaveChangesAsync();
        foreach (var (roleName, permissionNames) in RolePermissions.Defaults)
        {
            var role = await roles.FindByNameAsync(roleName) ?? throw new InvalidOperationException($"Role {roleName} was not found.");
            var permissionIds = await db.Permissions.Where(x => permissionNames.Contains(x.Name)).Select(x => x.Id).ToListAsync();
            var existing = await db.RolePermissions.Where(x => x.RoleId == role.Id).Select(x => x.PermissionId).ToListAsync();
            db.RolePermissions.AddRange(permissionIds.Except(existing).Select(x => new RolePermission { RoleId = role.Id, PermissionId = x }));
        }
        await db.SaveChangesAsync();
        var seeds = new[] { ("superadmin@example.local", "Super Admin", "SuperAdmin"), ("admin@example.local", "Admin", "Admin"), ("lead@example.local", "Team Lead", "TeamLead"), ("agent1@example.local", "Agent One", "Agent"), ("agent2@example.local", "Agent Two", "Agent") };
        foreach (var (email, name, role) in seeds) { var user = await users.FindByEmailAsync(email); if (user is null) { user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name }; var result = await users.CreateAsync(user, "CentralChat1!"); if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description))); } if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role); }
        var sales = await db.Teams.SingleOrDefaultAsync(x => x.Name == "Sales"); if (sales is null) { sales = new Team { Name = "Sales" }; db.Teams.Add(sales); await db.SaveChangesAsync(); }
        foreach (var email in new[] { "agent1@example.local", "agent2@example.local" }) { var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"Seed user {email} was not found."); if (!await db.TeamMembers.AnyAsync(x => x.TeamId == sales.Id && x.UserId == user.Id)) db.TeamMembers.Add(new TeamMember { TeamId = sales.Id, UserId = user.Id }); }
        await db.SaveChangesAsync();
    }
}

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>(); return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("PostgreSQL is unreachable."); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", ex); }
    }
}

public sealed class RedisHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { await using var redis = await ConnectionMultiplexer.ConnectAsync(configuration.GetConnectionString("Redis")!); var latency = await redis.GetDatabase().PingAsync(); return HealthCheckResult.Healthy($"Redis responded in {latency.TotalMilliseconds:F0} ms."); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy("Redis is unreachable.", ex); }
    }
}

public sealed class RabbitMqHealthCheck(RabbitConnection rabbit) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { using var channel = rabbit.CreateChannel(); return Task.FromResult(channel.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ channel is closed.")); }
        catch (Exception ex) { return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", ex)); }
    }
}
