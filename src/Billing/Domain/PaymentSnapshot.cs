using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>
/// One persisted `payments` row's plain shape — feature 22's read side,
/// mirroring <see cref="InvoiceSnapshot"/>/<see cref="CreditLedgerEntrySnapshot"/>.
/// There is no <c>Payment</c> aggregate: a remittance carries no invariant
/// beyond what <see cref="Invoice.MarkPaid"/> already enforces (`B10`), so
/// this is a read-only projection of the row, never reconstituted into a
/// domain object of its own (design.md §14's own anticipation).
/// </summary>
public sealed record PaymentSnapshot(
    UniqueId Id,
    string PaymentReference,
    UniqueId InvoiceId,
    Money Amount,
    DateTimeOffset ValueDate,
    string Source);
