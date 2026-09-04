using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>One durable command a saga step owes — the payload of the in-process fast-path signal (design.md §5.5).</summary>
public sealed record SagaCommandRef(Guid OrderId, SagaCommandKind Command);

/// <summary>
/// The in-process fast-path signal (design.md §5.5) — the fastest route from
/// a dispatch-owed application event to the dispatch worker, and never the
/// delivery guarantee itself: the durable <c>saga_commands</c> row plus the
/// sweeper is (SO3).
/// </summary>
public interface ISagaCommandSignal
{
    /// <summary>
    /// Enqueues <paramref name="commandRef"/> for dispatch. Returns
    /// <see langword="void"/> and MUST NOT block (SO10) — this is called
    /// from an <see cref="OrderToCash.Cqrs.IEventHandler{TEvent}"/>, strictly
    /// after the owning transaction committed, and blocking it would put the
    /// dispatch/retry budget back on the caller's await chain, exactly what
    /// SO10 exists to prevent. A full channel silently drops the signal
    /// (design.md §5.5) — safe, because the row is already committed
    /// <c>pending</c> and the sweeper will resume it.
    /// </summary>
    void Signal(SagaCommandRef commandRef);
}
