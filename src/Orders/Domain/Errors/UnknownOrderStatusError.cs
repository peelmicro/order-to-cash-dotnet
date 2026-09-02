using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a stored or wire-received status token — or, on
/// <c>Order.Rehydrate</c>, an <see cref="OrderStatus"/> value outside the
/// nine defined members — is not a member of the closed set.
/// </summary>
/// <remarks>
/// <b>Not one of design.md §9.1's ten named error codes.</b> §8.3 requires
/// <c>Order.Rehydrate</c> to validate "the status token is a member of the
/// closed set (a <c>nvarchar(20)</c> column can hold anything)", but §9.1's
/// error table and §1's <c>Errors/</c> layout list exactly ten types, none
/// of which fits this case — the closest analogues,
/// <see cref="OrderNotCancellableError"/> and
/// <see cref="IllegalOrderTransitionError"/>, are about a transition between
/// two <em>known</em> statuses, not an unrecognised one. This type closes
/// that gap by the same pattern §9.1 already uses for the parallel case on
/// <see cref="CancellationReason"/> (<see cref="UnknownCancellationReasonError"/>,
/// code <c>order.cancellation_reason_unknown</c>): flagged in
/// progress/impl_orders_aggregate.md rather than decided silently, per
/// CLAUDE.md's "stop and report" instruction for anything the design did not
/// settle.
/// </remarks>
public sealed class UnknownOrderStatusError : DomainError
{
    public UnknownOrderStatusError(string offendingValue)
        : base("order.status_unknown", $"'{offendingValue}' is not a recognised order status.")
    {
        OffendingValue = offendingValue;
    }

    public string OffendingValue { get; }
}
