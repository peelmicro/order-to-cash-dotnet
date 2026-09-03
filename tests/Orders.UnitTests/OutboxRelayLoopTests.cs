using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OI6 — no infrastructure (design.md §9.1): <see cref="OutboxRelayBackgroundService"/>
/// depends on <see cref="IOutboxRelay"/>, resolved from DI, so a fake with
/// controllable timing proves the loop's own re-entry and drain behaviour
/// with no database and no host.
/// </summary>
public sealed class OutboxRelayLoopTests
{
    [Fact]
    public async Task OI6_RelayLoop_NeverStartsASecondPollCycleWhileOneIsStillInProgress()
    {
        var relay = new BlockingFakeOutboxRelay();
        await using var provider = BuildProvider(relay, pollIntervalMs: 20);
        var service = provider.GetRequiredService<OutboxRelayBackgroundService>();

        await service.StartAsync(CancellationToken.None);

        // The first cycle is in flight and blocked. Wait several poll
        // intervals — a re-entrant loop would have started a second cycle
        // by now.
        await relay.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Assert.Equal(1, relay.CallCount);

        relay.ReleaseCurrentCall();
        await relay.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A SECOND cycle only ever starts after the first one released —
        // never overlapping it.
        Assert.True(relay.CallCount is 1 or 2, "at most one extra cycle should have started by the time the second call begins");

        relay.ReleaseCurrentCall();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OI6_RelayLoop_StopAsyncWaitsForTheInFlightCycleToFinish()
    {
        var relay = new BlockingFakeOutboxRelay();
        await using var provider = BuildProvider(relay, pollIntervalMs: 20);
        var service = provider.GetRequiredService<OutboxRelayBackgroundService>();

        await service.StartAsync(CancellationToken.None);
        await relay.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopTask = service.StopAsync(CancellationToken.None);

        // StopAsync must NOT complete while the cycle is still blocked.
        var completedEarly = await Task.WhenAny(stopTask, Task.Delay(300)) == stopTask;
        Assert.False(completedEarly, "StopAsync completed before the in-flight cycle finished");

        relay.ReleaseCurrentCall();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static ServiceProvider BuildProvider(BlockingFakeOutboxRelay relay, int pollIntervalMs)
    {
        var services = new ServiceCollection();
        services.AddSingleton(relay);
        services.AddScoped<IOutboxRelay>(sp => sp.GetRequiredService<BlockingFakeOutboxRelay>());
        services.Configure<OutboxRelayOptions>(o => o.PollIntervalMs = pollIntervalMs);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<OutboxRelayBackgroundService>>(NullLogger<OutboxRelayBackgroundService>.Instance);
        services.AddSingleton<OutboxRelayBackgroundService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Blocks each call to <see cref="RunOnceAsync"/> until released, and exposes signals for "a call has started" so a test can observe re-entry without racing on timing.</summary>
    private sealed class BlockingFakeOutboxRelay : IOutboxRelay
    {
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                FirstCallStarted.TrySetResult();
            }
            else if (CallCount == 2)
            {
                SecondCallStarted.TrySetResult();
            }

            var myRelease = _release;
            await myRelease.Task;
            return new OutboxRelayResult(0, 0);
        }

        public void ReleaseCurrentCall()
        {
            var previous = _release;
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }
}
