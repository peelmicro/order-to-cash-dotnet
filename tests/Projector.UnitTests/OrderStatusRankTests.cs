using System.Reflection;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Projector.Domain;
using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

public sealed class OrderStatusRankTests
{
    private static readonly (Type PayloadType, string? Status, int Rank)[] _table =
    [
        (typeof(OrderPlacedPayload), "placed", 1),
        (typeof(StockReservedPayload), "stock_reserved", 2),
        (typeof(CreditApprovedPayload), "credit_approved", 3),
        (typeof(OrderConfirmedPayload), "confirmed", 4),
        (typeof(OrderDespatchedPayload), "despatched", 5),
        (typeof(InvoiceIssuedPayload), "invoiced", 6),
        (typeof(PaymentReceivedPayload), "paid", 7),
        (typeof(OrderCompletedPayload), "completed", 98),
        (typeof(OrderCancelledPayload), "cancelled", 99),
        (typeof(StockRejectedPayload), null, 0),
        (typeof(StockReleasedPayload), null, 0),
        (typeof(CreditRejectedPayload), null, 0),
        (typeof(CreditReleasedPayload), null, 0),
        (typeof(OrderSagaFailedPayload), null, 0),
    ];

    [Fact]
    public void PR12_MapsEachOfTheFourteenFactsToTheImpliedStatusAndRankOfTheTable_WithTheFiveStatusLessFactsImplyingNone()
    {
        Assert.Equal(14, _table.Length);
        Assert.Equal(14, FactCatalog.PayloadTypesByEventType.Count);
        Assert.Equal(14, OrderStatusRank.PayloadTypes.Count);

        foreach (var (payloadType, expectedStatus, expectedRank) in _table)
        {
            Assert.Equal(expectedStatus, OrderStatusRank.ImpliedStatusOf(payloadType));
            Assert.Equal(expectedRank, OrderStatusRank.RankOf(payloadType));
        }

        Assert.Equal(5, _table.Count(row => row.Status is null));
        Assert.Equal(9, _table.Count(row => row.Status is not null));
    }

    [Fact]
    public void PR40_TheNineStatusRankPairsEqualMongoSeedWritersOwnTable()
    {
        var field = typeof(MongoSeedWriter).GetField("_statusRank", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MongoSeedWriter._statusRank not found by reflection.");
        var seedTable = (IReadOnlyDictionary<string, int>)field.GetValue(null)!;

        Assert.Equal(9, seedTable.Count);

        foreach (var (payloadType, status, rank) in _table.Where(row => row.Status is not null))
        {
            Assert.True(seedTable.TryGetValue(status!, out var seedRank), $"seed table has no rank for status '{status}'");
            Assert.Equal(seedRank, rank);
            Assert.Equal(seedRank, OrderStatusRank.RankOf(payloadType));
        }
    }
}
