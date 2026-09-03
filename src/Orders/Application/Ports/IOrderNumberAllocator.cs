using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// Allocates the next <c>ORD-######</c> business reference under a row lock
/// on <c>otc_orders.order_number_sequences</c> (Databases doc §4.2, §3;
/// <c>OrderNumberSequenceConfiguration</c>'s own remark: "Allocation under a
/// row lock (<c>UPDLOCK</c>) is a repository concern, out of scope [t]here" —
/// this feature is where it lands). Called from inside the SAME transaction
/// <see cref="IUnitOfWork"/> opens for placing the order, so a rollback
/// returns the number rather than burns it (matching #7's own
/// <c>order-number-allocator.ts</c> design note, reproduced verbatim in
/// <c>PlaceOrderCommandHandler</c>).
/// </summary>
public interface IOrderNumberAllocator
{
    Task<OrderNumber> AllocateNextAsync(CancellationToken cancellationToken);
}
