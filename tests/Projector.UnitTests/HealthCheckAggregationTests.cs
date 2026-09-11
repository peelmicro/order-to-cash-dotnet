using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>design.md §8.2, tasks.md A4f — readiness aggregation over FAKED checks, no HTTP, no real dependency.</summary>
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
            new FakeHealthCheck("factStream", isUp: true),
            new FakeHealthCheck("rpcTransport", isUp: true),
            new FakeHealthCheck("readModel", isUp: true),
        };

        var (statusCode, body) = await HealthCheckAggregator.ReadyAsync(checks, CancellationToken.None);

        Assert.Equal(200, statusCode);
        Assert.Equal("up", body.Status);
        Assert.All(body.Checks!.Values, c => Assert.Equal("up", c.Status));
    }

    /// <summary>⚑ARM — count (tasks.md A4f): the DOWN check driven once per POSITION, not only the first.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Ready_503_NamingOnlyTheFailingCheck_WhenAnySingleOneIsDown(int downPosition)
    {
        var names = new[] { "factStream", "rpcTransport", "readModel" };
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
