using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §8.2, tasks.md A4f — readiness aggregation over FAKED checks,
/// no HTTP, no real dependency. <c>live()</c> is always <c>200 up</c>
/// regardless of every check's outcome; <c>ready()</c> is <c>200</c> when
/// all are up and <c>503</c>, naming the failing check, when any single one
/// is down — driven once per check POSITION (tasks.md A4f's own
/// <c>⚑ARM — count</c>), not only the first, since an aggregation loop that
/// short-circuits on the first failure would still pass a first-position-only
/// probe.
/// </summary>
public sealed class HealthCheckAggregationTests
{
    private sealed class FakeHealthCheck(string name, bool isUp) : IHealthCheck
    {
        public string Name { get; } = name;

        public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(isUp ? HealthCheckResult.Up() : HealthCheckResult.Down($"{Name} is down"));
    }

    [Fact]
    public void Live_Always200Up_IndependentOfEveryCheck()
    {
        var body = HealthCheckAggregator.Live();

        Assert.Equal("up", body.Status);
        Assert.Null(body.Checks);
    }

    [Fact]
    public async Task Ready_200_WhenAllChecksAreUp()
    {
        var checks = new IHealthCheck[]
        {
            new FakeHealthCheck("writeModel", isUp: true),
            new FakeHealthCheck("factStream", isUp: true),
            new FakeHealthCheck("rpcTransport", isUp: true),
        };

        var (statusCode, body) = await HealthCheckAggregator.ReadyAsync(checks, CancellationToken.None);

        Assert.Equal(200, statusCode);
        Assert.Equal("up", body.Status);
        Assert.All(body.Checks!.Values, c => Assert.Equal("up", c.Status));
    }

    /// <summary>
    /// ⚑ARM — count (tasks.md A4f): the SAME three-check set, with the DOWN
    /// check driven once per POSITION (index 0, 1 and 2) rather than only
    /// the first — an aggregation loop that returns as soon as it sees the
    /// first check (rather than running every one, as design.md §8.2
    /// requires so the body can name ALL failing checks) would still pass
    /// a first-position-only probe.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Ready_503_NamingOnlyTheFailingCheck_WhenAnySingleOneIsDown(int downPosition)
    {
        var names = new[] { "writeModel", "factStream", "rpcTransport" };
        var checks = names
            .Select((name, index) => (IHealthCheck)new FakeHealthCheck(name, isUp: index != downPosition))
            .ToArray();

        var (statusCode, body) = await HealthCheckAggregator.ReadyAsync(checks, CancellationToken.None);

        Assert.Equal(503, statusCode);
        Assert.Equal("down", body.Status);

        var downName = names[downPosition];
        foreach (var (name, result) in body.Checks!)
        {
            Assert.Equal(name == downName ? "down" : "up", result.Status);
        }

        Assert.NotNull(body.Checks[downName].Detail);
    }
}
