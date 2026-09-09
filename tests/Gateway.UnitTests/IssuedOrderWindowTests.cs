using OrderToCash.Gateway.Domain.Orders;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>R55's gateway-half distinction — 202 (projection pending) vs 404 (unknown) — ported from #7's review finding F3. Proves the recency window IssuedOrderWindow rather than the FakeClock's plumbing.</summary>
public sealed class IssuedOrderWindowTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void IsRecentlyIssued_IsFalse_ForAnIdThatWasNeverRecorded()
    {
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 10);

        Assert.False(window.IsRecentlyIssued(Guid.NewGuid()));
    }

    [Fact]
    public void IsRecentlyIssued_IsTrue_ImmediatelyAfterRecord()
    {
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 10);
        var orderId = Guid.NewGuid();

        window.Record(orderId);

        Assert.True(window.IsRecentlyIssued(orderId));
    }

    [Fact]
    public void IsRecentlyIssued_BecomesFalse_OnceTheTtlHasElapsed()
    {
        var clock = new FakeClock();
        var window = new IssuedOrderWindow(() => clock.Now, TimeSpan.FromMinutes(5), 10);
        var orderId = Guid.NewGuid();

        window.Record(orderId);
        clock.Now = clock.Now.AddMinutes(5).AddSeconds(1);

        Assert.False(window.IsRecentlyIssued(orderId));
    }

    [Fact]
    public void IsRecentlyIssued_StaysTrue_OneSecondBeforeTheTtlElapses()
    {
        var clock = new FakeClock();
        var window = new IssuedOrderWindow(() => clock.Now, TimeSpan.FromMinutes(5), 10);
        var orderId = Guid.NewGuid();

        window.Record(orderId);
        clock.Now = clock.Now.AddMinutes(5).AddSeconds(-1);

        Assert.True(window.IsRecentlyIssued(orderId));
    }

    [Fact]
    public void Record_EvictsTheOldestEntry_OnceCapacityIsExceeded()
    {
        var clock = new FakeClock();
        var window = new IssuedOrderWindow(() => clock.Now, TimeSpan.FromHours(1), capacity: 2);

        var first = Guid.NewGuid();
        window.Record(first);
        clock.Now = clock.Now.AddSeconds(1);

        var second = Guid.NewGuid();
        window.Record(second);
        clock.Now = clock.Now.AddSeconds(1);

        var third = Guid.NewGuid();
        window.Record(third);

        Assert.False(window.IsRecentlyIssued(first));
        Assert.True(window.IsRecentlyIssued(second));
        Assert.True(window.IsRecentlyIssued(third));
        Assert.Equal(2, window.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Rejects_ANonPositiveTtl(int ttlSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(ttlSeconds), 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Rejects_ANonPositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), capacity));
    }
}
