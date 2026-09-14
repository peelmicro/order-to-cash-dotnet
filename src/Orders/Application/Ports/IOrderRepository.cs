using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The exact shape <c>orders_aggregate</c>'s design.md §10.2 fixes — no
/// <c>tx</c> parameter (design.md §2.1: "same scope" is what "same
/// transaction" means here). <see cref="SaveChangesAsync"/> drains every
/// registered aggregate's <c>DomainEvents</c> into <c>outbox</c> rows, calls
/// <c>DbContext.SaveChangesAsync</c> once, and calls
/// <c>ClearDomainEvents()</c> only after it returns (R13, OI9).
/// </summary>
public interface IOrderRepository
{
    /// <param name="order">The newly created aggregate — tracked by the ambient <c>DbContext</c>, its <c>DomainEvents</c> drained by <see cref="SaveChangesAsync"/>, never by this method.</param>
    /// <param name="requestId">
    /// Feature <c>observability_reliability</c>, <c>RI1</c> — the client's
    /// `orders.create` idempotency key, persisted against the new row under
    /// <c>OrderConfiguration</c>'s filtered unique index. <c>null</c> when
    /// the caller omitted it (<c>RI4</c>) — never defaulted, never inferred.
    /// </param>
    /// <param name="cancellationToken">Cooperative cancellation, forwarded to the underlying EF Core call.</param>
    Task AddAsync(Order order, Guid? requestId, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken);

    Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken);

    /// <summary>
    /// Feature <c>observability_reliability</c>, <c>RI2</c>/<c>RI3</c> — a
    /// NO-TRACKING read (design.md §2.2, ledger L4): it never enters the EF
    /// adapter's identity map, so re-reading the winner after a rolled-back
    /// <c>SaveChangesAsync</c> in the same scope cannot make a later save in
    /// that scope try to write it again. Returns <c>null</c> when no order
    /// carries <paramref name="requestId"/>.
    /// </summary>
    Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>Drains every registered aggregate's <c>DomainEvents</c> into <c>outbox</c> rows, saves, and clears — in that order (R13, OI9).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
