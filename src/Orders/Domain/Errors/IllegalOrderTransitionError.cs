using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a <c>(from, to)</c> status pair is absent from Table T-1
/// (specs/shared/requirements.md R8, R9). Not <see langword="sealed"/>:
/// <see cref="OrderNotCancellableError"/> derives from it, because a refused
/// cancellation <em>is</em> an illegal transition, and the inheritance is
/// what lets the R9 test's exhaustive assertion over all 61 illegal pairs
/// catch the base type without special-casing the sixteen cancel-target
/// pairs (design.md §9.1).
/// </summary>
public class IllegalOrderTransitionError : DomainError
{
    public IllegalOrderTransitionError(OrderStatus from, OrderStatus to)
        : this(
            "order.illegal_transition",
            $"Cannot transition an order from '{OrderStatuses.ToToken(from)}' to '{OrderStatuses.ToToken(to)}': no such edge exists in Table T-1.",
            from,
            to)
    {
    }

    protected IllegalOrderTransitionError(string code, string message, OrderStatus from, OrderStatus to)
        : base(code, message)
    {
        From = from;
        To = to;
    }

    public OrderStatus From { get; }

    public OrderStatus To { get; }
}
