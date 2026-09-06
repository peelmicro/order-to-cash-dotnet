using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Presentation;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// Backlog id 50, `BC22`, ledger `L22` — <c>StopAsync</c> waits for EVERY
/// in-flight request individually and never lets one faulted task abort
/// host shutdown. Drives the private <c>_inFlight</c> tracking dictionary
/// directly via reflection — the only seam that reaches <c>StopAsync</c>'s
/// own logic without a real NATS connection, the
/// <c>StockResponderShutdownTests</c> instrument.
/// </summary>
public sealed class CreditResponderShutdownTests
{
    [Fact]
    public async Task BC22_CompletesShutdownWaitingForEveryInFlightRequest_WhenOneFaultsAndOneSucceeds()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();

        var responder = new BillingRpcResponder(
            connection: null!,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BillingResponderOptions()),
            NullLogger<BillingRpcResponder>.Instance);

        var healthyRan = false;
        var healthySignal = new TaskCompletionSource();
        var healthyTask = Task.Run(async () =>
        {
            await Task.Delay(20);
            healthyRan = true;
            healthySignal.SetResult();
        });
        var faultingTask = Task.Run(() => throw new InvalidOperationException("simulated reply failure"));

        var inFlightField = typeof(BillingRpcResponder).GetField("_inFlight", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inFlight = (ConcurrentDictionary<Task, byte>)inFlightField.GetValue(responder)!;
        inFlight.TryAdd(healthyTask, 0);
        inFlight.TryAdd(faultingTask, 0);

        // StopAsync must complete without throwing, AND the healthy task
        // must have observably run to completion before it returns — both
        // halves of BC22.
        await responder.StopAsync(CancellationToken.None);

        Assert.True(healthyRan);
        await healthySignal.Task;
    }
}
