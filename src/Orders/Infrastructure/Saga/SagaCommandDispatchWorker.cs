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
/// No per-order affinity/serialisation: <see cref="OrdersSagaDispatchOptions"/>'s
/// own doc comment enumerates why no invariant in this saga needs it, and
/// progress/impl_saga_command_fast_path_is_head_of_line_blocked.md records
/// the enumeration in full.
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
