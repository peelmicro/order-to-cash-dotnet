using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a cancellation reason does not pair with the order's status
/// per Table T-1's <em>Trigger</em> column — <c>stock_rejected</c> only from
/// <c>placed</c>, <c>credit_rejected</c> only from <c>stock_reserved</c>,
/// <c>operator_cancelled</c> from any of the four cancellable states
/// (specs/shared/requirements.md R10; design.md §6.1, #7's OA4). Raised only
/// on a live <see cref="Order.Cancel"/> request — the symmetric load-time
/// fault <see cref="Order.Rehydrate"/> can observe (a persisted reason on a
/// row whose status is not <c>cancelled</c> at all) raises
/// <see cref="InvalidOrderSnapshotError"/> instead, because a corrupt stored
/// row is not the caller's cancellation request pairing badly with the
/// order's current status (orders_acceptance follow-up 3, closing
/// review_orders_aggregate.md's advisory A3).
/// </summary>
public sealed class CancellationReasonNotApplicableError : DomainError
{
    public CancellationReasonNotApplicableError(CancellationReason reason, OrderStatus status)
        : base(
            "order.cancellation_reason_not_applicable",
            $"Cancellation reason '{CancellationReasons.ToToken(reason)}' does not apply to order status '{OrderStatuses.ToToken(status)}'.")
    {
        Reason = reason;
        Status = status;
    }

    public CancellationReason Reason { get; }

    public OrderStatus Status { get; }
}
