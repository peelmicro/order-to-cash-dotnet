using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature 46 (`orders_stock_check_rpc_error_discriminator`): over the REAL
/// transport — a real NATS broker and a real stand-in Fulfillment responder
/// answering raw <c>RpcError</c> bytes, never a mocked <see cref="INatsConnection"/>
/// (<see cref="NatsStockAvailabilityChecker"/>'s own class remark). Proves
/// the discriminator directly, at the level the bug lives at, before
/// <c>OrdersCreateAcceptanceTests</c>' end-to-end counterpart proves what
/// <c>orders.create</c> answers the caller with.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class NatsStockAvailabilityCheckerTests(NatsContainerFixture nats)
{
    /// <summary>
    /// The exact bug fixed: before the discriminator, an RpcError-shaped
    /// reply deserialised into <c>StockCheckReplyPayload</c> with a null
    /// <c>Lines</c>, and the very next line's LINQ <c>.Select</c> threw a
    /// bare <see cref="NullReferenceException"/>. Armed by deleting the
    /// discriminator — see progress/impl report.
    /// </summary>
    [Theory]
    [InlineData("NOT_FOUND", "product ZZZ is not known to Fulfillment")]
    [InlineData("PRECONDITION_FAILED", "stock item is locked for replenishment")]
    public async Task AnRpcErrorReply_ThrowsStockCheckBusinessErrorCarryingTheRespondersCodeAndMessage_NeverANullReferenceException(string responderCode, string responderMessage)
    {
        await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartErrorAsync(nats.Url, responderCode, responderMessage, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        var lines = new[] { new StockAvailabilityLine("P1", new Quantity(1)) };

        var error = await Assert.ThrowsAsync<StockCheckBusinessError>(
            () => checker.CheckAsync("ACME", lines, CancellationToken.None));

        Assert.Equal(RpcSubjects.StockCheck, error.Subject);
        Assert.Equal(responderCode, error.RpcErrorCode);
        Assert.Equal(responderMessage, error.ResponderMessage);
    }

    /// <summary>
    /// Advisory A1 (round 2): a reply body that is not valid JSON at all —
    /// neither the <c>RpcError</c> shape nor <c>StockCheckReplyPayload</c> —
    /// must not reach the caller as a bare <see cref="JsonException"/>. #7's
    /// same seam guards this exact case (see the production fix's remark);
    /// this proves the containment, not just the discrimination.
    /// </summary>
    [Fact]
    public async Task AMalformedNonJsonReply_ThrowsStockCheckTransportError_NeverABareJsonException()
    {
        await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartMalformedAsync(nats.Url, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        var lines = new[] { new StockAvailabilityLine("P1", new Quantity(1)) };

        var error = await Assert.ThrowsAsync<StockCheckTransportError>(
            () => checker.CheckAsync("ACME", lines, CancellationToken.None));

        Assert.Equal(RpcSubjects.StockCheck, error.Subject);
    }

    /// <summary>
    /// Review round 2, L21 addendum — the THIRD outbound NATS request site
    /// (the other two: <c>NatsSagaCommandsAdapter</c>, unit-level;
    /// <c>NatsRpcClient</c>, this shape against the Gateway). Two
    /// GENUINELY CONCURRENT <see cref="NatsStockAvailabilityChecker.CheckAsync"/>
    /// calls, each under its own active <see cref="Activity"/>, each
    /// observed by a REAL Fulfillment stand-in to carry its OWN
    /// <c>traceparent</c> — <see cref="NatsHeaders"/> is not thread-safe
    /// (ledger L21), so a HOISTED (shared) instance would let one call's
    /// trace id bleed into the other's.
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

        await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
        await using var realConnection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var connection = new RequestOverlapBarrierConnection(realConnection);
        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        var lines = new[] { new StockAvailabilityLine("P1", new Quantity(1)) };

        async Task CallUnderOwnActivityAsync()
        {
            using var activity = OtcActivity.Source.StartActivity("test-call");
            await checker.CheckAsync("ACME", lines, CancellationToken.None);
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

        Assert.Equal(2, fulfillment.ObservedHeaders.Count);
        var traceParents = fulfillment.ObservedHeaders.Select(h => h["traceparent"].ToString()).ToList();
        Assert.All(traceParents, tp => Assert.False(string.IsNullOrEmpty(tp)));
        Assert.NotEqual(traceParents[0], traceParents[1]);
    }
}
