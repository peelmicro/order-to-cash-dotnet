using System.Diagnostics;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Messaging.Rpc;
using OrderToCash.Gateway.Infrastructure.Observability;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// The feature's own named risk, proved at the level the risk actually
/// lives at: a REAL NATS broker, never a mocked <see cref="INatsConnection"/>
/// — "the check is an end-to-end call through a real broker against the
/// real responders — not two green suites". A stand-in responder plays the
/// "real responder" role for the cases a genuine no-responder/timeout
/// scenario cannot be staged against a live service deterministically;
/// <see cref="FulfillmentStockEndToEndTests"/> covers the ACTUAL responder
/// half of the same risk.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class NatsRpcClientIntegrationTests(NatsContainerFixture nats)
{
    private sealed record Ping(string Text);

    private sealed record Pong(string Echo);

    /// <summary>
    /// The budget every SUCCESS-path case in this class runs under. Backlog
    /// id 81 measured it rather than adjusting it: the charged round trip
    /// through the real broker is p99 1.4 ms idle and p99 5.3 ms with the
    /// machine at 2x CPU oversubscription (32 busy loops on 16 cores), and
    /// the worst SUCCESSFUL charged call observed across 1 348 concurrent
    /// pairs was 127 ms — so 2 000 ms is a ~15x margin on the worst
    /// observation and ~380x on p99, and is NOT the constraint that was
    /// binding when this test went red. See
    /// <see cref="OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId"/>
    /// for what was.
    /// </summary>
    private const int SuccessPathBudgetMs = 2000;

    private static NatsRpcClient BuildClient(INatsConnection connection, int timeoutMs = SuccessPathBudgetMs) =>
        new(connection, Options.Create(new NatsOptions { DefaultTimeoutMs = timeoutMs }));

    [Fact]
    public async Task CallAsync_DecodesTheSuccessReply_AndSendsTheCorrelationAndRequestIdHeaders()
    {
        const string subject = "gateway.it.echo";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url, subject, data => RpcJson.Serialize(new Pong($"echo:{RpcJson.Deserialize<Ping>(data).Text}")), CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection);
        var correlationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var reply = await client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(correlationId, requestId), CancellationToken.None);

        Assert.Equal("echo:hello", reply.Echo);
        Assert.NotNull(responder.LastRequestHeaders);
        Assert.Equal(correlationId.ToString(), responder.LastRequestHeaders!["x-correlation-id"].ToString());
        Assert.Equal(requestId.ToString(), responder.LastRequestHeaders!["x-request-id"].ToString());
    }

    /// <summary>No responder subscribed at all — the IMMEDIATE transport refusal, never waiting out the deadline.</summary>
    [Fact]
    public async Task CallAsync_Throws_RpcTransportError_WhenNoResponderIsSubscribed()
    {
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 500);

        var error = await Assert.ThrowsAsync<RpcTransportError>(
            () => client.CallAsync<Ping, Pong>("gateway.it.no-responder", new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("UNAVAILABLE", error.Code);
    }

    /// <summary>A responder IS subscribed but never answers — the caller's own deadline elapses, a DIFFERENT failure from "no responder at all".</summary>
    [Fact]
    public async Task CallAsync_Throws_RpcTimeoutError_WhenAResponderIsSubscribedButNeverReplies()
    {
        const string subject = "gateway.it.silent";
        await using var responder = await StandInResponder.StartSilentAsync(nats.Url, subject, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 300);

        var error = await Assert.ThrowsAsync<RpcTimeoutError>(
            () => client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("TIMEOUT", error.Code);
    }

    /// <summary>The two transient failures are DIFFERENT RpcCallError subtypes — the exact distinction the acceptance bullet names ("guard the mapping per case, not that an error becomes an error").</summary>
    [Fact]
    public async Task CallAsync_NoResponderAndSilentResponder_ThrowDifferentErrorTypes()
    {
        const string silentSubject = "gateway.it.silent-vs-unavailable";
        await using var responder = await StandInResponder.StartSilentAsync(nats.Url, silentSubject, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 300);

        var noResponderError = await Assert.ThrowsAsync<RpcTransportError>(
            () => client.CallAsync<Ping, Pong>("gateway.it.genuinely-nothing-here", new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
        var silentError = await Assert.ThrowsAsync<RpcTimeoutError>(
            () => client.CallAsync<Ping, Pong>(silentSubject, new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.NotEqual(noResponderError.Code, silentError.Code);
    }

    [Fact]
    public async Task CallAsync_Throws_RpcBusinessError_CarryingTheResponderAsSentCodeMessageAndDetails()
    {
        const string subject = "gateway.it.business-error";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url,
            subject,
            _ => RpcJson.Serialize(new RpcErrorPayload("PRECONDITION_FAILED", "invoice already paid", new Dictionary<string, object?> { ["code"] = "INVOICE_ALREADY_PAID" })),
            CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection);

        var error = await Assert.ThrowsAsync<RpcBusinessError>(
            () => client.CallAsync<Ping, Pong>(subject, new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.Equal("invoice already paid", error.Message);
        Assert.NotNull(error.Details);
        // The exact normalisation this class exists for: a JsonElement
        // scalar unwrapped to a native string, not left boxed as
        // JsonElement (which RpcErrorClassifier's `is string` check would
        // silently never match).
        Assert.IsType<string>(error.Details!["code"]);
        Assert.Equal("INVOICE_ALREADY_PAID", error.Details!["code"]);
    }

    /// <summary>
    /// Review round 2, D5 row 34 — the Gateway client injects the REAL
    /// active trace id into the outbound request headers (design.md §11
    /// row 34, ledger L21). No Gateway test asserted this before this
    /// round. Armed by deleting <c>TraceContext.InjectNats(headers)</c> in
    /// <c>NatsRpcClient.cs</c> — see the round-2 fix record.
    /// </summary>
    [Fact]
    public async Task D5_Row34_InjectsTheActiveTraceIdIntoTheOutboundRequestHeaders()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OtcActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        const string subject = "gateway.it.trace-inject";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url, subject, data => RpcJson.Serialize(new Pong($"echo:{RpcJson.Deserialize<Ping>(data).Text}")), CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection);

        Activity? callActivity;
        using (callActivity = OtcActivity.Source.StartActivity("test-call"))
        {
            Assert.NotNull(callActivity);
            await client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        }

        Assert.NotNull(responder.LastRequestHeaders);
        var traceparent = responder.LastRequestHeaders!["traceparent"].ToString();

        // Backlog id 82 — a bare Assert.False(string.IsNullOrEmpty(traceparent))
        // used to stand here. It printed "Expected: False / Actual: True" and
        // named nothing, and the Assert.Equal on the next line already
        // subsumes it: a missing header reads back as the empty string, which
        // that comparison reports as a string difference against the real
        // activity id.
        Assert.Equal(callActivity!.Id, traceparent);
        Assert.True(ActivityContext.TryParse(traceparent, null, out var extracted), $"'{traceparent}' did not parse as a W3C traceparent.");
        Assert.Equal(callActivity.TraceId, extracted.TraceId);
    }

    /// <summary>
    /// Review round 2, L21 addendum — two GENUINELY CONCURRENT calls, each
    /// under its own active <see cref="Activity"/>, each observed by a REAL
    /// responder to carry its OWN <c>traceparent</c> — never one bleeding
    /// into the other, which is exactly the failure a HOISTED (shared)
    /// <see cref="NatsHeaders"/> instance would produce (design.md §5.2,
    /// ledger L21). <see cref="NatsRpcClient.CallAsync{TRequest,TReply}"/>
    /// builds a fresh <see cref="NatsHeaders"/> per call — this proves it
    /// against a REAL broker, the same shape
    /// <c>NatsSagaCommandsAdapterTests.OR4_TwoConcurrentCalls…</c>
    /// establishes at the unit level for Orders' own outbound adapter.
    ///
    /// D9 (review round 3) — the round-2 arm used <c>await
    /// Task.Delay(50)</c> between starting call A and call B, which a real
    /// connection's own build-to-send latency closes long before call B
    /// ever builds its headers, so the plain hoist passed 3/3. The
    /// PREVIOUS wording of this comment attributed a widened-delay
    /// "make the collision deterministic" instruction to <c>CLAUDE.md</c>;
    /// that sentence does not appear there (it was the leader's own brief)
    /// and the widening lived in the MUTATION, not here — both corrected.
    /// This case now creates the overlap ITSELF, via
    /// <see cref="RequestOverlapBarrierConnection"/>, a thin
    /// <c>INatsConnection</c> decorator wrapping the real connection: its
    /// <c>RequestAsync</c> blocks each call until BOTH have arrived, so a
    /// plain hoist with NO delay in the mutation is proven to collide on
    /// EVERY run, over the real broker.
    ///
    /// Backlog id 81 — this case went red once inside a full
    /// <c>quality.sh</c> run with <c>RpcTimeoutError : RPC call to
    /// "gateway.it.trace-concurrent" timed out after 2000ms</c>, and the
    /// budget was NOT the cause. Measured, on an IDLE machine, with the
    /// cause established before anything was changed: issuing the two
    /// concurrent calls as a brand-new <see cref="NatsConnection"/>'s FIRST
    /// traffic loses one of the two replies in about 1 % of rounds — 8
    /// losses in 648 cold rounds — while establishing the connection first
    /// gave 0 losses in 700 rounds (500 by an explicit
    /// <c>ConnectAsync()</c>, 200 by one ordinary prior request). In every
    /// one of the 8 losses the stand-in responder had received BOTH
    /// requests and the losing call's own TWIN completed in 2-8 ms, which
    /// no amount of machine contention can produce: a loaded machine
    /// cannot make one call take 2 000 ms while its simultaneous partner
    /// takes 4 ms. Raising the budget would only have made the red run
    /// slower — D3 of the same diagnostic lost a reply under a 120 000 ms
    /// budget and waited out all two minutes of it.
    ///
    /// The fix is therefore the <c>ConnectAsync()</c> below, and the
    /// assertion after it is its guard: the overlap this case needs is
    /// supplied by <see cref="RequestOverlapBarrierConnection"/>, so
    /// issuing the pair over an UNESTABLISHED connection adds nothing to
    /// what it proves and adds a 1 %-per-run lost reply.
    /// </summary>
    [Fact]
    public async Task OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OtcActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        const string subject = "gateway.it.trace-concurrent";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url, subject, data => RpcJson.Serialize(new Pong($"echo:{RpcJson.Deserialize<Ping>(data).Text}")), CancellationToken.None);
        await using var realConnection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // Backlog id 81 — establish the connection BEFORE the concurrent
        // pair is issued. NatsConnection connects lazily, so without this
        // line the two barrier-released calls race the connection's own
        // establishment and one of them loses its reply about 1 % of runs.
        await realConnection.ConnectAsync();
        Assert.True(
            realConnection.ConnectionState == NatsConnectionState.Open,
            $"backlog id 81: this case must issue its two concurrent calls over an ALREADY ESTABLISHED connection, never as the connection's first traffic — "
            + $"the cold shape loses one of the two replies in ~1 % of runs (8 losses in 648 cold rounds against 0 in 700 established ones) and the loser then "
            + $"waits out the whole {SuccessPathBudgetMs} ms budget, which is the RpcTimeoutError this test reported on 2026-09-12. "
            + $"The connection state at the moment the pair was about to be issued was '{realConnection.ConnectionState}', not '{NatsConnectionState.Open}'.");

        var connection = new RequestOverlapBarrierConnection(realConnection);
        var client = BuildClient(connection);

        async Task CallUnderOwnActivityAsync()
        {
            using var activity = OtcActivity.Source.StartActivity("test-call");
            await client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        }

        // Task.Run, not a bare invocation: RequestOverlapBarrierConnection's
        // barrier BLOCKS the calling thread synchronously until the second
        // call arrives, and CallUnderOwnActivityAsync runs synchronously up
        // to that exact point (no await precedes it) — a bare
        // `CallUnderOwnActivityAsync()` would therefore block the TEST's
        // own thread before it ever reached the line starting call B,
        // deadlocking. Each call gets its OWN thread-pool thread instead,
        // and Activity.Current still flows correctly into it (AsyncLocal
        // flows through Task.Run by ExecutionContext capture), so
        // OtcActivity.Source.StartActivity still tags each call with its
        // own, distinct Activity.
        var callA = Task.Run(CallUnderOwnActivityAsync);
        var callB = Task.Run(CallUnderOwnActivityAsync);
        await Task.WhenAll(callA, callB);

        Assert.Equal(2, responder.ObservedHeaders.Count);
        var traceParents = responder.ObservedHeaders.Select(h => h["traceparent"].ToString()).ToList();
        Assert.All(traceParents, tp => Assert.False(string.IsNullOrEmpty(tp), $"one of the {traceParents.Count} concurrent outbound calls carried an empty traceparent header; the values observed were [{string.Join(", ", traceParents)}]. (backlog id 82 — the bare Assert.False named nothing)"));
        Assert.NotEqual(traceParents[0], traceParents[1]);
    }
}
