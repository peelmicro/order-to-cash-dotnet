using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when <c>Order.RemoveLine</c> or <c>Order.ChangeLine</c> is given a
/// <c>lineId</c> that does not identify a line on the order
/// (specs/shared/requirements.md R6, R7).
/// </summary>
public sealed class OrderLineNotFoundError : DomainError
{
    public OrderLineNotFoundError(UniqueId lineId)
        : base("order.line_not_found", $"No order line with id '{lineId}' exists on this order.")
    {
        LineId = lineId;
    }

    public UniqueId LineId { get; }
}
