namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// Refusals raised by <see cref="CancelOrderCommandHandler"/> above the
/// domain — matching <see cref="PlaceOrderError"/>'s own split from
/// <see cref="OrderToCash.SharedKernel.DomainError"/> (design.md §9.2): the
/// responder's error mapping checks the specific application types FIRST
/// and only falls back to the generic <c>DomainError</c> catch for an
/// aggregate refusal it does not special-case. Terminal-status rejection
/// (bullet 3) is NOT here — <see cref="Domain.Errors.OrderNotCancellableError"/>
/// is a <c>DomainError</c>, raised by <c>Order.Cancel</c> itself, and mapped
/// directly to its own wire code (see <c>OrdersCreateErrorMapper</c>).
/// </summary>
public abstract class CancelOrderError(string message) : Exception(message)
{
    public abstract string Code { get; }
}

/// <summary>
/// <c>orders.cancel</c>'s <c>orderId</c> matches no order in the write
/// model — a genuinely different refusal from
/// <see cref="Domain.Errors.OrderNotCancellableError"/> (that one names an
/// order that exists but is in the wrong status).
/// </summary>
public sealed class OrderNotFoundError(Guid orderId)
    : CancelOrderError($"Order {orderId} was not found.")
{
    public override string Code => "ORDER_NOT_FOUND";

    public Guid OrderId { get; } = orderId;
}
