using Microsoft.AspNetCore.Http;
using OrderToCash.Gateway.Infrastructure.Observability;
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

        // Backlog id 74 bullet 5 (advisory A13) — the meter is PROCESS-WIDE.
        // A concurrently running class in this assembly that drives the real
        // middleware records into this very capture, and the exactly-one
        // assertion below used to read the WHOLE capture. This interloper
        // emits one matching-shaped measurement from a foreign execution
        // context, deterministically, so that assertion is now ARMED: read
        // unscoped it sees two.
        MetricCapture.RecordFromAConcurrentWriter(
            () => OtcMetrics.RequestLatencyMs.Record(4242, new KeyValuePair<string, object?>("endpoint", "/orders")));

        await middleware.InvokeAsync(context);

        var measurement = capture.SingleOwnMeasurement();
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

        // Backlog id 74 bullet 5 — same interloper as the success case above.
        MetricCapture.RecordFromAConcurrentWriter(
            () => OtcMetrics.RequestLatencyMs.Record(4242, new KeyValuePair<string, object?>("endpoint", "/orders")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var measurement = capture.SingleOwnMeasurement();
        Assert.True(measurement.Value >= 0);
    }
}
