using System.Security.Claims;
using CentralChat.Application;
using CentralChat.Domain;
using CentralChat.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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
    private static readonly (string Email, string DisplayName, string Role)[] DevelopmentUsers =
        [("superadmin@example.local", "Super Admin", "SuperAdmin"), ("admin@example.local", "Admin", "Admin"), ("lead@example.local", "Team Lead", "TeamLead"), ("agent1@example.local", "Agent One", "Agent"), ("agent2@example.local", "Agent Two", "Agent")];

    public static async Task InitializeAsync(IServiceProvider services, bool seedDevelopmentData)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>(); await db.Database.MigrateAsync();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CentralChat.API.DatabaseInitializer");
        await SeedRolesAndPermissionsAsync(scope.ServiceProvider, db);
        await SeedBootstrapAccountsAsync(scope.ServiceProvider, db, logger);
        if (seedDevelopmentData) await SeedDevelopmentAccountsAsync(scope.ServiceProvider, db, logger);
    }

    // Every permission claim on a JWT is read back from these tables at login, so they are required in
    // every environment, not only where the development accounts below are seeded.
    private static async Task SeedRolesAndPermissionsAsync(IServiceProvider services, CentralChatDbContext db)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in RolePermissions.Defaults.Keys) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
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
    }

    // Configured through Bootstrap__* environment variables so a deployment can create its first accounts
    // without credentials living in the repository. Re-running only fills gaps; existing passwords stand.
    private static async Task SeedBootstrapAccountsAsync(IServiceProvider services, CentralChatDbContext db, ILogger logger)
    {
        var options = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        if (!options.IsConfigured) { logger.LogInformation("Bootstrap accounts are not configured; set Bootstrap__AdminEmail and Bootstrap__AdminPassword to create them."); return; }
        if (!RolePermissions.Defaults.ContainsKey(options.AdminRole)) { logger.LogError("Bootstrap:AdminRole {Role} is not one of {Known}; no accounts were created.", options.AdminRole, string.Join(", ", RolePermissions.Defaults.Keys)); return; }
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        await EnsureUserAsync(users, logger, options.AdminEmail!, options.AdminDisplayName, options.AdminRole, options.AdminPassword!);

        var agentEmails = options.AgentEmailList;
        if (agentEmails.Count == 0) return;
        if (string.IsNullOrWhiteSpace(options.AgentPassword)) { logger.LogError("Bootstrap:AgentEmails is set but Bootstrap:AgentPassword is missing; no agent accounts were created."); return; }
        var agents = new List<ApplicationUser>();
        foreach (var email in agentEmails) { var agent = await EnsureUserAsync(users, logger, email, DisplayNameFor(email), "Agent", options.AgentPassword); if (agent is not null) agents.Add(agent); }
        await EnsureTeamAsync(db, options.TeamName, agents);
    }

    private static async Task SeedDevelopmentAccountsAsync(IServiceProvider services, CentralChatDbContext db, ILogger logger)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>(); var agents = new List<ApplicationUser>();
        foreach (var (email, displayName, role) in DevelopmentUsers) { var user = await EnsureUserAsync(users, logger, email, displayName, role, "CentralChat1!"); if (user is not null && role == "Agent") agents.Add(user); }
        await EnsureTeamAsync(db, "Sales", agents);
    }

    private static async Task EnsureTeamAsync(CentralChatDbContext db, string teamName, IReadOnlyCollection<ApplicationUser> members)
    {
        if (members.Count == 0) return;
        var team = await db.Teams.SingleOrDefaultAsync(x => x.Name == teamName); if (team is null) { team = new Team { Name = teamName }; db.Teams.Add(team); await db.SaveChangesAsync(); }
        foreach (var member in members) if (!await db.TeamMembers.AnyAsync(x => x.TeamId == team.Id && x.UserId == member.Id)) db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = member.Id });
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser?> EnsureUserAsync(UserManager<ApplicationUser> users, ILogger logger, string email, string displayName, string role, string password)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName };
            var result = await users.CreateAsync(user, password);
            // A rejected password or address must not stop the host from starting and ingesting webhooks.
            if (!result.Succeeded) { logger.LogError("Could not create account {Email}: {Errors}", email, string.Join("; ", result.Errors.Select(x => x.Description))); return null; }
            logger.LogInformation("Created account {Email} with role {Role}", email, role);
        }
        if (!await users.IsInRoleAsync(user, role)) await users.AddToRoleAsync(user, role);
        return user;
    }

    private static string DisplayNameFor(string email)
    {
        var local = email.Split('@')[0];
        return local.Length == 0 ? email : char.ToUpperInvariant(local[0]) + local[1..];
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
