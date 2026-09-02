using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when <see cref="Order.Place"/> is given no lines, or
/// <c>Order.RemoveLine</c> would remove the last remaining one — invariant
/// O1 (specs/shared/requirements.md R5).
/// </summary>
public sealed class OrderMustHaveAtLeastOneLineError : DomainError
{
    public OrderMustHaveAtLeastOneLineError()
        : base("order.must_have_at_least_one_line", "An order must have at least one line.")
    {
    }
}
