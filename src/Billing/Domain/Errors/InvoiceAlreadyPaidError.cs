using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>Raised by <see cref="Invoice.MarkPaid"/> when the invoice is already <c>paid</c> — invariant <b>B8</b>: <c>issued → paid</c> is the only transition, and it happens once.</summary>
public sealed class InvoiceAlreadyPaidError(string invoiceReference)
    : DomainError("INVOICE_ALREADY_PAID", $"Invoice '{invoiceReference}' has already been paid.")
{
    public string InvoiceReference { get; } = invoiceReference;
}
