using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.UnitTests.TestSupport;

/// <summary>
/// A genuine hand-rolled fake — real call records, no framework — mirroring
/// this repository's own convention (e.g. Orders' <c>IIdempotentSagaRunner</c>
/// fakes). Every eventId is "processed" the first time <see cref="RecordAsync"/>
/// is called for it, and "duplicate" on every call after that — the same
/// shape a real unique-index-backed ledger has, without a database.
/// </summary>
public sealed class FakeNotificationIdempotency : INotificationIdempotency
{
    private readonly HashSet<Guid> _recorded = [];

    public List<Guid> RecordCalls { get; } = [];

    public List<Guid> DeleteCalls { get; } = [];

    /// <summary>When set, <see cref="RecordAsync"/> throws this instead of recording — unused by default.</summary>
    public Exception? ThrowOnRecord { get; set; }

    /// <summary>When set, <see cref="DeleteRecordAsync"/> throws this instead of deleting — the N13-style probe.</summary>
    public Exception? ThrowOnDelete { get; set; }

    public Task<NotificationIdempotencyOutcome> RecordAsync(Guid eventId, CancellationToken cancellationToken)
    {
        RecordCalls.Add(eventId);

        if (ThrowOnRecord is { } exception)
        {
            throw exception;
        }

        if (!_recorded.Add(eventId))
        {
            return Task.FromResult(NotificationIdempotencyOutcome.Duplicate);
        }

        return Task.FromResult(NotificationIdempotencyOutcome.Processed);
    }

    public Task DeleteRecordAsync(Guid eventId, CancellationToken cancellationToken)
    {
        DeleteCalls.Add(eventId);

        if (ThrowOnDelete is { } exception)
        {
            throw exception;
        }

        _recorded.Remove(eventId);
        return Task.CompletedTask;
    }
}
