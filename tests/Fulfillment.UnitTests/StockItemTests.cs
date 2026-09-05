using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// `R30`, `R61`, `FS10`, `FS11`, `FS12` (unit half), `FS20` (two cases) and
/// <c>RecordOrderFact</c>'s aggregate guard — all against the pure
/// <see cref="StockItem"/> aggregate, no repository, no dispatcher.
/// </summary>
public sealed class StockItemTests
{
    [Fact]
    public void R30_RejectsInFullAnyOperationThatWouldPushReservedUnitsAboveUnitsAndChangesNoStockItem()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 5, reservedUnits: 0);

        var error = Assert.Throws<InsufficientStockError>(() => item.Reserve(UniqueId.New(), new OrderNumber(1), "RETAILER1", new Quantity(6)));

        Assert.Equal("P1", error.ProductCode);
        Assert.Equal(6, error.Requested);
        Assert.Equal(5, error.Available);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Empty(item.Reservations);
    }

    [Fact]
    public void R61_IncreasesUnitsByTheRequestedQuantity_LeavesReservedUnitsAndEveryReservationUnchanged_AndAppendsNoDomainEvent()
    {
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 4, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 4, reservation);

        item.Replenish(new Quantity(6));

        Assert.Equal(16, item.Units);
        Assert.Equal(4, item.ReservedUnits);
        var view = Assert.Single(item.Reservations);
        Assert.Equal(ReservationStatus.Reserved, view.Status);
        Assert.Equal(4, view.Units);
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void FS10_RefusesToReleaseAConsumedReservationAndChangesNothing()
    {
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 4, ReservationStatus.Consumed);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0, reservation);

        var error = Assert.Throws<ReservationTerminalError>(() => item.Release(orderReference));

        Assert.Equal(ReservationStatus.Consumed, error.From);
        Assert.Equal(10, item.Units);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Equal(ReservationStatus.Consumed, Assert.Single(item.Reservations).Status);
    }

    [Fact]
    public void FS11_ConsumeMovesTheOrdersReservationsToConsumed_DecreasesUnitsAndReservedUnitsByTheSameTotal_AndAppendsNoDomainEvent()
    {
        var orderReference = new OrderNumber(1);
        var reservationA = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Reserved);
        var reservationB = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 2, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 5, reservationA, reservationB);

        var consumed = item.Consume(orderReference);

        Assert.Equal(2, consumed.Count);
        Assert.All(item.Reservations, view => Assert.Equal(ReservationStatus.Consumed, view.Status));
        Assert.Equal(5, item.Units); // 10 - (3 + 2)
        Assert.Equal(0, item.ReservedUnits); // 5 - (3 + 2)
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void FS12_ReconstitutesFromASnapshotAndKeepsReservedUnitsEqualToTheSumOfReservedReservations_AfterReserveReleaseAndConsume()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        Assert.Equal(SumReserved(item), item.ReservedUnits);

        var orderA = new OrderNumber(1);
        item.Reserve(UniqueId.New(), orderA, "RETAILER1", new Quantity(3));
        Assert.Equal(3, item.ReservedUnits);
        Assert.Equal(SumReserved(item), item.ReservedUnits);

        item.Release(orderA);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Equal(SumReserved(item), item.ReservedUnits);

        var orderB = new OrderNumber(2);
        item.Reserve(UniqueId.New(), orderB, "RETAILER1", new Quantity(4));
        Assert.Equal(4, item.ReservedUnits);
        Assert.Equal(SumReserved(item), item.ReservedUnits);

        item.Consume(orderB);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Equal(6, item.Units); // 10 - 4
        Assert.Equal(SumReserved(item), item.ReservedUnits);
    }

    [Fact]
    public void FS20_RefusesAReplenishmentThatWouldOverflowTheUnitCounter_AndChangesNothing()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: int.MaxValue - 5, reservedUnits: 0);

        var error = Assert.Throws<StockUnitOverflowError>(() => item.Replenish(new Quantity(10)));

        Assert.Equal("P1", error.ProductCode);
        Assert.Equal(int.MaxValue - 5, item.Units);
    }

    [Fact]
    public void FS20_RefusesAReserveWhoseSummedLineUnitsWouldOverflowTheUnitCounter_AndChangesNothing()
    {
        // Two lines naming the SAME product whose Quantity values, summed as
        // a plain `int`, wrap to a small/negative number — which would
        // wrongly look "available" and let this obviously-oversized request
        // through, corrupting ReservedUnits. Summed as `long` (design.md
        // §3.1) the total is correctly seen as exceeding AvailableUnits, so
        // the order-scoped service rejects it as a business outcome instead.
        var item = ReservationTests.BuildItem("ACME", "P1", units: int.MaxValue, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(int.MaxValue - 1)), new ReserveOrderLine("P1", new Quantity(3))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Empty(item.Reservations);

        var fact = Assert.IsType<StockRejected>(Assert.Single(item.DomainEvents));
        var shortage = Assert.Single(fact.Shortages);
        Assert.Equal(int.MaxValue, shortage.Available);

        // Review advisory A4: `Requested` is summed as `long` internally but
        // the AsyncAPI field is `int`, so a total above `int.MaxValue` is
        // clamped rather than truncated — deliberate, and defensible because
        // the order is rejected either way, but previously unasserted.
        // (int.MaxValue - 1) + 3 overflows int.MaxValue, so this documents
        // the clamp rather than a silent wraparound.
        Assert.Equal(int.MaxValue, shortage.Requested);
    }

    [Fact]
    public void RecordOrderFact_RefusesAFactWhoseAggregateIdIsAForeignId()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var foreignId = UniqueId.New();

        var fact = new StockReserved(
            UniqueId.New(),
            foreignId,
            UniqueId.New(),
            UniqueId.New(),
            DateTimeOffset.UtcNow,
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            []);

        var error = Assert.Throws<FactAggregateMismatchError>(() => item.RecordOrderFact(fact));

        Assert.Equal(item.Id, error.StockItemId);
        Assert.Equal(foreignId, error.FactAggregateId);
        Assert.Empty(item.DomainEvents);
    }

    private static int SumReserved(StockItem item) =>
        item.Reservations.Where(r => r.Status == ReservationStatus.Reserved).Sum(r => r.Units);
}
