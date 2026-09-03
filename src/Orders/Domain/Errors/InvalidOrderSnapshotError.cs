using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised by <see cref="Order.Rehydrate"/> when a persisted row fails a
/// structural check a legal walk through the state machine could never
/// produce — a status token outside the closed set, or a
/// <c>cancellationReason</c> that does not satisfy O6's biconditional with
/// <c>status</c>. Distinct from every "business rejection of a live
/// request" code §9.1 fixes, on purpose (follow-up 3 of feature
/// <c>orders_acceptance</c>'s brief, closing review_orders_aggregate.md's
/// advisory A3): a corrupt stored row is a load-time fault the caller did
/// not cause, not a caller's cancellation request pairing badly with an
/// order's current status, and the two must stay distinguishable once
/// features 15/41/42 branch on <see cref="DomainError.Code"/>.
/// </summary>
/// <remarks>
/// Matches #7's answer exactly rather than inventing a new shape:
/// <c>InvalidOrderSnapshotError</c> (<c>ORDER_SNAPSHOT_INVALID</c>,
/// <c>apps/orders/src/domain/order-errors.ts:125</c>) is what #7's
/// <c>reconstitute</c> raises for the same two load-time faults — the
/// status-token check and both halves of the reason/status biconditional.
/// #7 leaves O1 (empty lines) on its live, reused <c>EmptyOrderError</c>
/// rather than this type, and #8 mirrors that too:
/// <see cref="OrderMustHaveAtLeastOneLineError"/> stays the error
/// <see cref="Order.Rehydrate"/> raises for O1, and
/// <see cref="OrderLineCurrencyMismatchError"/> stays the error it raises
/// for O2 — the one check #7's <c>reconstitute</c> lacks entirely (#7's own
/// review defect D3) and #8 added deliberately (design.md §8.3), so
/// reusing the aggregate's own O2 error there is design, not an oversight
/// to fold into this type.
/// </remarks>
public sealed class InvalidOrderSnapshotError : DomainError
{
    /// <summary>
    /// <paramref name="orderId"/> is <see langword="null"/> exactly when the
    /// caller has not resolved one yet — <see cref="OrderStatuses.Parse"/>
    /// runs before an aggregate exists to have an id — mirroring #7's own
    /// <c>orderId?: UniqueId</c>.
    /// </summary>
    public InvalidOrderSnapshotError(UniqueId? orderId, string reason)
        : base("order.snapshot_invalid", orderId is { } id ? $"Order {id}: invalid snapshot: {reason}" : $"Invalid snapshot: {reason}")
    {
        OrderId = orderId;
        Reason = reason;
    }

    public UniqueId? OrderId { get; }

    public string Reason { get; }
}
