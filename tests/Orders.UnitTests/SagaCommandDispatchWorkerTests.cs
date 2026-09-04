using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Saga;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.5 — <c>SO10_TheConsumeLoopReturnsBeforeTheRpcIssueCompletes</c>:
/// signalling hands off and returns before the RPC issue completes, and one
/// failing item does not stop the worker. No database, no real NATS —
/// against a fake <see cref="ISagaCommandDispatcher"/>.
/// </summary>
public sealed class SagaCommandDispatchWorkerTests
{
    [Fact]
    public async Task SO10_TheConsumeLoopReturnsBeforeTheRpcIssueCompletes()
    {
        var signal = new ChannelSagaCommandSignal(NullLogger<ChannelSagaCommandSignal>.Instance);
        var dispatcher = new BlockingFakeDispatcher();
        await using var provider = BuildProvider(signal, dispatcher);
        var worker = provider.GetRequiredService<SagaCommandDispatchWorker>();

        await worker.StartAsync(CancellationToken.None);

        var commandRef = new SagaCommandRef(Guid.NewGuid(), SagaCommandKind.StockReserve);

        // Signal is synchronous and returns immediately (SO10) — the call
        // itself never awaits the dispatch.
        signal.Signal(commandRef);

        await dispatcher.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The dispatch is genuinely still blocked at this point.
        Assert.False(dispatcher.Completed.Task.IsCompleted);

        dispatcher.ReleaseGate();
        await dispatcher.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(commandRef, dispatcher.LastDispatched);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneFailingItem_DoesNotStopTheWorker()
    {
        var signal = new ChannelSagaCommandSignal(NullLogger<ChannelSagaCommandSignal>.Instance);
        var dispatcher = new RecordingFakeDispatcher { ThrowOnFirst = true };
        await using var provider = BuildProvider(signal, dispatcher);
        var worker = provider.GetRequiredService<SagaCommandDispatchWorker>();

        await worker.StartAsync(CancellationToken.None);

        var first = new SagaCommandRef(Guid.NewGuid(), SagaCommandKind.StockReserve);
        var second = new SagaCommandRef(Guid.NewGuid(), SagaCommandKind.CreditHold);

        signal.Signal(first);
        signal.Signal(second);

        await dispatcher.SecondCallCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([first, second], dispatcher.Calls);

        await worker.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(ChannelSagaCommandSignal signal, ISagaCommandDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(signal);
        services.AddScoped(_ => dispatcher);
        services.AddSingleton<ILogger<SagaCommandDispatchWorker>>(NullLogger<SagaCommandDispatchWorker>.Instance);
        services.AddSingleton<SagaCommandDispatchWorker>();
        return services.BuildServiceProvider();
    }

    private sealed class BlockingFakeDispatcher : ISagaCommandDispatcher
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SagaCommandRef? LastDispatched { get; private set; }

        public async Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
        {
            LastDispatched = new SagaCommandRef(orderId, command);
            CallStarted.TrySetResult();
            await _gate.Task;
            Completed.TrySetResult();
        }

        public Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void ReleaseGate() => _gate.TrySetResult();
    }

    private sealed class RecordingFakeDispatcher : ISagaCommandDispatcher
    {
        public List<SagaCommandRef> Calls { get; } = [];

        public bool ThrowOnFirst { get; set; }

        public TaskCompletionSource SecondCallCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
        {
            var commandRef = new SagaCommandRef(orderId, command);
            Calls.Add(commandRef);

            if (Calls.Count == 1 && ThrowOnFirst)
            {
                throw new InvalidOperationException("Simulated dispatch failure.");
            }

            if (Calls.Count == 2)
            {
                SecondCallCompleted.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
