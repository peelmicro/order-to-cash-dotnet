using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`R36` (consume, one despatch, one fact; F6/F7/F8's creation half) against the pure <see cref="OrderDespatch"/> service.</summary>
public sealed class OrderDespatchTests
{
    [Fact]
    public void Create_ConsumesEveryReservedReservationOfTheOrderAcrossTwoItems_MovesThemToConsumed_AndCreatesOneDespatchAdviceWithOneFact()
    {
        var orderReference = new OrderNumber(1);
        var reservationA = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Reserved);
        var itemA = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 3, reservationA);
        var reservationB = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P2", 5, ReservationStatus.Reserved);
        var itemB = ReservationTests.BuildItem("ACME", "P2", units: 10, reservedUnits: 5, reservationB);

        var input = new DespatchOrderInput(orderReference, UniqueId.New());
        var outcome = OrderDespatch.Create([itemA, itemB], input, "DES-000001", ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(DespatchOutcomeKind.Created, outcome.Kind);

        // Consumed — reservations move to `consumed`, and BOTH counters drop (StockItem.Consume).
        Assert.Equal(ReservationStatus.Consumed, Assert.Single(itemA.Reservations).Status);
        Assert.Equal(ReservationStatus.Consumed, Assert.Single(itemB.Reservations).Status);
        Assert.Equal(7, itemA.Units);
        Assert.Equal(0, itemA.ReservedUnits);
        Assert.Equal(5, itemB.Units);
        Assert.Equal(0, itemB.ReservedUnits);

        // Exactly one despatch advice, one line per consumed reservation, one fact.
        var advice = outcome.Advice!;
        Assert.Equal("DES-000001", advice.DespatchReference);
        Assert.Equal(orderReference, advice.OrderReference);
        Assert.Collection(
            advice.Lines,
            l => { Assert.Equal("P1", l.ProductCode); Assert.Equal(3, l.Units.Value); },
            l => { Assert.Equal("P2", l.ProductCode); Assert.Equal(5, l.Units.Value); });
        Assert.Single(advice.DomainEvents);

        // StockItem.Consume itself emits nothing — order.despatched.v1 lives only on the DespatchAdvice.
        Assert.Empty(itemA.DomainEvents);
        Assert.Empty(itemB.DomainEvents);
    }

    [Fact]
    public void Create_TheFactsCompanyAndRetailerCode_ComeFromTheSameConsumedReservation()
    {
        // Prevention of #7's review finding N2 (asymmetric sourcing):
        // both fields must come from the SAME reservation, not
        // companyCode from the item and retailerCode from elsewhere.
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER-X", "P1", 2, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 2, reservation);

        var input = new DespatchOrderInput(orderReference, UniqueId.New());
        var outcome = OrderDespatch.Create([item], input, "DES-000001", ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal("ACME", outcome.Advice!.CompanyCode);
        Assert.Equal("RETAILER-X", outcome.Advice.RetailerCode);
    }

    [Fact]
    public void Create_DefensiveNoReservationsBranch_WhenNoItemHoldsAReservedReservationOfTheOrder()
    {
        var orderReference = new OrderNumber(1);
        var released = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Released);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0, released);

        var input = new DespatchOrderInput(orderReference, UniqueId.New());
        var outcome = OrderDespatch.Create([item], input, "DES-000001", ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(DespatchOutcomeKind.NoReservations, outcome.Kind);
        Assert.Null(outcome.Advice);
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public void Create_ANonReservedItemAmongstReservedOnes_OnlyReservedReservationsAreConsumedAndTraced()
    {
        var orderReference = new OrderNumber(1);
        var reservedLine = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 2, ReservationStatus.Reserved);
        var itemReserved = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 2, reservedLine);
        var alreadyReleasedLine = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P2", 4, ReservationStatus.Released);
        var itemReleased = ReservationTests.BuildItem("ACME", "P2", units: 10, reservedUnits: 0, alreadyReleasedLine);

        var input = new DespatchOrderInput(orderReference, UniqueId.New());
        var outcome = OrderDespatch.Create([itemReserved, itemReleased], input, "DES-000001", ReservationTests.SampleContext(), UniqueId.New);

        Assert.Equal(DespatchOutcomeKind.Created, outcome.Kind);
        var line = Assert.Single(outcome.Advice!.Lines);
        Assert.Equal("P1", line.ProductCode);
        Assert.Equal(ReservationStatus.Released, Assert.Single(itemReleased.Reservations).Status); // untouched
    }

    /// <summary>The fact's <c>EventId</c> and the advice's own id ARE the delegate's returned values, never independently minted — the same "no ids beyond those <c>newId</c> supplies" discipline `OrderStockReservation` keeps. Two KNOWN ids are queued and dequeued in the order <see cref="OrderDespatch.Create"/> calls <c>newId</c> (advice id first, fact <c>EventId</c> second); the assertions pin each returned value to the SPECIFIC queued id, not merely to "two distinct GUIDs".</summary>
    [Fact]
    public void Create_TheAdvicesIdAndTheFactsEventId_AreBothMintedByTheNewIdDelegate()
    {
        var orderReference = new OrderNumber(1);
        var reservation = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 2, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 2, reservation);

        var expectedAdviceId = UniqueId.New();
        var expectedFactEventId = UniqueId.New();
        var minted = new Queue<UniqueId>([expectedAdviceId, expectedFactEventId]);
        var input = new DespatchOrderInput(orderReference, UniqueId.New());

        var outcome = OrderDespatch.Create([item], input, "DES-000001", ReservationTests.SampleContext(), () => minted.Dequeue());

        var advice = outcome.Advice!;
        var fact = Assert.Single(advice.DomainEvents);
        Assert.Equal(expectedAdviceId, advice.Id);
        Assert.Equal(expectedFactEventId, ((OrderDespatched)fact).EventId);
    }
}
