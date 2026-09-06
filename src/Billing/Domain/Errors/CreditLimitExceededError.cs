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
public sealed class CreditLimitExceededError(long requestedMinorUnits, long availableMinorUnits)
    : DomainError("CREDIT_LIMIT_EXCEEDED", $"Requested amount {requestedMinorUnits} exceeds available credit {availableMinorUnits}.")
{
    public long RequestedMinorUnits { get; } = requestedMinorUnits;

    public long AvailableMinorUnits { get; } = availableMinorUnits;
}
