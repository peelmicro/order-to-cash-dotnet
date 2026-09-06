using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised by <see cref="Invoice.Issue"/>/<see cref="Invoice.Reconstitute"/> when the line list is empty — invariant <b>B6</b>. The invoice mirrors the despatched lines exactly; there is never a legitimate zero-line invoice.</summary>
public sealed class EmptyInvoiceLinesError(string orderReference)
    : DomainError("EMPTY_INVOICE_LINES", $"Invoice for order '{orderReference}' must have at least one line.")
{
    public string OrderReference { get; } = orderReference;
}
