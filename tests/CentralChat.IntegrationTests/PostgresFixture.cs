using CentralChat.Infrastructure;
using Microsoft.EntityFrameworkCore;

// CREATE and DROP DATABASE cannot take parameters, so these statements have to be composed. The only
// value interpolated is a database name this class generates from a GUID, never anything from outside.
#pragma warning disable EF1002

namespace CentralChat.IntegrationTests;

/// <summary>
/// These tests need a real PostgreSQL, not an in-memory substitute: the behaviour they protect is
/// enforced by unique indexes, and a provider that does not apply them would pass while the bug was
/// present. Each test gets its own schema-migrated database so they cannot interfere with each other.
///
/// Set TEST_POSTGRES to run them. Without it they are skipped rather than failing, so a checkout with
/// no database available still builds and tests cleanly.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string SkipReason =
        "Set TEST_POSTGRES to a PostgreSQL connection string to run the integration tests.";

    private readonly string? _adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES");
    private readonly List<string> _databases = [];

    public bool Available => !string.IsNullOrWhiteSpace(_adminConnection);

    public async Task<CentralChatDbContext> CreateDatabaseAsync()
    {
        var name = $"onebox_test_{Guid.NewGuid():N}";
        await using (var admin = Open(_adminConnection!))
        {
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{name}\"");
        }
        _databases.Add(name);

        var context = Open(WithDatabase(_adminConnection!, name));
        await context.Database.MigrateAsync();
        return context;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!Available) return;
        await using var admin = Open(_adminConnection!);
        foreach (var name in _databases)
        {
            // Terminate first: a pooled connection would otherwise keep the database alive.
            await admin.Database.ExecuteSqlRawAsync(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'");
            await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{name}\"");
        }
    }

    private static CentralChatDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<CentralChatDbContext>().UseNpgsql(connectionString).Options);

    private static string WithDatabase(string connectionString, string database)
    {
        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', parts.Append($"Database={database}"));
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

#pragma warning restore EF1002
