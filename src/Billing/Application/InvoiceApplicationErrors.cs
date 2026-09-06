namespace OrderToCash.Billing.Application;

/// <summary>
/// `BI4` — the request's `currency` differs from the resolved credit line's.
/// Lives in `Application/`, not `Domain/`: this is a contract violation of
/// the incoming command, not a statement about the invoice's own state —
/// mirroring <see cref="CreditCurrencyMismatchError"/>.
/// </summary>
public sealed class InvoiceCurrencyMismatchError(string expected, string received)
    : Exception($"Requested currency '{received}' differs from the credit line's currency '{expected}'.")
{
    public string Expected { get; } = expected;

    public string Received { get; } = received;
}
