using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// `R34` (domain half), `F5`, `FS8`, `FS13`, F3 across three items, and a
/// repeated product summed across two lines — all against the pure
/// <see cref="OrderStockReservation"/> service.
/// </summary>
public sealed class OrderStockReservationTests
{
    [Fact]
    public void R34_ReleasesTheReservationsDecreasesReservedUnitsAndEmitsExactlyOneStockReleasedV1()
    {
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 4, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 4, reservation);

        var input = new ReleaseOrderInput(orderReference, "order_cancelled", UniqueId.New());
        var outcome = OrderStockReservation.Release([item], input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReleaseOutcomeKind.Released, outcome.Kind);
        Assert.Single(outcome.Released);
        Assert.Equal(0, item.ReservedUnits);

        var fact = Assert.IsType<StockReleased>(Assert.Single(item.DomainEvents));
        Assert.Equal("order_cancelled", fact.Reason);
        Assert.Single(fact.Released);
    }

    /// <summary>
    /// Backlog 49: <c>Release</c> used to mint its fact's <c>EventId</c> with
    /// <c>UniqueId.New()</c> directly rather than the <c>newId</c> delegate
    /// <c>Reserve</c> already took — the one id "no ids beyond those
    /// <c>newId</c> supplies" (design.md §3.3) did not actually keep. A
    /// deterministic fake proves the fact's <c>EventId</c> IS the delegate's
    /// return value, not an id <c>Release</c> minted on its own.
    /// </summary>
    [Fact]
    public void Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned()
    {
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 4, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 4, reservation);
        var expectedEventId = UniqueId.New();

        var input = new ReleaseOrderInput(orderReference, "order_cancelled", UniqueId.New());
        var outcome = OrderStockReservation.Release([item], input, ReservationTests.SampleContext(), () => expectedEventId);

        Assert.Equal(expectedEventId, outcome.Fact!.EventId);
    }

    /// <summary>
    /// A1 (review round 1, `fulfillment_despatch`): the same "no ids beyond
    /// those <c>newId</c> supplies" property backlog 49 pinned for <c>Release</c>
    /// was unguarded for <c>Reserve</c>'s two facts — <c>StockReserved</c>
    /// and <c>StockRejected</c> could both be minted with <c>UniqueId.New()</c>
    /// directly on a green suite. <c>Reserve</c> calls <c>newId()</c> once per
    /// reservation line before calling it once more for the fact's own
    /// <c>EventId</c>, so the queue's LAST value is the one that must land on
    /// <see cref="StockReserved.EventId"/>.
    /// </summary>
    [Fact]
    public void Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(2))],
            UniqueId.New());

        var reservationLineId = UniqueId.New();
        var expectedFactEventId = UniqueId.New();
        var minted = new Queue<UniqueId>([reservationLineId, expectedFactEventId]);

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), () => minted.Dequeue());

        Assert.Equal(ReserveOutcomeKind.Reserved, outcome.Kind);
        Assert.Equal(expectedFactEventId, outcome.ReservedFact!.EventId);
    }

    /// <summary>A1's sibling for the rejection path — see <see cref="Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast"/>. `Rejected` calls `newId()` exactly once (the shortage check consumes no id), so a single known value pins the whole call.</summary>
    [Fact]
    public void Reserve_TheRejectedFactsEventId_IsTheOneTheNewIdDelegateReturned()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = itemP1 };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(2)), new ReserveOrderLine("UNKNOWN", new Quantity(1))],
            UniqueId.New());
        var expectedEventId = UniqueId.New();

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), () => expectedEventId);

        Assert.Equal(ReserveOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(expectedEventId, outcome.RejectedFact!.EventId);
    }

    [Fact]
    public void F5_ReleaseOfAnOrderWithNoReservedReservationIsASuccessNoOpThatEmitsNothing()
    {
        var orderReference = new OrderNumber(1);
        var already = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 4, ReservationStatus.Released);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0, already);

        var input = new ReleaseOrderInput(orderReference, "order_cancelled", UniqueId.New());
        var outcome = OrderStockReservation.Release([item], input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReleaseOutcomeKind.AlreadyReleased, outcome.Kind);
        Assert.Empty(outcome.Released);
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void FS8_RejectsTheWholeOrderWithReasonUnknownProductAndAvailableZero_WhenAnyLineNamesAProductTheCompanyDoesNotStock()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = itemP1 };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(2)), new ReserveOrderLine("UNKNOWN", new Quantity(1))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(0, itemP1.ReservedUnits);
        Assert.Empty(itemP1.Reservations);

        var fact = Assert.IsType<StockRejected>(Assert.Single(itemP1.DomainEvents));
        Assert.Equal("unknown_product", fact.Reason);
        var shortage = Assert.Single(fact.Shortages);
        Assert.Equal("UNKNOWN", shortage.ProductCode);
        Assert.Equal(0, shortage.Available);
    }

    [Fact]
    public void FS13_StampsAggregateIdWithTheFirstKnownLinesStockItemOnReservedAndRejected_AndTheFirstReleasedReservationsItemOnReleased()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemP2 = ReservationTests.BuildItem("ACME", "P2", units: 10, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = itemP1, ["P2"] = itemP2 };

        var reserveInput = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(1)), new ReserveOrderLine("P2", new Quantity(1))],
            UniqueId.New());
        var reserveOutcome = OrderStockReservation.Reserve(itemsByProductCode, reserveInput, ReservationTests.SampleContext(), UniqueId.New);
        Assert.Equal(itemP1.Id, reserveOutcome.ReservedFact!.AggregateId);

        // Rejected: P2 (unknown) is listed first on the request, so the
        // carrier is the first line that RESOLVES — P1's own second attempt.
        var itemP3 = ReservationTests.BuildItem("ACME", "P3", units: 10, reservedUnits: 0);
        var itemsByProductCode2 = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P3"] = itemP3 };
        var rejectInput = new ReserveOrderInput(
            new OrderNumber(2),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("UNKNOWN", new Quantity(1)), new ReserveOrderLine("P3", new Quantity(1))],
            UniqueId.New());
        var rejectOutcome = OrderStockReservation.Reserve(itemsByProductCode2, rejectInput, ReservationTests.SampleContext(), UniqueId.New);
        Assert.Equal(itemP3.Id, rejectOutcome.RejectedFact!.AggregateId);

        // Released: the first item whose Release() call actually released something.
        var orderReference = new OrderNumber(3);
        var neverReserved = ReservationTests.BuildItem("ACME", "P4", units: 10, reservedUnits: 0);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P5", 2, ReservationStatus.Reserved);
        var releasedFrom = ReservationTests.BuildItem("ACME", "P5", units: 10, reservedUnits: 2, reservation);
        var releaseOutcome = OrderStockReservation.Release([neverReserved, releasedFrom], new ReleaseOrderInput(orderReference, "order_cancelled", UniqueId.New()), ReservationTests.SampleContext(), UniqueId.New);
        Assert.Equal(releasedFrom.Id, releaseOutcome.Fact!.AggregateId);
    }

    [Fact]
    public void AThreeItemOrderWhoseThirdLineIsShort_ReservesNothingAndNamesOnlyTheShortLine()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemP2 = ReservationTests.BuildItem("ACME", "P2", units: 10, reservedUnits: 0);
        var itemP3 = ReservationTests.BuildItem("ACME", "P3", units: 1, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = itemP1,
            ["P2"] = itemP2,
            ["P3"] = itemP3,
        };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(2)), new ReserveOrderLine("P2", new Quantity(2)), new ReserveOrderLine("P3", new Quantity(5))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(0, itemP1.ReservedUnits);
        Assert.Equal(0, itemP2.ReservedUnits);
        Assert.Equal(0, itemP3.ReservedUnits);
        Assert.Empty(itemP1.Reservations);
        Assert.Empty(itemP2.Reservations);
        Assert.Empty(itemP3.Reservations);

        var allFacts = itemP1.DomainEvents.Concat(itemP2.DomainEvents).Concat(itemP3.DomainEvents).ToList();
        var fact = Assert.IsType<StockRejected>(Assert.Single(allFacts));
        var shortage = Assert.Single(fact.Shortages);
        Assert.Equal("P3", shortage.ProductCode);
        Assert.Equal(5, shortage.Requested);
        Assert.Equal(1, shortage.Available);
    }

    [Fact]
    public void ARepeatedProductOnTwoLinesSumsForAvailabilityButYieldsTwoReservations()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 5, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(2)), new ReserveOrderLine("P1", new Quantity(3))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Reserved, outcome.Kind);
        Assert.Equal(2, outcome.Reservations.Count);
        Assert.Equal(5, item.ReservedUnits);
        Assert.Equal(2, item.Reservations.Count);
    }
}
