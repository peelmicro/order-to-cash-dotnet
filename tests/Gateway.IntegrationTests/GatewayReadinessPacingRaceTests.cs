using System.Diagnostics;
using NATS.Client.Core;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Backlog id 69 — the 7th and 8th unpaced readiness sites found while
/// enumerating id 63's six were PACED during the guard-hardening loop and
/// then left unguarded: the reviewer deleted both <c>Task.Delay</c> calls,
/// forced a rebuild, and <c>Gateway.IntegrationTests</c> was still 48/48
/// green. This class closes that, in the SAME three-part deterministic shape
/// the other six sites already use (<c>OrdersCancelResponderReadinessRaceTests</c>,
/// <c>BillingResponderReadinessRaceTests</c>, <c>FulfillmentResponderReadinessRaceTests</c>):
///
/// <list type="number">
/// <item>a request against a subject with NO subscriber reproduces
/// <see cref="NatsNoRespondersException"/> — the exact sentinel both loops
/// retry on, and the reason an attempt costs no wall-clock;</item>
/// <item>an UNPACED replica of the same hundred-attempt loop, racing a
/// subscriber delayed by a controlled 300 ms, loses EVERY time and loses for
/// the RIGHT reason — it exhausts its whole attempt budget in a fraction of
/// that delay;</item>
/// <item>each of the two REAL paced loops, against the SAME delayed
/// subscriber, wins EVERY time.</item>
/// </list>
///
/// A difference of KIND — a loop that spends wall-clock versus one that does
/// not — never an observation that a flake stopped. Joins
/// <see cref="NatsCollection"/>, which <c>NatsRpcClientIntegrationTests</c>,
/// <c>StreamHttpTests</c> and <c>StreamHeartbeatHttpTests</c> already share,
/// so it starts no container of its own.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class GatewayReadinessPacingRaceTests(NatsContainerFixture nats)
{
    /// <summary>An order of magnitude past the 200 ms per-attempt request timeout both loops use, and ~6x the 50 ms pacing interval — so a paced loop needs several real waits to absorb it and an unpaced one cannot absorb it at all.</summary>
    private static readonly TimeSpan _subscriberDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>"Every time" is a claim about repetition, so every race below is run this many times and the assertion is over ALL the runs, never one sample.</summary>
    private const int Runs = 3;

    [Fact]
    public async Task RequestSentBeforeAnythingSubscribes_ThrowsNoRespondersImmediately_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        for (var run = 1; run <= Runs; run++)
        {
            var subject = $"gateway.readiness-race-repro.{Guid.NewGuid():N}";
            var stopwatch = Stopwatch.StartNew();

            await Assert.ThrowsAsync<NatsNoRespondersException>(() =>
                caller.RequestAsync<byte[], byte[]>(
                    subject,
                    "probe"u8.ToArray(),
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(2) },
                    cancellationToken: CancellationToken.None).AsTask());

            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
                $"run {run}/{Runs}: the no-responders sentinel took {stopwatch.Elapsed.TotalMilliseconds:F0} ms against a 2000 ms request timeout. " +
                "This whole class rests on that sentinel returning IMMEDIATELY rather than waiting the timeout out — if it now waits, a retry budget " +
                "counted in attempts really would be a budget in wall-clock and backlog id 63/id 69's premise would no longer hold.");
        }
    }

    [Fact]
    public async Task WithoutPacing_AReplicaOfTheSameHundredAttemptLoop_BurnsItsWholeBudgetWellInsideTheSubscribersDelay_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        for (var run = 1; run <= Runs; run++)
        {
            var subject = $"gateway.readiness-race-repro.{Guid.NewGuid():N}";
            await using var subscriber = StartDelayedSubscriber(nats.Url, subject, _subscriberDelay);

            var stopwatch = Stopwatch.StartNew();
            var reachable = await UnpacedReplicaReachableAsync(caller, subject, CancellationToken.None);
            stopwatch.Stop();

            Assert.False(
                reachable,
                $"run {run}/{Runs}: the UNPACED replica reached a subscriber that only subscribes after {_subscriberDelay.TotalMilliseconds:F0} ms, in " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms. This test is the control for the two paced loops below — if the unpaced shape can win, the " +
                "race is no longer decided by pacing and neither of those tests proves anything.");

            Assert.True(
                stopwatch.Elapsed < _subscriberDelay,
                $"run {run}/{Runs}: the UNPACED replica did lose, but it took {stopwatch.Elapsed.TotalMilliseconds:F0} ms to lose — longer than the " +
                $"{_subscriberDelay.TotalMilliseconds:F0} ms subscriber delay it was racing, so it lost for some reason OTHER than spending no wall-clock. " +
                "A loss for the wrong reason is not evidence about pacing (backlog id 69, acceptance bullet 3).");
        }
    }

    [Fact]
    public async Task StandInResponderReadinessLoop_AbsorbsADelayedSubscription_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        for (var run = 1; run <= Runs; run++)
        {
            var subject = $"gateway.readiness-race-repro.{Guid.NewGuid():N}";
            await using var subscriber = StartDelayedSubscriber(nats.Url, subject, _subscriberDelay);

            var (failure, elapsed) = await TimeReadinessWaitAsync(() =>
                StandInResponder.WaitUntilReachableAsync(caller, subject, [0xFE], CancellationToken.None));

            AssertThePacedLoopAbsorbedTheDelay(
                run,
                failure,
                elapsed,
                site: "StandInResponder.WaitUntilReachableAsync (tests/Gateway.IntegrationTests/StandInResponder.cs)",
                pacingInterval: StandInResponder.ReadinessPacingInterval,
                attempts: StandInResponder.ReadinessAttempts);
        }
    }

    [Fact]
    public async Task FulfillmentStockEndToEndReadinessLoop_AbsorbsADelayedSubscription_EveryTime()
    {
        await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

        for (var run = 1; run <= Runs; run++)
        {
            var subject = $"gateway.readiness-race-repro.{Guid.NewGuid():N}";
            await using var subscriber = StartDelayedSubscriber(nats.Url, subject, _subscriberDelay);

            var (failure, elapsed) = await TimeReadinessWaitAsync(() =>
                FulfillmentStockEndToEndTests.WaitUntilReachableAsync(caller, subject, "probe"u8.ToArray()));

            AssertThePacedLoopAbsorbedTheDelay(
                run,
                failure,
                elapsed,
                site: "FulfillmentStockEndToEndTests.WaitUntilReachableAsync (tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs)",
                pacingInterval: FulfillmentStockEndToEndTests.ReadinessPacingInterval,
                attempts: FulfillmentStockEndToEndTests.ReadinessAttempts);
        }
    }

    /// <summary>
    /// The ONE assertion both paced-loop tests end in. Its message names the
    /// pacing, the site, the run, the observed wall-clock and the budget —
    /// backlog id 82's standard, applied to this entry's own arms: a failure
    /// reading "Expected: True / Actual: False" would be evidence only to a
    /// reader who opens the stack line.
    /// </summary>
    private static void AssertThePacedLoopAbsorbedTheDelay(int run, Exception? failure, TimeSpan elapsed, string site, TimeSpan pacingInterval, int attempts)
    {
        var pacedBudget = TimeSpan.FromMilliseconds(attempts * pacingInterval.TotalMilliseconds);

        Assert.True(
            failure is null && elapsed >= _subscriberDelay - TimeSpan.FromMilliseconds(50),
            failure is not null
                ? $"run {run}/{Runs}: {site} gave up after {elapsed.TotalMilliseconds:F0} ms against a subscriber delayed by only " +
                  $"{_subscriberDelay.TotalMilliseconds:F0} ms. Its {attempts}-attempt budget is a budget in WALL-CLOCK only while it waits " +
                  $"{pacingInterval.TotalMilliseconds:F0} ms after every failed attempt (a paced budget of {pacedBudget.TotalMilliseconds:F0} ms); without that delay " +
                  $"NatsNoRespondersException returns immediately and the whole budget is spent in about a millisecond. The loop is NOT PACING. " +
                  $"Underlying failure: {failure.GetType().Name}: {failure.Message}"
                : $"run {run}/{Runs}: {site} returned after only {elapsed.TotalMilliseconds:F0} ms, before the subscriber it was racing had even subscribed " +
                  $"({_subscriberDelay.TotalMilliseconds:F0} ms). It cannot have absorbed the delay by pacing, so this run proves nothing about pacing.");
    }

    private static async Task<(Exception? Failure, TimeSpan Elapsed)> TimeReadinessWaitAsync(Func<Task> readinessWait)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await readinessWait();
            stopwatch.Stop();
            return (null, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is TimeoutException or NatsNoRespondersException or NatsNoReplyException)
        {
            stopwatch.Stop();
            return (ex, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// A deliberate UNPACED replica of the two loops under test — the same
    /// hundred attempts at the same 200 ms per-attempt timeout, catching the
    /// same two sentinels, with the one <c>Task.Delay</c> both real loops
    /// perform removed. It is the control: it makes the difference this class
    /// proves a difference of KIND (a loop that spends wall-clock versus one
    /// that does not) rather than a difference of luck, without mutating the
    /// real loops to get it.
    /// </summary>
    private static async Task<bool> UnpacedReplicaReachableAsync(INatsConnection connection, string subject, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < StandInResponder.ReadinessAttempts; attempt++)
        {
            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    subject,
                    "probe"u8.ToArray(),
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (reply.Data is not null)
                {
                    return true;
                }
            }
            catch (NatsNoReplyException)
            {
            }
            catch (NatsNoRespondersException)
            {
            }

            // NO Task.Delay here — that absence is the whole point.
        }

        return false;
    }

    private static DelayedSubscriberHandle StartDelayedSubscriber(string natsUrl, string subject, TimeSpan delay)
    {
        var cts = new CancellationTokenSource();
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });

        return new DelayedSubscriberHandle(connection, cts, RunAsync(connection, subject, delay, cts.Token));

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
