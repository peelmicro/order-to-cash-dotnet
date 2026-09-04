using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The poll loop and graceful drain — <c>OutboxRelayBackgroundService</c>'s
/// own shape, copied because it is already reviewed (design.md §6.4).
/// <see cref="SagaCommandSweeper"/> itself stays a plain class with no host
/// dependency.
/// </summary>
public sealed class SagaCommandSweeperBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OrdersSagaOptions> options,
    ILogger<SagaCommandSweeperBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Sweeper.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.Sweeper.IntervalMs));

        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
        // the next one rather than stacking — one loop, one await per cycle,
        // no second caller (OutboxRelayBackgroundService's own guarantee).
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<ISagaCommandSweeper>();

            try
            {
                await sweeper.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Saga command sweep cycle failed; will retry on the next tick.");
            }
        }
    }
}
