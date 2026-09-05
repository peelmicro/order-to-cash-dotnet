using OrderToCash.Fulfillment.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`FS19`'s ordering half, ledger L2/L3.</summary>
public sealed class StockLockOrderTests
{
    [Fact]
    public void FS19_OrdersDistinctProductCodesByInvariantUppercaseOrdinal_IndependentlyOfRequestOrderAndOfLetterCase()
    {
        var requestOrderA = new[] { "p3", "P1", "p2" };
        var requestOrderB = new[] { "P2", "p3", "p1" };
        var requestOrderC = new[] { "P1", "P2", "P3" }; // already-uppercase spelling

        var fixedA = StockLockOrder.Fix(requestOrderA);
        var fixedB = StockLockOrder.Fix(requestOrderB);
        var fixedC = StockLockOrder.Fix(requestOrderC);

        Assert.Equal(requestOrderA.OrderBy(c => c.ToUpperInvariant(), StringComparer.Ordinal), fixedA);
        Assert.Equal(fixedA.Select(c => c.ToUpperInvariant()), fixedB.Select(c => c.ToUpperInvariant()));
        Assert.Equal(fixedA.Select(c => c.ToUpperInvariant()), fixedC.Select(c => c.ToUpperInvariant()));
        Assert.Equal(["P1", "P2", "P3"], fixedC);
    }

    [Fact]
    public void FS19_DeduplicatesCaseInsensitively()
    {
        var codes = new[] { "p1", "P1", "P2" };

        var fixedOrder = StockLockOrder.Fix(codes);

        Assert.Equal(2, fixedOrder.Count);
    }
}
