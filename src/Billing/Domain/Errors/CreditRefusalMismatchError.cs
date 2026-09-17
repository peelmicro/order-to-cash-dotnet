using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Refuse"/> when asked to refuse with
/// reason <c>over_limit</c> for a request that actually fits within the
/// available credit — a refusal must not lie about why (`BC14`).
/// </summary>
/// <remarks>
/// Backlog id 102: reaches a human via <c>BillingErrorMapper</c> -&gt;
/// Gateway problem+json <c>detail</c>. Rendered with the shared money-text
/// formatter (id 100). <paramref name="currency"/> is the credit line's own
/// currency (<see cref="BuyerCredit.CreditLimit"/>'s) — both amounts are
/// checked against it before this error can be raised.
/// </remarks>
public sealed class CreditRefusalMismatchError(long requestedMinorUnits, long availableMinorUnits, string currency)
    : DomainError(
        "CREDIT_REFUSAL_MISMATCH",
        $"Refusal reason 'over_limit' claimed for a request ({MoneyText.Format(requestedMinorUnits, currency)}) that fits within the available credit ({MoneyText.Format(availableMinorUnits, currency)}).")
{
    public long RequestedMinorUnits { get; } = requestedMinorUnits;

    public long AvailableMinorUnits { get; } = availableMinorUnits;
}
