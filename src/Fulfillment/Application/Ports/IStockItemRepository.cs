using OrderToCash.Fulfillment.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// What <see cref="Application.StockReservationService"/>'s <c>already_reserved</c>
/// short-circuit reads — deliberately includes reservations whose product is
/// NOT in this request, so a terminal reservation on a product the retry
/// omitted still short-circuits (`FS5`).
/// </summary>
public sealed record StockLockResult(
    IReadOnlyDictionary<string, StockItem> ItemsByProductCode,
    IReadOnlyList<ReservationSnapshot> ExistingReservationsOfOrder);

/// <summary>
/// The non-locking pre-read for release (design.md §4.4 step 0). Carries
/// <see cref="CompanyCode"/> alongside the distinct product codes —
/// <b>a deliberate, documented extension of design.md §5.2's snippet</b>:
/// <c>asyncapi.yaml</c>'s <c>StockReleaseRequestPayload</c> carries no
/// <c>companyCode</c> field, so the only place it can come from before the
/// locking transaction opens is the order's own persisted reservations
/// (every reservation row already carries <c>company_code</c>). See
/// <c>progress/impl_fulfillment_stock.md</c> for the reasoning.
/// </summary>
public sealed record OrderReservationLookup(string CompanyCode, IReadOnlyList<string> ProductCodes);

/// <summary>The locking write-side port (design.md §5.2). No <c>tx</c> parameter anywhere — the ambient transaction comes from the caller's DI scope, exactly as Orders' <c>IOrderRepository</c> already does.</summary>
public interface IStockItemRepository
{
    /// <summary>
    /// Locks one row per distinct product code, one statement each, in the
    /// `FS19` order, then loads the order's reservations under the same lock
    /// discipline. Unknown product codes are simply absent from the returned
    /// dictionary (<see cref="StringComparer.OrdinalIgnoreCase"/> keys).
    /// </summary>
    Task<StockLockResult> LockForOrderAsync(string companyCode, IReadOnlyList<string> productCodes, OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>
    /// <c>stock.replenish</c>'s own lock — the same one-statement-per-row,
    /// `FS19`-ordered protocol as <see cref="LockForOrderAsync"/>'s stock
    /// step, but with no order and therefore no reservations step: a
    /// replenishment names no <c>orderReference</c> at all
    /// (<c>asyncapi.yaml</c> <c>StockReplenishRequestPayload</c>). A
    /// deliberate, documented addition beyond design.md §5.2's literal
    /// snippet — see <c>progress/impl_fulfillment_stock.md</c>.
    /// </summary>
    Task<IReadOnlyDictionary<string, StockItem>> LockItemsAsync(string companyCode, IReadOnlyList<string> productCodes, CancellationToken cancellationToken);

    /// <summary>Non-locking pre-read for release (design.md §4.4 step 0) — <see langword="null"/> when the order holds no reservation row at all (`FS9`).</summary>
    Task<OrderReservationLookup?> ProductCodesOfOrderAsync(OrderNumber orderReference, CancellationToken cancellationToken);

    /// <summary>Syncs each loaded item's row and its reservations, drains EVERY item's <c>DomainEvents</c> into outbox rows, then <c>SaveChangesAsync</c> — all inside the ambient transaction. Never opens its own (`R13`).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
