using NATS.Client.Core;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Fix round for feature <c>orders_cancel_responder</c>: the first
/// <c>./quality.sh</c> run of the D1/D2 fix round showed
/// <c>OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound</c> fail
/// with <see cref="NatsNoRespondersException"/> once, then pass twice more —
/// which the report at the time wrote off as "a transient
/// resource-contention flake." That conclusion violated CLAUDE.md's own
/// rule ("a green run is not evidence about a red one") and its own
/// disclosed mechanism said otherwise: the test sends its
/// <c>orders.cancel</c> RPC immediately after
/// <see cref="SagaIntegrationTestSupport.StartHostAsync"/> returns, with no
/// wait for <c>OrdersCreateResponder</c>'s NATS subscription to actually be
/// live — <c>BackgroundService.StartAsync</c> returns as soon as
/// <c>ExecuteAsync</c> is SCHEDULED, not once its subscriptions have landed
/// server-side.
///
/// This file proves that mechanism is real, deterministically — not by
/// re-running the flaky test and hoping, but by CONSTRUCTING the exact
/// failure condition on purpose (a subscriber not yet live) so the outcome
/// is a matter of KIND, not of PROBABILITY:
///
/// <list type="number">
/// <item>
/// <see cref="RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime"/> —
/// the production subject and payload shape (<c>orders.cancel</c>,
/// <see cref="OrdersCancelRequestPayload"/>), with NO subscriber at all,
/// reproduces the EXACT exception the flaky run showed, 100% of the time.
/// This is what a caller racing ahead of the responder's subscription looks
/// like from the wire's point of view — the same condition, just captured
/// at a moment (before any subscribe attempt) where it is certain rather
/// than possible.
/// </item>
/// <item>
/// <see cref="WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically"/> —
/// a synthetic subscriber whose subscription is deliberately delayed by a
/// controlled interval (300ms), well past the request's own timeout (a
/// budget an order of magnitude smaller). A caller that does not wait loses
/// this race every time, by construction — proving the general shape of
/// the defect (send-before-subscribe) fails deterministically, not
/// occasionally.
/// </item>
/// <item>
/// <see cref="WithTheWait_ADelayedSubscriberNoLongerLosesTheRace"/> — the
/// SAME delayed subscriber, but the caller now goes through
/// <see cref="SagaIntegrationTestSupport.WaitUntilReachableAsync"/> (the
/// exact retry/catch loop <c>StartHostAsync</c> now calls before it returns)
/// first. The wait absorbs the delay and the subsequent request succeeds —
/// proving the fix's own retry loop, not merely a rerun of the original
/// test, closes exactly the mechanism (1) and (2) demonstrate.
/// </item>
/// </list>
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class OrdersCancelResponderReadinessRaceTests(NatsContainerFixture nats)
{
    [Fact]
    public async Task RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        var probe = RpcJson.Serialize(new OrdersCancelRequestPayload(Guid.NewGuid(), OrderReference: null, "operator_cancelled", Note: null));

        // No host, no responder, nothing subscribed to orders.cancel on
        // this broker at all — the certain limiting case of "the RPC is
        // sent before the subscription is live," which is exactly what
        // UnknownOrderId_RepliesNotFound risked when StartHostAsync
        // returned before OrdersCreateResponder's subscription had landed.
        var ex = await Assert.ThrowsAsync<NatsNoRespondersException>(() =>
            caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCancel,
                probe,
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) },
                cancellationToken: CancellationToken.None).AsTask());

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically()
    {
        var subject = $"orders.cancel.readiness-race-repro.{Guid.NewGuid():N}";
        await using var subscribeTask = StartDelayedSubscriberAsync(nats.Url, subject, TimeSpan.FromMilliseconds(300));

        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        // Sent the instant the caller is ready — exactly the OLD
        // StartHostAsync's behaviour: no wait for the subscriber, a request
        // timeout an order of magnitude smaller than the subscriber's own
        // delay, so this is not a coin flip.
        await Assert.ThrowsAsync<NatsNoRespondersException>(() =>
            caller.RequestAsync<byte[], byte[]>(
                subject,
                "probe"u8.ToArray(),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(30) },
                cancellationToken: CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task WithTheWait_ADelayedSubscriberNoLongerLosesTheRace()
    {
        var subject = $"orders.cancel.readiness-race-repro.{Guid.NewGuid():N}";
        await using var subscribeTask = StartDelayedSubscriberAsync(nats.Url, subject, TimeSpan.FromMilliseconds(300));

        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        // The SAME 300ms-delayed subscriber as the test above — the only
        // difference is this caller goes through the fix's own retry loop
        // first, the exact function SagaIntegrationTestSupport.StartHostAsync
        // now calls before it returns.
        await SagaIntegrationTestSupport.WaitUntilReachableAsync(caller, subject, "probe"u8.ToArray(), CancellationToken.None);

        var reply = await caller.RequestAsync<byte[], byte[]>(
            subject,
            "probe"u8.ToArray(),
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reply.Data);
    }

    /// <summary>
    /// A minimal, self-contained stand-in subscriber whose OWN
    /// <c>SubscribeAsync</c> call is deliberately delayed by
    /// <paramref name="delay"/> before it starts listening — a controlled
    /// stand-in for the real, uncontrolled window between
    /// <c>host.StartAsync()</c> returning and <c>OrdersCreateResponder</c>'s
    /// own subscription landing server-side. Disposing the returned handle
    /// awaits the background loop and its connection's teardown.
    /// </summary>
    private static DelayedSubscriberHandle StartDelayedSubscriberAsync(string natsUrl, string subject, TimeSpan delay)
    {
        var cts = new CancellationTokenSource();
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });

        var loop = RunAsync(connection, subject, delay, cts.Token);

        return new DelayedSubscriberHandle(connection, cts, loop);

        static async Task RunAsync(NatsConnection connection, string subject, TimeSpan delay, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                await message.ReplyAsync("ok"u8.ToArray(), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class DelayedSubscriberHandle(NatsConnection connection, CancellationTokenSource cts, Task loop) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — exactly what cancelling the loop above causes.
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                cts.Dispose();
            }
        }
    }
}
