using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// The poll loop and graceful drain — design.md §5.4. Owns the interval, the
/// DI scope per cycle and shutdown; <see cref="OutboxRelay"/> itself stays a
/// plain class with no host dependency (design.md §2.2).
/// </summary>
public sealed class OutboxRelayBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxRelayOptions> options,
    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMs));

        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
        // the next one rather than stacking — OI6 is satisfied by
        // construction: one loop, one await per cycle, no second caller.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // One DI scope per cycle, disposed at the end (design.md §2.1's
            // price, paid here) — the OrdersDbContext and its change
            // tracker are fresh every cycle.
            using var scope = scopeFactory.CreateScope();
            var relay = scope.ServiceProvider.GetRequiredService<IOutboxRelay>();

            try
            {
                await relay.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The failure is already durable — nothing was stamped — so
                // stopping the relay would only delay recovery.
                logger.LogError(ex, "Outbox relay poll cycle failed; will retry on the next tick.");
            }
        }
    }
}
