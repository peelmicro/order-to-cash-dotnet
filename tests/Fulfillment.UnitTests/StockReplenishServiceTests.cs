using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`FS14` — all-or-nothing: an unknown product on any line raises before anything is mutated.</summary>
public sealed class StockReplenishServiceTests
{
    [Fact]
    public async Task ReplenishAsync_AnUnknownProductOnAnyLine_RaisesBeforeAnythingIsMutated()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var repository = new FakeStockItemRepository
        {
            // P2 is absent — LockItemsAsync's own contract: unknown product
            // codes are simply absent from the returned dictionary.
            LockItemsResult = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = itemP1 },
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReplenishService(unitOfWork, repository);

        var command = new ReplenishStockCommand("ACME", [new StockReplenishRequestLine("P1", 5), new StockReplenishRequestLine("P2", 3)]);

        await Assert.ThrowsAsync<UnknownStockItemError>(() => service.ReplenishAsync(command, CancellationToken.None));

        Assert.Equal(10, itemP1.Units); // P1's own line never applied either — all-or-nothing.
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task ReplenishAsync_EveryLineKnown_AppliesAllLinesAndRepliesTheAffectedItems()
    {
        var itemP1 = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var itemP2 = ReservationTests.BuildItem("ACME", "P2", units: 20, reservedUnits: 0);
        var repository = new FakeStockItemRepository
        {
            LockItemsResult = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = itemP1, ["P2"] = itemP2 },
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReplenishService(unitOfWork, repository);

        var command = new ReplenishStockCommand("ACME", [new StockReplenishRequestLine("P1", 5), new StockReplenishRequestLine("P2", 3)]);
        var reply = await service.ReplenishAsync(command, CancellationToken.None);

        Assert.Equal(15, itemP1.Units);
        Assert.Equal(23, itemP2.Units);
        Assert.Equal(2, reply.Items.Count);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
