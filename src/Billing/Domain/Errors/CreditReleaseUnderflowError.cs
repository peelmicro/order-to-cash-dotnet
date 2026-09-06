using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="BuyerCredit.Release"/> if it were ever asked to
/// release more than an order's outstanding exposure — <b>B5</b> forbids
/// <c>exposure(order)</c> from ever going below zero. <see cref="Release"/>
/// itself never constructs this state (it always releases exactly the
/// outstanding exposure), so this error is the domain's own defence rather
/// than a reachable caller mistake.
/// </summary>
public sealed class CreditReleaseUnderflowError(string orderReference, long outstandingMinorUnits, long requestedMinorUnits)
    : DomainError("CREDIT_RELEASE_UNDERFLOW", $"Order '{orderReference}': releasing {requestedMinorUnits} would drive exposure below zero (outstanding {outstandingMinorUnits}).")
{
    public string OrderReference { get; } = orderReference;
}
