using System.Diagnostics;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// A4e addendum round 2, item 2 — a BEHAVIOURAL guard for the
/// <c>TaskCreationOptions.LongRunning</c> production fix (A4e addendum
/// round 1): <c>IAdminClient.GetMetadata</c> is SYNCHRONOUS, and calling it
/// directly on the method that returns <c>CheckAsync</c>'s <c>Task</c>
/// blocks whichever ThreadPool thread invoked it for up to the check's own
/// 2s bound — which, under concurrent <c>/health/ready</c> load, starves
/// the SAME pool <c>/health/live</c> needs for dispatch (measured, A4e
/// addendum round 1: up to 4005ms against a near-instant baseline). This
/// test proves the fix DIRECTLY: the <c>Task</c> that <c>CheckAsync</c>
/// returns must come back promptly and INCOMPLETE, proving the blocking
/// call is running on its own dedicated thread rather than the caller's.
/// </summary>
public sealed class KafkaHealthCheckLongRunningTests
{
    /// <summary>
    /// 192.0.2.1 — TEST-NET-1 (RFC 5737), reserved for documentation and
    /// never routed on the public internet or in this container network:
    /// a connect attempt is BLACK-HOLED (no SYN-ACK, no RST) rather than
    /// immediately refused, so it genuinely blocks for the check's own
    /// SocketTimeoutMs/_timeout bound. A refused LOCALHOST port would fail
    /// near-instantly with ECONNREFUSED instead and would not exercise the
    /// "blocks synchronously for ~2s" shape this test is about. Measured
    /// directly against this exact address (Orders.UnitTests' own sibling
    /// copy of this file, run first): the awaited <c>CheckAsync</c> call
    /// took ~2s end to end via the health check's own 2s bound — confirming
    /// the address genuinely blocks rather than failing fast.
    /// </summary>
    private const string BlackHoledBootstrapServers = "192.0.2.1:9092";

    [Fact]
    public async Task RunsTheBlockingGetMetadataCallOffTheCallingThread_SoCheckAsyncReturnsAPendingTaskPromptly()
    {
        using var check = new KafkaHealthCheck(Options.Create(new HealthOptions { KafkaBootstrapServers = BlackHoledBootstrapServers }));

        var stopwatch = Stopwatch.StartNew();
        var task = check.CheckAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
            $"CheckAsync itself took {stopwatch.Elapsed.TotalMilliseconds:F0}ms to RETURN its Task — the synchronous GetMetadata call is blocking the CALLING thread instead of running on its own dedicated (LongRunning) thread.");
        Assert.False(
            task.IsCompleted,
            "CheckAsync's returned Task was already completed the instant it was returned — the blocking GetMetadata call ran synchronously on the calling thread rather than on a dedicated thread.");

        var result = await task;
        Assert.False(result.IsUp, "the health check reported up against an unreachable, black-holed bootstrap address.");
    }
}
