using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Saga;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §6.4 — no-overlap self-scheduling and claim → dispatch →
/// reschedule, against a fake <see cref="ISagaCommandSweeper"/>
/// (<c>OutboxRelayLoopTests</c>'s own shape). No database, no host.
/// </summary>
public sealed class SagaCommandSweeperLoopTests
{
    [Fact]
    public async Task SweeperLoop_NeverStartsASecondCycleWhileOneIsStillInProgress()
    {
        var sweeper = new BlockingFakeSweeper();
        await using var provider = BuildProvider(sweeper, intervalMs: 20);
        var service = provider.GetRequiredService<SagaCommandSweeperBackgroundService>();

        await service.StartAsync(CancellationToken.None);

        await sweeper.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Assert.Equal(1, sweeper.CallCount);

        sweeper.ReleaseCurrentCall();
        await sweeper.SecondCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(sweeper.CallCount is 1 or 2, "at most one extra cycle should have started by the time the second call begins");

        sweeper.ReleaseCurrentCall();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SweeperLoop_ClaimsDispatchesAndReschedulesEveryCycle()
    {
        var sweeper = new CountingFakeSweeper();
        await using var provider = BuildProvider(sweeper, intervalMs: 20);
        var service = provider.GetRequiredService<SagaCommandSweeperBackgroundService>();

        await service.StartAsync(CancellationToken.None);

        await sweeper.ReachedCallCount(3).WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        Assert.True(sweeper.CallCount >= 3);
    }

    [Fact]
    public async Task SweeperLoop_DisabledNeverCallsTheSweeper()
    {
        var sweeper = new CountingFakeSweeper();
        await using var provider = BuildProvider(sweeper, intervalMs: 20, enabled: false);
        var service = provider.GetRequiredService<SagaCommandSweeperBackgroundService>();

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, sweeper.CallCount);
    }

    private static ServiceProvider BuildProvider(ISagaCommandSweeper sweeper, int intervalMs, bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sweeper);
        var options = new OrdersSagaOptions();
        options.Sweeper.IntervalMs = intervalMs;
        options.Sweeper.Enabled = enabled;
        services.AddSingleton<IOptions<OrdersSagaOptions>>(Options.Create(options));
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SagaCommandSweeperBackgroundService>>(NullLogger<SagaCommandSweeperBackgroundService>.Instance);
        services.AddSingleton<SagaCommandSweeperBackgroundService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Blocks each call until released, exposing signals for "a call has started" — <c>OutboxRelayLoopTests</c>'s own shape.</summary>
    private sealed class BlockingFakeSweeper : ISagaCommandSweeper
    {
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SagaSweepResult> RunOnceAsync(CancellationToken cancellationToken)
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
            return new SagaSweepResult(0, 0);
        }

        public void ReleaseCurrentCall()
        {
            var previous = _release;
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }

    /// <summary>Never blocks — completes each cycle instantly, so the loop reschedules freely; used to prove the loop re-enters repeatedly.</summary>
    private sealed class CountingFakeSweeper : ISagaCommandSweeper
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<SagaSweepResult> RunOnceAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new SagaSweepResult(1, 1));
        }

        /// <summary>Polls until <see cref="CallCount"/> reaches <paramref name="target"/> — a short, bounded poll is simpler and just as honest as an event-based wait for a loop already ticking every 20 ms.</summary>
        public async Task ReachedCallCount(int target)
        {
            while (CallCount < target)
            {
                await Task.Delay(10);
            }
        }
    }
}
