using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// Drains the in-process signal channel (design.md §5.5) — the fast path.
/// Runs <see cref="OrdersSagaDispatchOptions.DegreeOfParallelism"/> PARALLEL
/// consumer loops over the SAME <see cref="ChannelSagaCommandSignal"/>
/// (backlog id 80): before this fix, ONE sequential loop awaited every
/// <c>dispatcher.DispatchAsync</c> in turn, so a single slow or absent
/// responder stalled every other order's fast-path dispatch behind it for
/// up to the ~16.5s worst case (<c>OrdersSagaCommandOptions</c>'s own
/// header) — the exact defect design.md §5.5's own argument against
/// dispatching inline in the fact handler describes ("every subsequent fact
/// would queue behind a dead responder"), moved one stage later rather than
/// avoided. Each loop is otherwise unchanged from the pre-fix worker: one
/// <see cref="IServiceScope"/> per item, so the RPC issue and SO4's retries
/// happen OFF the Kafka consume loop (SO10); a failing dispatch is logged
/// and that loop continues, because the durable <c>saga_commands</c> row
/// plus <see cref="SagaCommandSweeper"/> — not this worker — is the
/// guarantee.
/// </summary>
/// <remarks>
/// <para>
/// No per-order affinity/serialisation: <see cref="OrdersSagaDispatchOptions"/>'s
/// own doc comment enumerates why no invariant in this saga needs it, and
/// progress/impl_saga_command_fast_path_is_head_of_line_blocked.md records
/// the enumeration in full.
/// </para>
/// <para>
/// <b>The <c>Math.Max(1, ...)</c> floor below is load-bearing and guarded</b>
/// (backlog id 90). Without it, a <c>DegreeOfParallelism</c> of 0 makes
/// <c>Enumerable.Range(0, 0)</c> empty, so <c>Task.WhenAll</c> has nothing to
/// wait for and settles within milliseconds: this
/// <see cref="BackgroundService"/> finishes SUCCESSFULLY, the Generic Host
/// keeps running, <c>/health/ready</c> still answers <c>200 "up"</c> (no
/// health check references this worker), and the fast path dispatches nothing
/// for any order, forever — every saga command silently demoted to the 30 s
/// sweeper. A NEGATIVE value behaves differently and is worth knowing apart:
/// <see cref="Enumerable.Range"/>'s <c>ArgumentOutOfRangeException</c> is
/// captured INTO the returned task rather than thrown out of
/// <c>ExecuteAsync</c> (<c>Task.WhenAll(IEnumerable&lt;Task&gt;)</c>
/// enumerates asynchronously in .NET 10), so the task FAULTS instead of
/// completing — loud under a real host's default
/// <c>BackgroundServiceExceptionBehavior.StopHost</c>, where 0 is not. The
/// clamp was measured unguarded: deleting it left all four of this worker's
/// tests passing (id 80's review, probe P6). It is now armed by
/// <c>SagaCommandDispatchWorkerTests.DegreeOfParallelismBelowOne_IsClampedToOneRunningConsumerLoop</c>
/// and by
/// <c>SagaCommandDispatchWorkerTests.DegreeOfParallelismZero_MustNotLeaveExecuteAsyncCompletedWhileTheHostStaysUpAndHealthy</c>.
/// The floor is the SECOND line of defence, not the first:
/// <c>OrdersSagaServiceCollectionExtensions.AddOrdersSaga</c> throws at
/// composition time for a configured value below 1 (CLAUDE.md's "loud at
/// boot"), and this floor covers the paths that never go through that
/// composition root.
/// </para>
/// </remarks>
public sealed class SagaCommandDispatchWorker(
    ChannelSagaCommandSignal signal,
    IServiceScopeFactory scopeFactory,
    IOptions<OrdersSagaOptions> options,
    ILogger<SagaCommandDispatchWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var degreeOfParallelism = Math.Max(1, options.Value.Dispatch.DegreeOfParallelism);

        var loops = Enumerable.Range(0, degreeOfParallelism)
            .Select(_ => ConsumeLoopAsync(stoppingToken));

        return Task.WhenAll(loops);
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var commandRef in signal.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            using var scope = scopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<ISagaCommandDispatcher>();

            try
            {
                await dispatcher.DispatchAsync(commandRef.OrderId, commandRef.Command, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The failure is already durable (the row is pending/parked
                // in saga_commands) — stopping the worker would only delay
                // recovery, exactly OutboxRelayBackgroundService's own stance.
                logger.LogError(
                    ex,
                    "Saga command dispatch failed for order {OrderId}, command {Command}; the durable row remains for the sweeper.",
                    commandRef.OrderId,
                    commandRef.Command);
            }
        }
    }
}
