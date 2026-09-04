namespace OrderToCash.Orders.Application.Ports;

/// <summary>Mirrors <c>OrderToCash.Orders.Infrastructure.Messaging.ConsumptionOutcome</c> without requiring <see cref="SagaFactHandler"/> to reference that Infrastructure enum directly.</summary>
public enum IdempotentSagaRunOutcome
{
    Processed,
    Duplicate,
}

/// <summary>
/// A thin seam over the EXISTING, UNMODIFIED
/// <c>OrderToCash.Orders.Infrastructure.Messaging.IdempotentConsumer</c>
/// (design.md §5.1) — fixed to <c>ConsumerName.OrdersSaga</c>, so
/// <c>OrderToCash.Orders.Application.Sagas.SagaFactHandler</c> depends on a
/// fakeable port rather than a concrete class whose own dedup insert
/// requires a real <c>DbContext</c>.
/// The one implementation, <c>IdempotentConsumerSagaRunner</c>, composes the
/// canonical class verbatim — it wraps it, it does not replace or alter it.
/// </summary>
public interface IIdempotentSagaRunner
{
    Task<IdempotentSagaRunOutcome> RunOnceAsync(Guid eventId, Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
