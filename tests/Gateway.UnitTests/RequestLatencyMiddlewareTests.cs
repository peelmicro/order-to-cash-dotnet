using Microsoft.AspNetCore.Http;
using OrderToCash.Gateway.Presentation;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// OR5/design.md §7 — <c>otc_request_latency_ms</c>, tagged by endpoint,
/// measured on the ERROR path too, not only on <c>200</c> — ported cases
/// 61-62. No host, no Kestrel: <see cref="DefaultHttpContext"/> directly.
/// </summary>
public sealed class RequestLatencyMiddlewareTests
{
    [Fact]
    public async Task RecordsOnSuccess_TaggedByTheRequestPath()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders";
        var middleware = new RequestLatencyMiddleware(_ => Task.CompletedTask);

        using var capture = MetricCapture.ForInstrument("otc_request_latency_ms");
        await middleware.InvokeAsync(context);

        var measurement = Assert.Single(capture.Measurements);
        Assert.True(measurement.Value >= 0);
        Assert.Equal("/orders", measurement.Tags.Single(t => t.Key == "endpoint").Value?.ToString());
    }

    /// <summary>Ported case 62 — the duration is measured EVEN when the downstream pipeline throws, in a <c>finally</c>, before the exception is rethrown to whatever wraps this middleware (<c>ProblemJsonMiddleware</c>, outside it).</summary>
    [Fact]
    public async Task RecordsOnTheErrorPathToo_BeforeRethrowing()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/orders";
        var middleware = new RequestLatencyMiddleware(_ => throw new InvalidOperationException("boom"));

        using var capture = MetricCapture.ForInstrument("otc_request_latency_ms");
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var measurement = Assert.Single(capture.Measurements);
        Assert.True(measurement.Value >= 0);
    }
}
