using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a line addition, removal or modification is attempted while
/// the order's status is one of the six R7 lists — invariant O4
/// (specs/shared/requirements.md R7). Evaluated first, before any
/// structural check, so removing the last line of a <c>confirmed</c> order
/// raises this and not <see cref="OrderMustHaveAtLeastOneLineError"/>
/// (design.md §5.2).
/// </summary>
public sealed class OrderLinesAreFrozenError : DomainError
{
    public OrderLinesAreFrozenError(OrderStatus status)
        : base(
            "order.lines_are_frozen",
            $"Order lines are frozen once the order reaches status '{OrderStatuses.ToToken(status)}'.")
    {
        Status = status;
    }

    public OrderStatus Status { get; }
}
