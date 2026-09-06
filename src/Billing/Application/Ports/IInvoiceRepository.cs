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
}
