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
}
