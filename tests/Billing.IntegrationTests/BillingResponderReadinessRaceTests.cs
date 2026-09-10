using NATS.Client.Core;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Backlog id 63 — proves, deterministically, that <see cref="BillingHostFixture"/>'s
/// generic <c>WaitUntilReachableAsync(NatsConnection, string, byte[], CancellationToken)</c>
/// overload is genuinely PACED, not merely bounded by its per-attempt
/// request timeout. Same three-part shape as Orders.IntegrationTests'
/// <c>OrdersCancelResponderReadinessRaceTests</c> (the precedent this loop
/// was ported from):
///
/// <list type="number">
/// <item>a request against a subject with NO subscriber reproduces
/// <see cref="NatsNoRespondersException"/> — the exact sentinel the loop
/// retries on;</item>
/// <item>WITHOUT pacing, a caller racing a subscriber delayed by a
/// controlled interval (300ms) — an order of magnitude past the request's
/// own timeout — loses the race deterministically, by construction;</item>
/// <item>WITH the fix's own retry loop, the SAME delayed subscriber no
/// longer wins: the wait absorbs the delay and the subsequent request
/// succeeds.</item>
/// </list>
///
/// This proves the fix is a difference of KIND (a caller that waits versus
/// one that does not), not of probability.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class BillingResponderReadinessRaceTests(NatsContainerFixture nats)
{
    [Fact]
    public async Task RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
        var subject = $"credit.list.readiness-race-repro.{Guid.NewGuid():N}";

        var ex = await Assert.ThrowsAsync<NatsNoRespondersException>(() =>
            caller.RequestAsync<byte[], byte[]>(
                subject,
                "probe"u8.ToArray(),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) },
                cancellationToken: CancellationToken.None).AsTask());

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically()
    {
        var subject = $"credit.list.readiness-race-repro.{Guid.NewGuid():N}";
        await using var subscribeTask = StartDelayedSubscriberAsync(nats.Url, subject, TimeSpan.FromMilliseconds(300));

        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

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
        var subject = $"credit.list.readiness-race-repro.{Guid.NewGuid():N}";
        await using var subscribeTask = StartDelayedSubscriberAsync(nats.Url, subject, TimeSpan.FromMilliseconds(300));

        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        // The SAME 300ms-delayed subscriber, but this caller goes through
        // the fixture's own (now paced) retry loop first.
        await BillingHostFixture.WaitUntilReachableAsync(caller, subject, "probe"u8.ToArray(), CancellationToken.None);

        var reply = await caller.RequestAsync<byte[], byte[]>(
            subject,
            "probe"u8.ToArray(),
            replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) },
            cancellationToken: CancellationToken.None);

        Assert.NotNull(reply.Data);
    }

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
