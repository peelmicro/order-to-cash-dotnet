using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Ports;

public interface IInvoiceRepository
{
    /// <summary>B7 fast path: a non-transactional, un-hinted read by `orderReference`, before any transaction is opened.</summary>
    Task<InvoiceSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>B7 authority: the same read under `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`, INSIDE the ambient transaction and AFTER the credits row lock (design.md §7.2 step 2).</summary>
    Task<InvoiceSnapshot?> LockByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>INSERTs the invoice row and its line rows, then drains <see cref="Invoice.DomainEvents"/> into outbox rows, then `SaveChangesAsync` — all inside the ambient transaction. Never an UPDATE on this path.</summary>
    Task SaveAsync(Invoice invoice, CancellationToken cancellationToken);

    // -- feature 22 (billing_remittance_intake) additions below — the SAME
    // aggregate, the SAME repository, per design.md §14's own anticipation.

    /// <summary>Non-transactional, un-hinted identity resolution by <c>invoiceId</c> — used both by the fast path (build the `duplicate` reply) and, before any transaction opens, to resolve the (retailerCode, companyCode, orderReference) `LockForOrderAsync` needs (`BI8`'s credit lock is always first).</summary>
    Task<InvoiceSnapshot?> FindByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken);

    /// <summary>Non-transactional, un-hinted identity resolution by <c>invoiceReference</c> — the sibling of <see cref="FindByIdAsync"/> for a caller who named the invoice by its human-readable reference instead.</summary>
    Task<InvoiceSnapshot?> FindByInvoiceReferenceAsync(string invoiceReference, CancellationToken cancellationToken);

    /// <summary>R48 fast path: a non-transactional, un-hinted lookup by `paymentReference` — a hit answers `duplicate` immediately, NO transaction opened, mirroring `FindByOrderReferenceAsync`'s own fast path.</summary>
    Task<PaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken);

    /// <summary>The authority re-read closing the race the fast path leaves open: a plain (un-hinted) read of the ONE payment (if any) already recorded for this invoice, taken AFTER <see cref="LockByIdAsync"/>'s lock is granted — under `EfCoreUnitOfWork`'s `ReadCommitted` isolation with RCSI, this sees the latest committed row, never a stale snapshot (`B8`'s own concurrency argument, ledger note in design.md §7.3).</summary>
    Task<PaymentSnapshot?> FindPaymentByInvoiceIdAsync(UniqueId invoiceId, CancellationToken cancellationToken);

    /// <summary>The `B8` authority — the invoice row locked by `id` under `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`, INSIDE the ambient transaction and AFTER the credits row lock (`BI8`'s order, extended to this subject).</summary>
    Task<InvoiceSnapshot?> LockByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken);

    /// <summary>UPDATEs the invoice row's `status`/`paid_at` (the ONE UPDATE this repository ever issues), INSERTs the `payments` row, then drains <see cref="Invoice.DomainEvents"/> (exactly one `payment.received.v1`) into the outbox, then `SaveChangesAsync` — all inside the ambient transaction, called AFTER <see cref="LockByIdAsync"/> loaded <paramref name="invoice"/>'s row in the SAME repository instance.</summary>
    Task MarkPaidAsync(Invoice invoice, MarkPaidInput payment, CancellationToken cancellationToken);
}
