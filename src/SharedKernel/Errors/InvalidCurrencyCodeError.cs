namespace OrderToCash.SharedKernel.Errors;

/// <summary>
/// Raised when a <see cref="Money"/> is constructed with a currency code that
/// is not a well-formed ISO 4217 alpha-3 code — three uppercase ASCII
/// letters. (specs/shared/requirements.md R1: "an ISO 4217 alpha-3 currency
/// code".) Whether the code is a *known, seeded* currency
/// (domain-model.md §2.1) is a reference-catalogue concern of the Orders
/// context, out of scope for the shared kernel.
/// </summary>
public sealed class InvalidCurrencyCodeError : DomainError
{
    public InvalidCurrencyCodeError(string? currency)
        : base(
            "money.invalid_currency_code",
            $"'{currency ?? "<null>"}' is not a well-formed ISO 4217 alpha-3 currency code.")
    {
    }
}
