using System.Diagnostics;
using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// One durable command a saga step owes — the payload of the in-process
/// fast-path signal (design.md §5.5).
/// </summary>
/// <remarks>
/// <see cref="TraceParent"/> is captured automatically, from
/// <see cref="Activity.Current"/>, at EVERY construction site — never passed
/// explicitly — because every one of this record's ~10 call sites
/// (<c>OrderSagas.cs</c>, <c>SagaFactHandler.cs</c>,
/// <c>CancelOrderCommandHandler.cs</c>) already runs inside the
/// <see cref="Activity"/> the triggering fact/command started. Without this,
/// <c>SagaCommandDispatchWorker</c>'s drain loop dispatches under whatever
/// (unrelated, or absent) <see cref="Activity.Current"/> the BACKGROUND LOOP
/// happens to have at drain time — never the fact's own trace — which is
/// exactly the gap feature id 28's composed-stack verification found and
/// disclosed (R56): Orders/Fulfillment/Billing each observed a DIFFERENT
/// trace id for one order's happy path. <see langword="null"/> when no span
/// was active at construction — never fabricated (matches
/// <c>OutboxRelay.BuildPublishableFact</c>'s own "no stored parent, no
/// header" rule for the identical reason).
/// </remarks>
public sealed record SagaCommandRef(Guid OrderId, SagaCommandKind Command)
{
    /// <summary>The W3C <c>traceparent</c> of the <see cref="Activity"/> active when this ref was constructed, or <see langword="null"/> when none was active.</summary>
    public string? TraceParent { get; init; } = Activity.Current?.Id;
}

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
