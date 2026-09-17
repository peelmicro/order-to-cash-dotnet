using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Release"/> if it were ever asked to
/// release more than an order's outstanding exposure — <b>B5</b> forbids
/// <c>exposure(order)</c> from ever going below zero. <see cref="BuyerCredit.Release"/>
/// itself never constructs this state (it always releases exactly the
/// outstanding exposure), so this error is the domain's own defence rather
/// than a reachable caller mistake.
/// </summary>
/// <remarks>
/// Backlog id 102: reaches a human via <c>BillingErrorMapper</c> -&gt;
/// Gateway problem+json <c>detail</c>. Rendered with the shared money-text
/// formatter (id 100). <paramref name="currency"/> is the credit line's own
/// currency (<see cref="BuyerCredit.CreditLimit"/>'s).
/// </remarks>
public sealed class CreditReleaseUnderflowError(string orderReference, long outstandingMinorUnits, long requestedMinorUnits, string currency)
    : DomainError(
        "CREDIT_RELEASE_UNDERFLOW",
        $"Order '{orderReference}': releasing {MoneyText.Format(requestedMinorUnits, currency)} would drive exposure below zero (outstanding {MoneyText.Format(outstandingMinorUnits, currency)}).")
{
    public string OrderReference { get; } = orderReference;
}
