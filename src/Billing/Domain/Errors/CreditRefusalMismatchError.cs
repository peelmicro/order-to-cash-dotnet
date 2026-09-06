using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Refuse"/> when asked to refuse with
/// reason <c>over_limit</c> for a request that actually fits within the
/// available credit — a refusal must not lie about why (`BC14`).
/// </summary>
public sealed class CreditRefusalMismatchError(long requestedMinorUnits, long availableMinorUnits)
    : DomainError("CREDIT_REFUSAL_MISMATCH", $"Refusal reason 'over_limit' claimed for a request ({requestedMinorUnits}) that fits within the available credit ({availableMinorUnits}).")
{
    public long RequestedMinorUnits { get; } = requestedMinorUnits;

    public long AvailableMinorUnits { get; } = availableMinorUnits;
}
