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
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken);

    Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken);

    /// <summary>Drains every registered aggregate's <c>DomainEvents</c> into <c>outbox</c> rows, saves, and clears — in that order (R13, OI9).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
