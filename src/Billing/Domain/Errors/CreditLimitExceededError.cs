using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Approve"/> when the caller asks it to
/// approve a hold <see cref="BuyerCredit.EvaluateHold"/> would not answer
/// <c>Fits</c> for — a caller cannot bypass <b>B1</b> by skipping the
/// evaluation. Carries both amounts so a caller that DID call
/// <c>EvaluateHold</c> first (the only supported path) never actually
/// reaches this.
/// </summary>
/// <remarks>
/// Backlog id 102: reaches a human via <c>BillingErrorMapper</c> -&gt;
/// Gateway problem+json <c>detail</c>. Rendered with the shared money-text
/// formatter (id 100). <paramref name="currency"/> is the credit line's own
/// currency (<see cref="BuyerCredit.CreditLimit"/>'s).
/// </remarks>
public sealed class CreditLimitExceededError(long requestedMinorUnits, long availableMinorUnits, string currency)
    : DomainError(
        "CREDIT_LIMIT_EXCEEDED",
        $"Requested amount {MoneyText.Format(requestedMinorUnits, currency)} exceeds available credit {MoneyText.Format(availableMinorUnits, currency)}.")
{
    public long RequestedMinorUnits { get; } = requestedMinorUnits;

    public long AvailableMinorUnits { get; } = availableMinorUnits;
}
