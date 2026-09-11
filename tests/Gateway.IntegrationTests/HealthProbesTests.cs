using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// R60/OR6, design.md §8, tasks.md A4e — the Gateway checks
/// <c>rpcTransport</c> (NATS) and <c>readModel</c> (MongoDB) only
/// (design.md §8.2 — it owns no write model). Both routes are mapped into
/// the Gateway's own EXISTING <see cref="Microsoft.AspNetCore.Builder.WebApplication"/>
/// pipeline (design.md §8.1), never a separate port. This suite pauses the
/// REAL NATS container — the exact row #7 shipped unbounded, found and
/// disclosed by #7's own IMPLEMENTER
/// (<c>order-to-cash-nestjs/progress/impl_observability_reliability.md:912</c>,
/// the mechanism at <c>:927</c>) and only CONFIRMED by #7's reviewer
/// (<c>order-to-cash-nestjs/progress/review_observability_reliability.md:70-72</c>,
/// design.md §8.3; review round 1, R2) — and proves readiness reports
/// <c>down</c> for that check ONLY, liveness stays <c>200</c> throughout,
/// and readiness recovers on unpause.
/// </summary>
[Collection(GatewayHealthCollection.Name)]
public sealed class HealthProbesTests(NatsContainerFixture nats, MongoContainerFixture mongo)
{
    private sealed record HealthResponseDto(string Status, Dictionary<string, CheckResultDto>? Checks);

    private sealed record CheckResultDto(string Status, string? Detail);

    /// <summary>
    /// design.md §8.3's readiness bound for THIS service: the aggregator
    /// runs its checks SEQUENTIALLY (<c>HealthCheckAggregator.ReadyAsync</c>'s
    /// own <c>foreach</c>, never <c>Task.WhenAll</c>), each individually
    /// bounded to 2s (design.md §8.3), so the worst-case check-only cost
    /// for the Gateway's 2 checks (<c>rpcTransport</c>, <c>readModel</c>)
    /// is 2 x 2s = 4s. The further +2s margin is a FIXED per-request
    /// allowance, not scaled by check count — it pays for the HTTP round
    /// trip itself (Kestrel's own request dispatch, this suite's JSON body
    /// deserialization, loopback network/scheduling jitter), which is paid
    /// once per call regardless of how many checks the aggregator ran, not
    /// once per check. 2 x 2s + 2s = 6s.
    /// </summary>
    private static readonly TimeSpan _pausedReadinessBound = TimeSpan.FromSeconds((2 * 2) + 2);

    /// <summary>
    /// A4e addendum round 2, item 4 — the bound for a <c>/health/live</c>
    /// call sampled WHILE a <c>/health/ready</c> call is genuinely in
    /// flight, far tighter than <see cref="_pausedReadinessBound"/>: the
    /// liveness aggregator (<c>HealthCheckAggregator.Live()</c>) consults
    /// NOTHING — no dependency, no I/O, a pure in-memory return — so 500ms
    /// leaves ample margin for Kestrel's own request dispatch, this
    /// suite's JSON serialization and loopback network/scheduling jitter
    /// under ONE concurrent in-flight readiness call (not the 20-way
    /// artificially constrained stress scenario A4e addendum round 1 used
    /// to make thread-pool starvation deterministic), while remaining an
    /// order of magnitude tighter than the multi-second readiness bound.
    /// </summary>
    private static readonly TimeSpan _liveWhileReadyInFlightBound = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Every individual HTTP round trip in the PRE-PAUSE and POST-RECOVERY
    /// polls below is bounded to its OWN short window, never left to
    /// `HttpClient`'s 100s DEFAULT timeout — under this repository's own
    /// heavy concurrent `dotnet test` load (`quality.sh` runs every
    /// `*.IntegrationTests` project at once), a single request to a real
    /// Kestrel instance can occasionally take longer than that default
    /// (observed live against this suite's sibling copy in
    /// `Fulfillment.IntegrationTests`, `quality.sh` run, 2026-09-10:
    /// `TaskCanceledException` at the 100s mark). A request that does not
    /// complete in time is treated as "not observed this poll" HERE ONLY —
    /// a call made WHILE the dependency is paused below uses
    /// <see cref="TryGetTimedAsync"/> instead, and a timeout there is a
    /// FAILURE, never a skip, because both routes must answer within
    /// <see cref="_pausedReadinessBound"/> throughout the pause window
    /// (tasks.md A4e — "`/health/live` answers 200 throughout").
    /// </summary>
    private static async Task<HttpResponseMessage?> TryGetAsync(HttpClient client, string path)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await client.GetAsync(path, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Times a single HTTP round trip using a per-call token WIDER than
    /// <paramref name="bound"/>, so a probe exceeding its bound is caught
    /// by the CALLER's own elapsed-time assertion — the one the task
    /// actually cares about — rather than being raced and silently
    /// truncated by a tighter client-side cancellation (tasks.md's own
    /// "the per-call token must be at least that bound" instruction).
    /// </summary>
    private static async Task<(HttpResponseMessage? Response, TimeSpan Elapsed)> TryGetTimedAsync(HttpClient client, string path, TimeSpan bound)
    {
        using var cts = new CancellationTokenSource(bound + TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.GetAsync(path, cts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return (response, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return (null, stopwatch.Elapsed);
        }
    }

    private static async Task<HealthResponseDto?> PollUntilStatusAsync(HttpClient client, string path, HttpStatusCode expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await TryGetAsync(client, path);
            if (response is not null && response.StatusCode == expected)
            {
                return await response.Content.ReadFromJsonAsync<HealthResponseDto>();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    [Fact]
    public async Task R60_OR6_ReportsReadyWhileEveryDependencyIsReachable_ThenNotReadyNamingOnlyTheStoppedDependency_WhileLivenessAnswers200Throughout_AndRecoversWhenItReturns()
    {
        await using var gateway = await GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = nats.Url;
            options.Mongo.ConnectionUri = mongo.ConnectionString;
            options.Mongo.Database = $"otc_read_model_health_{Guid.NewGuid():N}";
        });

        var readyBody = await PollUntilStatusAsync(gateway.Client, "/health/ready", HttpStatusCode.OK, TimeSpan.FromSeconds(30));
        Assert.NotNull(readyBody);
        Assert.Equal("up", readyBody!.Status);
        Assert.All(readyBody.Checks!.Values, c => Assert.Equal("up", c.Status));

        var live = await TryGetAsync(gateway.Client, "/health/live");
        Assert.NotNull(live);
        Assert.Equal(HttpStatusCode.OK, live!.StatusCode);

        await nats.PauseAsync();
        try
        {
            // A4e addendum round 2, item 4 — liveness must be sampled WHILE
            // a readiness call is genuinely IN FLIGHT, not merely
            // alternated with it inside the poll loop below: round-1's own
            // variant 2 arming found the in-loop liveness branch
            // unreachable once readiness resolves to 503 on its own first
            // poll, and the condition that actually starved liveness in
            // production (round 1's measured defect) was a
            // `/health/ready` request still EXECUTING, never a completed
            // one.
            var readyInFlightTask = TryGetTimedAsync(gateway.Client, "/health/ready", _pausedReadinessBound);
            var samplesWhileReadyInFlight = 0;
            var sampleDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!readyInFlightTask.IsCompleted && DateTime.UtcNow < sampleDeadline)
            {
                var (liveDuringReady, liveDuringReadyElapsed) = await TryGetTimedAsync(gateway.Client, "/health/live", _liveWhileReadyInFlightBound);
                Assert.True(
                    liveDuringReady is not null && liveDuringReady.StatusCode == HttpStatusCode.OK && liveDuringReadyElapsed <= _liveWhileReadyInFlightBound,
                    $"/health/live did not answer 200 within {_liveWhileReadyInFlightBound.TotalMilliseconds:F0}ms while a /health/ready call was in flight (elapsed {liveDuringReadyElapsed.TotalMilliseconds:F0}ms{(liveDuringReady is null ? ", no response" : $", status {liveDuringReady.StatusCode}")}).");
                samplesWhileReadyInFlight++;
                await Task.Delay(TimeSpan.FromMilliseconds(150));
            }

            Assert.True(
                samplesWhileReadyInFlight >= 3,
                $"only {samplesWhileReadyInFlight} /health/live sample(s) were taken while a /health/ready call was in flight — the bound assertion above cannot be trusted vacuously; need at least 3.");

            // Group N round 3 — this call previously had NO bound at all
            // (`gateway.Client.GetAsync(...)`, drained by a bare `await`);
            // an L27 re-arm exposed it, failing only at HttpClient's 100s
            // default rather than within this suite's own bound. Now
            // bounded via TryGetTimedAsync, asserted with the same
            // path-naming message the later poll uses.
            var (readyInFlight, readyInFlightElapsed) = await readyInFlightTask;
            Assert.True(
                readyInFlightElapsed <= _pausedReadinessBound,
                $"/health/ready took {readyInFlightElapsed.TotalMilliseconds:F0}ms while NATS was paused, exceeding its {_pausedReadinessBound.TotalSeconds:F0}s bound (design.md §8.3: 2 checks x 2s + 2s margin) — the initial in-flight call.");
            _ = readyInFlight; // the poll loop below re-derives and asserts the down body independently.

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            HealthResponseDto? downBody = null;
            while (DateTime.UtcNow < deadline)
            {
                var (pausedReady, readyElapsed) = await TryGetTimedAsync(gateway.Client, "/health/ready", _pausedReadinessBound);
                Assert.True(
                    readyElapsed <= _pausedReadinessBound,
                    $"/health/ready took {readyElapsed.TotalMilliseconds:F0}ms while NATS was paused, exceeding its {_pausedReadinessBound.TotalSeconds:F0}s bound (design.md §8.3: 2 checks x 2s + 2s margin).");
                if (pausedReady is not null && pausedReady.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    downBody = await pausedReady.Content.ReadFromJsonAsync<HealthResponseDto>();
                    break;
                }

                // Liveness must answer 200 THROUGHOUT this window — a
                // timed-out call here is a FAILURE (a hung `/health/live`
                // is exactly the defect "200 throughout" exists to catch,
                // and the one #7 shipped unbounded, design.md §8.3), never
                // skipped as "not observed".
                var (liveWhileDown, liveElapsed) = await TryGetTimedAsync(gateway.Client, "/health/live", _pausedReadinessBound);
                Assert.True(
                    liveWhileDown is not null && liveElapsed <= _pausedReadinessBound,
                    $"/health/live did not answer within {_pausedReadinessBound.TotalSeconds:F0}s while NATS was paused (elapsed {liveElapsed.TotalMilliseconds:F0}ms).");
                Assert.Equal(HttpStatusCode.OK, liveWhileDown!.StatusCode);

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            Assert.NotNull(downBody);
            Assert.Equal("down", downBody!.Status);
            Assert.Equal("down", downBody.Checks!["rpcTransport"].Status);
            Assert.Equal("up", downBody.Checks["readModel"].Status);

            var (liveWhilePaused, livePausedElapsed) = await TryGetTimedAsync(gateway.Client, "/health/live", _pausedReadinessBound);
            Assert.True(
                liveWhilePaused is not null && livePausedElapsed <= _pausedReadinessBound,
                $"/health/live did not answer within {_pausedReadinessBound.TotalSeconds:F0}s while NATS was paused (elapsed {livePausedElapsed.TotalMilliseconds:F0}ms).");
            Assert.Equal(HttpStatusCode.OK, liveWhilePaused!.StatusCode);
        }
        finally
        {
            await nats.UnpauseAsync();
        }

        var recoveredBody = await PollUntilStatusAsync(gateway.Client, "/health/ready", HttpStatusCode.OK, TimeSpan.FromSeconds(30));
        Assert.NotNull(recoveredBody);
        Assert.Equal("up", recoveredBody!.Status);
        Assert.All(recoveredBody.Checks!.Values, c => Assert.Equal("up", c.Status));
    }

    /// <summary>
    /// Review round 2, D7 — the ONLY test pausing a REAL Mongo container
    /// against the Gateway's own <c>MongoHealthCheck</c> copy.
    /// <c>Architecture.Tests</c>' structural parity facts
    /// (<c>HealthProbeCopyParityTests</c>, <c>HealthProbeTimeoutTests</c>)
    /// cannot prove the 2s bound is actually ENFORCED — only that a
    /// <see cref="TimeSpan"/> field with that value exists somewhere in the
    /// file: deleting <c>cts.CancelAfter(_timeout);</c> left the whole
    /// <c>Architecture.Tests</c> suite green (probe 13, review round 2).
    /// This closes the Gateway half of R60's stalled-container guarantee
    /// for <c>readModel</c>, the same way <c>Projector.IntegrationTests</c>'
    /// own <c>HealthProbesTests</c> already closes it for Projector's copy.
    /// </summary>
    [Fact]
    public async Task R60_OR6_ReportsReadModelDownWithinItsBound_WhileMongoIsPaused_AndRecoversWhenItReturns()
    {
        await using var gateway = await GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = nats.Url;
            options.Mongo.ConnectionUri = mongo.ConnectionString;
            options.Mongo.Database = $"otc_read_model_health_mongo_{Guid.NewGuid():N}";
        });

        var readyBody = await PollUntilStatusAsync(gateway.Client, "/health/ready", HttpStatusCode.OK, TimeSpan.FromSeconds(30));
        Assert.NotNull(readyBody);
        Assert.Equal("up", readyBody!.Status);

        await mongo.PauseAsync();
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            HealthResponseDto? downBody = null;
            while (DateTime.UtcNow < deadline)
            {
                var (pausedReady, readyElapsed) = await TryGetTimedAsync(gateway.Client, "/health/ready", _pausedReadinessBound);
                Assert.True(
                    readyElapsed <= _pausedReadinessBound,
                    $"/health/ready took {readyElapsed.TotalMilliseconds:F0}ms while Mongo was paused, exceeding its {_pausedReadinessBound.TotalSeconds:F0}s bound (design.md §8.3: 2 checks x 2s + 2s margin).");
                if (pausedReady is not null && pausedReady.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    downBody = await pausedReady.Content.ReadFromJsonAsync<HealthResponseDto>();
                    break;
                }

                var (liveWhileDown, liveElapsed) = await TryGetTimedAsync(gateway.Client, "/health/live", _pausedReadinessBound);
                Assert.True(
                    liveWhileDown is not null && liveElapsed <= _pausedReadinessBound,
                    $"/health/live did not answer within {_pausedReadinessBound.TotalSeconds:F0}s while Mongo was paused (elapsed {liveElapsed.TotalMilliseconds:F0}ms).");
                Assert.Equal(HttpStatusCode.OK, liveWhileDown!.StatusCode);

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            Assert.NotNull(downBody);
            Assert.Equal("down", downBody!.Status);
            Assert.Equal("down", downBody.Checks!["readModel"].Status);
            Assert.Equal("up", downBody.Checks["rpcTransport"].Status);
        }
        finally
        {
            await mongo.UnpauseAsync();
        }

        var recoveredBody = await PollUntilStatusAsync(gateway.Client, "/health/ready", HttpStatusCode.OK, TimeSpan.FromSeconds(30));
        Assert.NotNull(recoveredBody);
        Assert.Equal("up", recoveredBody!.Status);
        Assert.All(recoveredBody.Checks!.Values, c => Assert.Equal("up", c.Status));
    }
}
