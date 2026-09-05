using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>The plain, framework-free shape <see cref="StockItem.Reconstitute"/> rebuilds an aggregate from, and <see cref="StockItem.ToSnapshot"/> produces — the mapper's contract with the persistence layer.</summary>
public sealed record ReservationSnapshot(
    UniqueId Id,
    OrderNumber OrderReference,
    string CompanyCode,
    string RetailerCode,
    string ProductCode,
    int Units,
    ReservationStatus Status);

/// <summary>
/// The plain, framework-free shape a <see cref="StockItem"/> is reconstituted
/// from. <see cref="Reservations"/> is scoped to the order(s) actually loaded
/// with the item — never the item's entire history (design.md §3.1, gate
/// row 12 inherited from #7).
/// </summary>
public sealed record StockItemSnapshot(
    UniqueId Id,
    string CompanyCode,
    string ProductCode,
    int Units,
    int ReservedUnits,
    int LowStockThreshold,
    IReadOnlyList<ReservationSnapshot> Reservations);
