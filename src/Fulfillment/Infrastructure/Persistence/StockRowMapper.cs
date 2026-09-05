using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using RowReservation = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Reservation;
using RowStock = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Stock;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>Rows &lt;-&gt; <see cref="StockItem"/> — snapshot in, snapshot out (design.md §2).</summary>
public static class StockRowMapper
{
    /// <summary>Reconstitutes the aggregate from its stock row and the reservation rows loaded WITH it (never the item's entire history).</summary>
    public static StockItem ToDomain(RowStock row, IReadOnlyList<RowReservation> reservationRows)
    {
        var snapshot = new StockItemSnapshot(
            UniqueId.From(row.Id),
            row.CompanyCode,
            row.ProductCode,
            row.Units,
            row.ReservedUnits,
            row.LowStockThreshold,
            [.. reservationRows.Select(ToReservationSnapshot)]);

        return StockItem.Reconstitute(snapshot);
    }

    /// <summary>Copies the two mutable counters onto an already-tracked (or brand-new) row.</summary>
    public static void SyncMutableFields(RowStock row, StockItem aggregate, DateTime updatedAt)
    {
        row.Units = aggregate.Units;
        row.ReservedUnits = aggregate.ReservedUnits;
        row.UpdatedAt = updatedAt;
    }

    private static ReservationSnapshot ToReservationSnapshot(RowReservation row) => new(
        UniqueId.From(row.Id),
        OrderNumber.Parse(row.OrderReference),
        row.CompanyCode,
        row.RetailerCode,
        row.ProductCode,
        row.Units,
        ReservationStatuses.Parse(row.Status));
}
