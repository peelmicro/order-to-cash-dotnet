namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// The two invoice subjects this responder speaks — `specs/shared/asyncapi.yaml`
/// channels' own `address`. Guarded by `InvoiceSubjectsTests`, which reads
/// the spec as text rather than retyping the subjects — the discipline
/// <see cref="CreditSubjects"/> already establishes.
/// </summary>
public static class InvoiceSubjects
{
    public const string InvoiceIssue = "billing.invoice.issue";

    public const string InvoiceList = "billing.invoice.list";

    /// <summary>Feature 22 — added here, not a fourth subjects class: <c>billing.payment.register</c>'s primary written aggregate IS <c>Invoice</c>, the same one <see cref="InvoiceIssue"/> answers for (design.md §4.1's "one controller, not three" precedent).</summary>
    public const string PaymentRegister = "billing.payment.register";
}
