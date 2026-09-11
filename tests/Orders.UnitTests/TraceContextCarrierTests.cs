using System.Diagnostics;
using NATS.Client.Core;
using OrderToCash.Orders.Infrastructure.Observability;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OR4/design.md §5.2, §5.3, §5.5 — the W3C trace-context carrier helpers,
/// ported from #7's <c>trace-context.spec</c> (design.md §11 cases 21-28):
/// inject/extract on the NATS carrier, extract from headers with no
/// context, extract from null headers, <c>ActiveTraceParent()</c> with and
/// without a span, <c>ContextFromTraceParent(null)</c>, a restored-context
/// child keeps the trace id and mints a fresh span id. Every assertion here
/// compares the REAL trace id back out of the header — never merely
/// "a header is present" (§5.5, ledger L24).
/// </summary>
public sealed class TraceContextCarrierTests : IDisposable
{
    private readonly ActivityListener _listener;

    public TraceContextCarrierTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OtcActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void ActiveTraceParent_WithNoActiveSpan_IsNull()
    {
        Assert.Null(Activity.Current);
        Assert.Null(TraceContext.ActiveTraceParent());
    }

    [Fact]
    public void ActiveTraceParent_WithAnActiveSpan_IsTheRealW3CTraceParentString()
    {
        using var activity = OtcActivity.Source.StartActivity("unit-test");

        var traceParent = TraceContext.ActiveTraceParent();

        Assert.NotNull(traceParent);
        Assert.Equal(activity!.Id, traceParent);
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceParent);
    }

    [Fact]
    public void ContextFromTraceParent_Null_IsNull()
    {
        Assert.Null(TraceContext.ContextFromTraceParent(null));
    }

    [Fact]
    public void ContextFromTraceParent_Empty_IsNull()
    {
        Assert.Null(TraceContext.ContextFromTraceParent(string.Empty));
    }

    [Fact]
    public void ContextFromTraceParent_Malformed_IsNull()
    {
        Assert.Null(TraceContext.ContextFromTraceParent("not-a-traceparent"));
    }

    [Fact]
    public void ContextFromTraceParent_ARealTraceParent_RestoresTheSameTraceId()
    {
        using var activity = OtcActivity.Source.StartActivity("unit-test");
        var traceParent = activity!.Id!;

        var restored = TraceContext.ContextFromTraceParent(traceParent);

        Assert.NotNull(restored);
        Assert.Equal(activity.TraceId, restored!.Value.TraceId);
        Assert.Equal(activity.SpanId, restored.Value.SpanId);
    }

    /// <summary>A child span started under a RESTORED context keeps the SAME trace id and mints a FRESH span id — #7's exact wording, ported.</summary>
    [Fact]
    public void ARestoredContextChild_KeepsTheSameTraceId_AndMintsAFreshSpanId()
    {
        using var root = OtcActivity.Source.StartActivity("root");
        var restored = TraceContext.ContextFromTraceParent(root!.Id)!.Value;

        using var child = OtcActivity.Source.StartActivity("child", ActivityKind.Producer, parentContext: restored);

        Assert.NotNull(child);
        Assert.Equal(root.TraceId, child!.TraceId);
        Assert.NotEqual(root.SpanId, child.SpanId);
        Assert.Equal(root.SpanId, child.ParentSpanId);
    }

    [Fact]
    public void InjectNats_WithAnActiveSpan_AddsTheRealTraceParentToAFreshHeaderInstance()
    {
        using var activity = OtcActivity.Source.StartActivity("unit-test");
        var headers = new NatsHeaders();

        TraceContext.InjectNats(headers);

        Assert.True(headers.TryGetLastValue("traceparent", out var traceParent));
        Assert.Equal(activity!.Id, traceParent);
    }

    [Fact]
    public void InjectNats_WithNoActiveSpan_AddsNoTraceparentHeaderAtAll()
    {
        var headers = new NatsHeaders();

        TraceContext.InjectNats(headers);

        Assert.False(headers.ContainsKey("traceparent"));
    }

    [Fact]
    public void ExtractNats_ARealInjectedHeaderSet_RoundTripsToTheSameTraceId()
    {
        using var activity = OtcActivity.Source.StartActivity("unit-test");
        var headers = new NatsHeaders();
        TraceContext.InjectNats(headers);

        var extracted = TraceContext.ExtractNats(headers);

        Assert.NotNull(extracted);
        Assert.Equal(activity!.TraceId, extracted!.Value.TraceId);
    }

    [Fact]
    public void ExtractNats_HeadersWithNoTraceparent_IsNull()
    {
        Assert.Null(TraceContext.ExtractNats(new NatsHeaders()));
    }

    [Fact]
    public void ExtractNats_NullHeaders_IsNull()
    {
        Assert.Null(TraceContext.ExtractNats(null));
    }

    [Fact]
    public void ExtractKafka_ARealTraceParentEntry_RoundTripsToTheSameTraceId()
    {
        using var activity = OtcActivity.Source.StartActivity("unit-test");
        var headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["traceparent"] = activity!.Id! };

        var extracted = TraceContext.ExtractKafka(headers);

        Assert.NotNull(extracted);
        Assert.Equal(activity.TraceId, extracted!.Value.TraceId);
    }

    [Fact]
    public void ExtractKafka_NoTraceparentEntry_IsNull()
    {
        Assert.Null(TraceContext.ExtractKafka(new Dictionary<string, string>(StringComparer.Ordinal)));
    }
}
