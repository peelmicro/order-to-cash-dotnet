using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// The outcome of <see cref="BuyerCredit.EvaluateHold"/> — pure, no
/// mutation, no event, no port (design.md §3.1). The four cases in
/// <c>BC26</c>'s fixed order: <see cref="AlreadyHeld"/> ranks above
/// <see cref="CurrencyMismatch"/>, which ranks above <see cref="OverLimit"/>;
/// <see cref="Fits"/> is the ONLY case the application layer may consult
/// <c>ICreditDecisionPort</c> for (`BC13`).
/// </summary>
public abstract record HoldEvaluation
{
    /// <summary>`BC7` — a `hold` entry already exists for this order, whatever its net exposure is now.</summary>
    public sealed record AlreadyHeld(Money HeldAmount) : HoldEvaluation;

    /// <summary>`BC4` — the requested currency differs from the line's.</summary>
    public sealed record CurrencyMismatch(string Expected) : HoldEvaluation;

    /// <summary>`R39` — the requested amount would exceed the credit limit.</summary>
    public sealed record OverLimit(Money AvailableCredit) : HoldEvaluation;

    /// <summary>The only case that reaches the credit-decision port.</summary>
    public sealed record Fits : HoldEvaluation;
}
