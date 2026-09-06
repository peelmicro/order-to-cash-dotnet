using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Errors;

/// <summary>
/// Raised by <see cref="Invoice.Reconstitute"/> when a persisted row would
/// already violate an invariant this aggregate exists to preserve — a
/// `status`/`paid_at` disagreement (<b>B9</b>, `BI10`), disagreeing totals
/// (<b>B6</b>), or a line whose currency differs from the invoice's own
/// (<b>B6</b>). A load-time fault, never a business rejection.
/// </summary>
public sealed class InvalidInvoiceSnapshotError(string reason)
    : DomainError("INVALID_INVOICE_SNAPSHOT", reason)
{
}
