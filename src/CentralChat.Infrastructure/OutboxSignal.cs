using CentralChat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CentralChat.Infrastructure;

/// <summary>
/// The publisher used to discover new work only on its next poll, so every message — inbound and
/// outbound — carried up to a full poll interval of dead time before anyone saw it. Measured end to
/// end, a customer's message reached an agent's screen in about 1.6 seconds, nearly all of it waiting.
///
/// Writing to the outbox now wakes the publisher immediately. The poll interval stays as a fallback:
/// this signal is in-process, so a second instance still needs the timer to notice work committed by
/// the first. Extra signals are harmless — the publisher simply finds nothing and waits again.
/// </summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    public void Notify()
    {
        // A single pending permit is enough: one wake-up drains everything queued.
        try { _pending.Release(); }
        catch (SemaphoreFullException) { }
    }

    public Task WaitAsync(TimeSpan fallbackInterval, CancellationToken cancellationToken) =>
        _pending.WaitAsync(fallbackInterval, cancellationToken);

    public void Dispose() => _pending.Dispose();
}

/// <summary>
/// Signals on save rather than at each call site, so work queued by code written later is picked up
/// promptly without anyone having to remember to announce it.
/// </summary>
public sealed class OutboxSignalInterceptor(OutboxSignal signal) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SignalIfQueued(eventData);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        SignalIfQueued(eventData);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void SignalIfQueued(SaveChangesCompletedEventData eventData)
    {
        if (eventData.Context?.ChangeTracker.Entries<OutboxMessage>().Any() == true) signal.Notify();
    }
}
