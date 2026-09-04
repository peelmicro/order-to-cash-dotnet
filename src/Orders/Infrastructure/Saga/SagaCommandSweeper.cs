using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>Claimed vs. dispatched, so a cycle with nothing due is distinguishable from one where a dispatch attempt itself failed.</summary>
public sealed record SagaSweepResult(int Claimed, int Dispatched);

/// <summary>
/// The one member <see cref="SagaCommandSweeperBackgroundService"/> depends
/// on — resolved from DI, so <c>SagaCommandSweeperLoopTests</c> can
/// substitute a fake and prove the loop's own re-entry/reschedule behaviour
/// with no database (<c>IOutboxRelay</c>/<c>OutboxRelayLoopTests</c>'s own shape).
/// </summary>
public interface ISagaCommandSweeper
{
    Task<SagaSweepResult> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One sweep cycle (design.md §6.4) — the durability backstop for SO3's
/// crash window and SO5's park-and-retry. Claims the due batch, then
/// dispatches each row OUTSIDE the claiming transaction through
/// <see cref="ISagaCommandDispatcher.DispatchClaimedAsync"/> — called
/// DIRECTLY, never through <see cref="OrderToCash.Cqrs.IDispatcher"/> and
/// never through <see cref="ISagaCommandSignal"/>, because the sweeper must
/// not depend on the layer it exists to back up.
/// </summary>
public sealed class SagaCommandSweeper(
    ISagaCommandStore store,
    ISagaCommandDispatcher dispatcher,
    IOptions<OrdersSagaOptions> options,
    ILogger<SagaCommandSweeper> logger) : ISagaCommandSweeper
{
    public async Task<SagaSweepResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await store.ClaimDueAsync(options.Value.Sweeper.BatchSize, cancellationToken).ConfigureAwait(false);

        var dispatched = 0;

        foreach (var row in claimed)
        {
            try
            {
                await dispatcher.DispatchClaimedAsync(row, cancellationToken).ConfigureAwait(false);
                dispatched++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Failures here are already durable (the row stays
                // pending/parked) — logged and retried on the next tick,
                // matching the outbox relay's own stance.
                logger.LogError(
                    ex,
                    "Saga command sweep failed to dispatch order {OrderId}, command {Command}; retried on the next tick.",
                    row.OrderId,
                    row.Command);
            }
        }

        return new SagaSweepResult(claimed.Count, dispatched);
    }
}
