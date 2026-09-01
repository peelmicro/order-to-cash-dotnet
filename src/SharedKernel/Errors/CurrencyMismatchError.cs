namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when an add, subtract or ordering comparison is attempted between
/// two <see cref="Money"/> values whose currency codes differ
/// (specs/shared/requirements.md R2; domain-model.md invariant M2). There is
/// deliberately no path that converts one currency into another — this error
/// is the only outcome of a currency mismatch.
/// </summary>
public sealed class CurrencyMismatchError : DomainError
{
    public CurrencyMismatchError(string leftCurrency, string rightCurrency)
        : base(
            "money.cross_currency",
            $"Cannot combine or compare amounts in different currencies: '{leftCurrency}' and '{rightCurrency}'.")
    {
        LeftCurrency = leftCurrency;
        RightCurrency = rightCurrency;
    }

    public string LeftCurrency { get; }

    public string RightCurrency { get; }
}
