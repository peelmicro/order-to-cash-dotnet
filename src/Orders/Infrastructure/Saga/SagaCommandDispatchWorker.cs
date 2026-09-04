using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// Drains the in-process signal channel (design.md §5.5) — the fast path.
/// One <see cref="IServiceScope"/> per item, so the RPC issue and SO4's
/// retries happen OFF the Kafka consume loop (SO10); a failing dispatch is
/// logged and the loop continues, because the durable <c>saga_commands</c>
/// row plus <see cref="SagaCommandSweeper"/> — not this worker — is the
/// guarantee.
/// </summary>
public sealed class SagaCommandDispatchWorker(
    ChannelSagaCommandSignal signal,
    IServiceScopeFactory scopeFactory,
    ILogger<SagaCommandDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
