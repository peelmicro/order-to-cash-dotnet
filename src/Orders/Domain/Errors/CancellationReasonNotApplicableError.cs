using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a cancellation reason does not pair with the order's status
/// per Table T-1's <em>Trigger</em> column — <c>stock_rejected</c> only from
/// <c>placed</c>, <c>credit_rejected</c> only from <c>stock_reserved</c>,
/// <c>operator_cancelled</c> from any of the four cancellable states
/// (specs/shared/requirements.md R10; design.md §6.1, #7's OA4). Reused for
/// the symmetric case <c>Order.Rehydrate</c> can observe on load — a
/// persisted reason on an order whose status is not <c>cancelled</c> at all
/// — since that, too, is a reason recorded where it does not apply
/// (design.md §8.3).
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
