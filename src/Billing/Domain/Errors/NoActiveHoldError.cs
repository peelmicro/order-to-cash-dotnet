using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Consume"/> when the order named holds
/// no active hold to consume — <c>R40</c>'s precondition. Ships delivered
/// and unit-tested; feature 21's invoice-issue caller is the only thing
/// that reaches it in production.
/// </summary>
public sealed class NoActiveHoldError(string orderReference)
    : DomainError("NO_ACTIVE_HOLD", $"Order '{orderReference}' holds no active hold to consume.")
{
    public string OrderReference { get; } = orderReference;
}
