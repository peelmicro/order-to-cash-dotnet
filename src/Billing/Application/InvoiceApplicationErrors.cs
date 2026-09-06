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

/// <summary>
/// Feature 22 — no invoice resolves for the `invoiceId`/`invoiceReference`
/// the `billing.payment.register` request named. Lives in
/// <c>Application/</c>, not <c>Domain/</c>: a contract violation of the
/// incoming command's identity, mirroring <see cref="CreditLineNotFoundError"/>.
/// </summary>
public sealed class InvoiceNotFoundError(Guid? invoiceId, string? invoiceReference)
    : Exception($"No invoice resolves for invoiceId '{invoiceId?.ToString() ?? "<none>"}' / invoiceReference '{invoiceReference ?? "<none>"}'.")
{
    public Guid? InvoiceId { get; } = invoiceId;

    public string? InvoiceReference { get; } = invoiceReference;
}

/// <summary>
/// Feature 22, R48/R49's boundary — the SAME `paymentReference` is already
/// recorded against a DIFFERENT invoice than the one this request names.
/// Raised on TWO paths that must agree (review finding N11 in #7's
/// counterpart, inherited as prevention rather than rediscovered here):
/// the fast path's `identityMatches` check (before any transaction opens),
/// and the `payments.payment_reference` UNIQUE-constraint backstop when two
/// requests race. A genuine redelivery (same reference, same invoice) never
/// reaches this — that is <c>duplicate</c>, not a conflict.
/// </summary>
public sealed class PaymentReferenceConflictError(string paymentReference)
    : Exception($"paymentReference '{paymentReference}' is already recorded against a different invoice.")
{
    public string PaymentReference { get; } = paymentReference;
}
