using CentralChat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CentralChat.API;

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
