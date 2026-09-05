using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>
/// A child entity of <see cref="StockItem"/>, with identity within the
/// aggregate but no life of its own (specs/shared/domain-model.md §4.1). A
/// <see langword="sealed class"/>, not a record — two reservations of the
/// same quantity for the same order/product are not the same reservation.
/// Reachable only through its owning <see cref="StockItem"/>: nobody can move
/// a reservation without the owning item's counter moving with it
/// (design.md §3.2).
/// </summary>
public sealed class Reservation : Entity
{
    private Reservation(
        UniqueId id,
        OrderNumber orderReference,
        string companyCode,
        string retailerCode,
        string productCode,
        Quantity units,
        ReservationStatus status)
        : base(id)
    {
        OrderReference = orderReference;
        CompanyCode = companyCode;
        RetailerCode = retailerCode;
        ProductCode = productCode;
        Units = units;
        Status = status;
    }

    public OrderNumber OrderReference { get; }

    public string CompanyCode { get; }

    public string RetailerCode { get; }

    public string ProductCode { get; }

    public Quantity Units { get; }

    public ReservationStatus Status { get; private set; }

    /// <summary>Creates a brand-new reservation in status <c>reserved</c> — the only way one is born.</summary>
    public static Reservation Create(UniqueId id, OrderNumber orderReference, string companyCode, string retailerCode, string productCode, Quantity units) =>
        new(id, orderReference, companyCode, retailerCode, productCode, units, ReservationStatus.Reserved);

    /// <summary>Restores a reservation from its persisted row, in whatever status it was stored — never through <see cref="Create"/>, and never re-validated against the state machine (a legal walk already produced it).</summary>
    public static Reservation Reconstitute(UniqueId id, OrderNumber orderReference, string companyCode, string retailerCode, string productCode, Quantity units, ReservationStatus status) =>
        new(id, orderReference, companyCode, retailerCode, productCode, units, status);

    /// <summary><c>reserved -&gt; released</c>; anything else throws <see cref="ReservationTerminalError"/> and changes nothing (F4, `R35`).</summary>
    public void Release()
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new ReservationTerminalError(Status, "release");
        }

        Status = ReservationStatus.Released;
    }

    /// <summary><c>reserved -&gt; consumed</c>; anything else throws <see cref="ReservationTerminalError"/> and changes nothing (F4).</summary>
    public void Consume()
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new ReservationTerminalError(Status, "consume");
        }

        Status = ReservationStatus.Consumed;
    }
}
