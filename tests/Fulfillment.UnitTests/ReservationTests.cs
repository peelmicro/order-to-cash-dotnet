using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// The matrix's <c>fulfillment/domain/reservation.spec</c> cases — `R32`,
/// `R33` (the order-scoped, all-or-nothing behaviour, filed here per
/// design.md §14's own mapping) and `R35` (the child entity's own state
/// machine).
/// </summary>
public sealed class ReservationTests
{
    [Fact]
    public void R32_CreatesOneReservationPerLineIncreasesReservedUnitsAndEmitsExactlyOneStockReservedV1()
    {
        var itemP1 = BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemP2 = BuildItem("ACME", "P2", units: 10, reservedUnits: 0);
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = itemP1,
            ["P2"] = itemP2,
        };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(3)), new ReserveOrderLine("P2", new Quantity(4))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Reserved, outcome.Kind);
        Assert.Equal(2, outcome.Reservations.Count);
        Assert.Equal(3, itemP1.ReservedUnits);
        Assert.Equal(4, itemP2.ReservedUnits);

        var allFacts = itemP1.DomainEvents.Concat(itemP2.DomainEvents).ToList();
        var fact = Assert.Single(allFacts);
        Assert.IsType<StockReserved>(fact);
    }

    [Fact]
    public void R33_CreatesNoReservationAtAllAndEmitsStockRejectedV1NamingRequestedAndAvailableUnitsWhenOneLineIsShort()
    {
        var itemP1 = BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemP2 = BuildItem("ACME", "P2", units: 2, reservedUnits: 0); // short: only 2 available
        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1"] = itemP1,
            ["P2"] = itemP2,
        };

        var input = new ReserveOrderInput(
            new OrderNumber(1),
            "ACME",
            "RETAILER1",
            [new ReserveOrderLine("P1", new Quantity(3)), new ReserveOrderLine("P2", new Quantity(4))],
            UniqueId.New());

        var outcome = OrderStockReservation.Reserve(itemsByProductCode, input, SampleContext(), UniqueId.New);

        Assert.Equal(ReserveOutcomeKind.Rejected, outcome.Kind);
        Assert.Empty(outcome.Reservations);
        Assert.Equal(0, itemP1.ReservedUnits);
        Assert.Equal(0, itemP2.ReservedUnits);
        Assert.Empty(itemP1.Reservations);
        Assert.Empty(itemP2.Reservations);

        var allFacts = itemP1.DomainEvents.Concat(itemP2.DomainEvents).ToList();
        var fact = Assert.Single(allFacts);
        var rejected = Assert.IsType<StockRejected>(fact);
        var shortage = Assert.Single(rejected.Shortages);
        Assert.Equal("P2", shortage.ProductCode);
        Assert.Equal(4, shortage.Requested);
        Assert.Equal(2, shortage.Available);
        Assert.Equal("insufficient_stock", rejected.Reason);
    }

    [Fact]
    public void R35_RefusesEveryTransitionOutOfReleasedAndOutOfConsumedAndChangesNothing()
    {
        var releasedReservation = Reservation.Create(UniqueId.New(), new OrderNumber(1), "ACME", "RETAILER1", "P1", new Quantity(5));
        releasedReservation.Release();

        var releaseError = Assert.Throws<ReservationTerminalError>(releasedReservation.Release);
        Assert.Equal(ReservationStatus.Released, releasedReservation.Status);
        Assert.Equal(ReservationStatus.Released, releaseError.From);

        var consumeErrorFromReleased = Assert.Throws<ReservationTerminalError>(releasedReservation.Consume);
        Assert.Equal(ReservationStatus.Released, releasedReservation.Status);
        Assert.Equal(ReservationStatus.Released, consumeErrorFromReleased.From);

        var consumedReservation = Reservation.Create(UniqueId.New(), new OrderNumber(2), "ACME", "RETAILER1", "P1", new Quantity(5));
        consumedReservation.Consume();

        var releaseErrorFromConsumed = Assert.Throws<ReservationTerminalError>(consumedReservation.Release);
        Assert.Equal(ReservationStatus.Consumed, consumedReservation.Status);
        Assert.Equal(ReservationStatus.Consumed, releaseErrorFromConsumed.From);

        var consumeErrorFromConsumed = Assert.Throws<ReservationTerminalError>(consumedReservation.Consume);
        Assert.Equal(ReservationStatus.Consumed, consumedReservation.Status);
        Assert.Equal(ReservationStatus.Consumed, consumeErrorFromConsumed.From);
    }

    internal static StockItem BuildItem(string companyCode, string productCode, int units, int reservedUnits, params ReservationSnapshot[] reservations) =>
        StockItem.Reconstitute(new StockItemSnapshot(UniqueId.New(), companyCode, productCode, units, reservedUnits, LowStockThreshold: 0, [.. reservations]));

    internal static StockContext SampleContext() => new(DateTimeOffset.UtcNow, UniqueId.New());
}
