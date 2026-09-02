namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when <c>Order.Cancel</c> is attempted from a status Table T-1 has
/// no cancel edge for — <c>despatched</c>, <c>invoiced</c>, <c>paid</c>,
/// <c>completed</c> or <c>cancelled</c> itself
/// (specs/shared/requirements.md R8, R9). A specialised
/// <see cref="IllegalOrderTransitionError"/>, not a peer, so a caller that
/// wants to say "this order can no longer be cancelled" can branch on the
/// specific code while the R9 test still catches it through the base type
/// (design.md §9.1).
/// </summary>
public sealed class OrderNotCancellableError : IllegalOrderTransitionError
{
    public OrderNotCancellableError(OrderStatus from)
        : base(
            "order.not_cancellable",
            $"Order cannot be cancelled from status '{OrderStatuses.ToToken(from)}'; cancellation is legal only from placed, stock_reserved, credit_approved or confirmed.",
            from,
            OrderStatus.Cancelled)
    {
    }
}
