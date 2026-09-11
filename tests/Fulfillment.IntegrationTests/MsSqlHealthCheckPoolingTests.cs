using System.Diagnostics;
using Microsoft.Extensions.Options;
using OrderToCash.Fulfillment.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// A4e addendum round 2, item 1 — a BEHAVIOURAL guard for the
/// <c>Pooling=false</c> production fix (A4e addendum round 1). Without it,
/// a pooled PHYSICAL connection warmed while MS-SQL was healthy is silently
/// reused after MS-SQL is paused, and a query on that reused connection can
/// wait on the attention acknowledgement past its own
/// <c>CancelAfter(_timeout)</c> — observed live as a hang that outlasted
/// 500+ SECONDS with the cancellation token never taking effect. This test
/// proves the fix by measured behaviour against a real, paused container —
/// never by reading the connection string.
///
/// This project's <c>MsSqlContainerFixture</c> is the only one of the four
/// MS-SQL fixtures in this repository exposing <c>PauseAsync</c>/
/// <c>UnpauseAsync</c> (added for <c>HealthProbesTests</c>, A4e), which is
/// why this guard lives in <c>Fulfillment.IntegrationTests</c> rather than
/// being duplicated across all four services that carry the fix.
/// </summary>
[Collection(FulfillmentCollection.Name)]
public sealed class MsSqlHealthCheckPoolingTests(MsSqlContainerFixture mssql)
{
    /// <summary>
    /// <c>MsSqlHealthCheck</c>'s own <c>_timeout</c> is 2s. The margin here
    /// is a further 1s (3s total): the round-1 measurement of the FIXED
    /// (<c>Pooling=false</c>) behaviour showed a tight, consistent window —
    /// 20 consecutive paused calls at min 2000ms / median 2000ms / max
    /// 2001ms — so 1s comfortably clears that measured jitter (CancelAfter's
    /// own scheduling granularity, the `SqlException` catch, and the
    /// `Task.WhenAny` continuation) while staying far below the unbounded
    /// hang (500+ SECONDS, observed, never resolved on its own) a pooled
    /// regression reintroduces — so this bound cannot be satisfied by
    /// accident by the regressed shape.
    /// </summary>
    private static readonly TimeSpan _bound = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task NeverReusesAPooledPhysicalConnectionAfterTheServerGoesUnresponsive_SoCheckAsyncResolvesWithinItsOwnBoundEveryTime()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_pooling_guard_{Guid.NewGuid():N}");
        var check = new MsSqlHealthCheck(Options.Create(new HealthOptions { ConnectionString = connectionString }));

        // Warm a pooled PHYSICAL connection while the server is healthy —
        // exactly the condition ledger issue (ii) names, and the condition
        // that reproduced the 500+s hang when `Pooling=false` was absent.
        var warm = await check.CheckAsync(CancellationToken.None);
        Assert.True(warm.IsUp, $"the warm-up call itself reported down: {warm.Detail}");

        await mssql.PauseAsync();
        try
        {
            for (var i = 1; i <= 3; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                var checkTask = check.CheckAsync(CancellationToken.None);
                var winner = await Task.WhenAny(checkTask, Task.Delay(_bound));
                stopwatch.Stop();

                Assert.True(
                    winner == checkTask,
                    $"MsSqlHealthCheck.CheckAsync call #{i} did not return within {_bound.TotalMilliseconds:F0}ms of MS-SQL being paused (still running at {stopwatch.Elapsed.TotalMilliseconds:F0}ms) — a pooled connection reused against an unresponsive server can hang far longer than its own stated timeout.");

                var result = await checkTask;
                Assert.False(result.IsUp, $"call #{i} reported up while MS-SQL was paused.");
            }
        }
        finally
        {
            await mssql.UnpauseAsync();
        }
    }
}
