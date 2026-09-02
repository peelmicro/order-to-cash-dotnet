using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a candidate line mutation (add, remove or change) would leave
/// <c>totalAmount</c> negative — invariant O3
/// (specs/shared/requirements.md R6). The candidate is computed and
/// validated before anything commits, so the aggregate is left untouched
/// (design.md §4.3).
/// </summary>
public sealed class OrderTotalMustNotBeNegativeError : DomainError
{
    public OrderTotalMustNotBeNegativeError(Money candidateTotalAmount)
        : base(
            "order.total_must_not_be_negative",
            $"The resulting total amount would be negative: {candidateTotalAmount.MinorUnits} {candidateTotalAmount.Currency}.")
    {
        CandidateTotalAmount = candidateTotalAmount;
    }

    public Money CandidateTotalAmount { get; }
}
