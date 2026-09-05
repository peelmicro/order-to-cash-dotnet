using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>A frozen, method-less projection of a <see cref="Reservation"/> — the only way a caller outside this aggregate observes a reservation's fields, so nobody can call <c>Release()</c>/<c>Consume()</c> on one directly without going through the owning <see cref="StockItem"/> (design.md §3.2).</summary>
public sealed record ReservationView(
    UniqueId Id,
    OrderNumber OrderReference,
    string CompanyCode,
    string RetailerCode,
    string ProductCode,
    int Units,
    ReservationStatus Status);

/// <summary>
/// The aggregate root for one <c>(companyCode, productCode)</c> row
/// (specs/shared/domain-model.md §4.1). Invariant <b>F1</b>
/// (<c>reservedUnits ≤ units</c>) lives here, not in the schema — a check
/// constraint would fire on legitimate intermediate states inside one
/// transaction and would duplicate logic the aggregate must have anyway to
/// produce a <c>stock.rejected.v1</c> <b>fact</b> rather than a raw provider
/// error (design.md §3.1).
/// </summary>
public sealed class StockItem : AggregateRoot
{
    private readonly List<Reservation> _reservations = [];

    private StockItem(UniqueId id, string companyCode, string productCode, int units, int reservedUnits, int lowStockThreshold)
        : base(id)
    {
        CompanyCode = companyCode;
        ProductCode = productCode;
        Units = units;
        ReservedUnits = reservedUnits;
        LowStockThreshold = lowStockThreshold;
    }

    public string CompanyCode { get; }

    public string ProductCode { get; }

    public int Units { get; private set; }

    public int ReservedUnits { get; private set; }

    public int LowStockThreshold { get; }

    /// <summary>Derived, never stored (asyncapi <c>StockView</c>) — availability is decided by subtraction so the F1 test itself cannot overflow (`FS20`).</summary>
    public int AvailableUnits => Units - ReservedUnits;

    /// <summary>The reservations LOADED with this item — scoped to the order(s) being handled, never the item's entire history (design.md §3.1, gate row 12).</summary>
    public IReadOnlyList<ReservationView> Reservations => [.. _reservations.Select(ToView)];

    /// <summary>Restores a persisted row. Refuses F1 violations, negatives, and a reservation set whose reserved units do not fit an <c>int</c> (<see cref="InvalidStockItemSnapshotError"/>). Trusts the stored <see cref="ReservedUnits"/> counter — it IS the authoritative cache — rather than recomputing it from the (possibly partial) loaded reservation set.</summary>
    public static StockItem Reconstitute(StockItemSnapshot snapshot)
    {
        if (snapshot.Units < 0)
        {
            throw new InvalidStockItemSnapshotError(snapshot.Id, "units must not be negative.");
        }

        if (snapshot.ReservedUnits < 0)
        {
            throw new InvalidStockItemSnapshotError(snapshot.Id, "reservedUnits must not be negative.");
        }

        if (snapshot.ReservedUnits > snapshot.Units)
        {
            throw new InvalidStockItemSnapshotError(snapshot.Id, $"reservedUnits ({snapshot.ReservedUnits}) must not exceed units ({snapshot.Units}) — invariant F1.");
        }

        long reservedSum = 0;
        foreach (var reservation in snapshot.Reservations)
        {
            if (reservation.Status == ReservationStatus.Reserved)
            {
                reservedSum += reservation.Units;

                if (reservedSum > int.MaxValue)
                {
                    throw new InvalidStockItemSnapshotError(snapshot.Id, "the loaded reservation set's reserved units do not fit an int.");
                }
            }
        }

        var item = new StockItem(snapshot.Id, snapshot.CompanyCode, snapshot.ProductCode, snapshot.Units, snapshot.ReservedUnits, snapshot.LowStockThreshold);

        foreach (var reservation in snapshot.Reservations)
        {
            item._reservations.Add(Reservation.Reconstitute(
                reservation.Id,
                reservation.OrderReference,
                reservation.CompanyCode,
                reservation.RetailerCode,
                reservation.ProductCode,
                new Quantity(reservation.Units),
                reservation.Status));
        }

        return item;
    }

    /// <summary>Pure question, no mutation, no event (`R31`).</summary>
    public bool CanReserve(Quantity units) => units.Value <= AvailableUnits;

    /// <summary>Throws <see cref="InsufficientStockError"/> (`R30`) if it would break F1; otherwise creates one <c>reserved</c> reservation and adds <paramref name="units"/> to <see cref="ReservedUnits"/>. Emits nothing — the order-scoped fact is <see cref="OrderStockReservation"/>'s job.</summary>
    public Reservation Reserve(UniqueId reservationId, OrderNumber orderReference, string retailerCode, Quantity units)
    {
        if (!CanReserve(units))
        {
            throw new InsufficientStockError(ProductCode, units.Value, AvailableUnits);
        }

        var reservation = Reservation.Create(reservationId, orderReference, CompanyCode, retailerCode, ProductCode, units);
        _reservations.Add(reservation);
        ReservedUnits += units.Value;

        return reservation;
    }

    /// <summary>Moves this item's <c>reserved</c> reservations of <paramref name="orderReference"/> to <c>released</c> and subtracts their units, returning them — an EMPTY list when none was <c>reserved</c> (F5, idempotent). Throws <see cref="ReservationTerminalError"/> if any of the order's reservations on this item is <c>consumed</c> (F4, `FS10`) — checked BEFORE any mutation.</summary>
    public IReadOnlyList<Reservation> Release(OrderNumber orderReference)
    {
        var orderReservations = _reservations.Where(r => r.OrderReference == orderReference).ToList();

        if (orderReservations.Any(r => r.Status == ReservationStatus.Consumed))
        {
            throw new ReservationTerminalError(ReservationStatus.Consumed, "release");
        }

        var toRelease = orderReservations.Where(r => r.Status == ReservationStatus.Reserved).ToList();

        foreach (var reservation in toRelease)
        {
            reservation.Release();
        }

        ReservedUnits -= toRelease.Sum(r => r.Units.Value);

        return toRelease;
    }

    /// <summary>Moves this item's <c>reserved</c> reservations of <paramref name="orderReference"/> to <c>consumed</c> and subtracts their total from BOTH <see cref="Units"/> and <see cref="ReservedUnits"/> (domain-model.md §4.2 row 4), returning them. Emits nothing — <c>order.despatched.v1</c> is feature 18's fact (`FS11`). Ships ready and uncalled in this feature.</summary>
    public IReadOnlyList<Reservation> Consume(OrderNumber orderReference)
    {
        var toConsume = _reservations.Where(r => r.OrderReference == orderReference && r.Status == ReservationStatus.Reserved).ToList();

        foreach (var reservation in toConsume)
        {
            reservation.Consume();
        }

        var total = toConsume.Sum(r => r.Units.Value);
        Units -= total;
        ReservedUnits -= total;

        return toConsume;
    }

    /// <summary>Adds to <see cref="Units"/> and appends NO domain event (`R61`). Refuses (<see cref="StockUnitOverflowError"/>, `FS20`) rather than wrap the counter, changing nothing.</summary>
    public void Replenish(Quantity quantity)
    {
        if (quantity.Value > int.MaxValue - Units)
        {
            throw new StockUnitOverflowError(ProductCode, "replenish");
        }

        Units += quantity.Value;
    }

    /// <summary>The only way a fact reaches the aggregate. Refuses (<see cref="FactAggregateMismatchError"/>) unless the fact's <c>AggregateId</c> equals this item's own — the one guard that stops this method being a generic "emit anything" hole.</summary>
    public void RecordOrderFact(StockDomainEvent fact)
    {
        if (fact.AggregateId != Id)
        {
            throw new FactAggregateMismatchError(Id, fact.AggregateId);
        }

        Raise(fact);
    }

    public StockItemSnapshot ToSnapshot() => new(
        Id,
        CompanyCode,
        ProductCode,
        Units,
        ReservedUnits,
        LowStockThreshold,
        [.. _reservations.Select(r => new ReservationSnapshot(r.Id, r.OrderReference, r.CompanyCode, r.RetailerCode, r.ProductCode, r.Units.Value, r.Status))]);

    private static ReservationView ToView(Reservation reservation) => new(
        reservation.Id,
        reservation.OrderReference,
        reservation.CompanyCode,
        reservation.RetailerCode,
        reservation.ProductCode,
        reservation.Units.Value,
        reservation.Status);
}
