using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CentralChat.Infrastructure;

/// <summary>
/// The outbox, inbox and webhook tables are append-only on the hot path: nothing in the message flow
/// ever deletes from them, and <see cref="WebhookEvent"/> keeps the full raw provider payload. Left
/// alone they grow without bound, which costs storage and — because the outbox publisher polls every
/// couple of seconds — steadily slows the path every inbound and outbound message travels.
///
/// Rows are only removed once they can no longer affect behaviour: an outbox row after it has been
/// published, an inbox row once redelivery of that message is long past, and a webhook event once it
/// is far outside any provider replay window.
/// </summary>
public sealed class RetentionService(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger) : BackgroundService
{
    private readonly RetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Retention is disabled; processed integration rows will be kept indefinitely.");
            return;
        }

        // Let migrations and the first burst of traffic settle before touching anything.
        await Task.Delay(TimeSpan.FromMinutes(_options.StartupDelayMinutes), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retention sweep failed; retrying on the next interval");
            }

            await Task.Delay(TimeSpan.FromHours(_options.IntervalHours), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentralChatDbContext>();
        var now = DateTimeOffset.UtcNow;

        var outbox = await DeleteInBatchesAsync(
            () => db.OutboxMessages.Where(x => x.ProcessedAt != null && x.ProcessedAt < now.AddDays(-_options.OutboxDays)).Select(x => x.Id),
            ids => db.OutboxMessages.Where(x => ids.Contains(x.Id)),
            ct);

        var inbox = await DeleteInBatchesAsync(
            () => db.InboxMessages.Where(x => x.ProcessedAt < now.AddDays(-_options.InboxDays)).Select(x => x.Id),
            ids => db.InboxMessages.Where(x => ids.Contains(x.Id)),
            ct);

        var webhooks = await DeleteInBatchesAsync(
            () => db.WebhookEvents.Where(x => x.ProcessingStatus == WebhookProcessingStatus.Processed && x.ReceivedAt < now.AddDays(-_options.WebhookEventDays)).Select(x => x.Id),
            ids => db.WebhookEvents.Where(x => ids.Contains(x.Id)),
            ct);

        if (outbox + inbox + webhooks > 0)
            logger.LogInformation("Retention removed {Outbox} outbox, {Inbox} inbox and {Webhooks} webhook rows", outbox, inbox, webhooks);
    }

    /// <summary>
    /// Deletes in bounded batches so a first sweep over a long-neglected table cannot hold a single
    /// enormous transaction open against the tables the message flow is actively writing to.
    /// </summary>
    private async Task<int> DeleteInBatchesAsync<TEntity>(
        Func<IQueryable<Guid>> expired,
        Func<List<Guid>, IQueryable<TEntity>> matching,
        CancellationToken ct)
        where TEntity : class
    {
        var removed = 0;

        while (!ct.IsCancellationRequested)
        {
            var ids = await expired().Take(_options.BatchSize).ToListAsync(ct);
            if (ids.Count == 0) break;

            removed += await matching(ids).ExecuteDeleteAsync(ct);
            if (ids.Count < _options.BatchSize) break;
        }

        return removed;
    }
}
