// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// The poll loop and graceful drain. Owns the interval, the DI scope per
/// cycle and shutdown; <see cref="OutboxRelay"/> itself stays a plain class
/// with no host dependency.
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

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopeFactory.CreateScope();
            var relay = scope.ServiceProvider.GetRequiredService<IOutboxRelay>();

            try
            {
                await relay.RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox relay poll cycle failed; will retry on the next tick.");
            }
        }
    }
}
