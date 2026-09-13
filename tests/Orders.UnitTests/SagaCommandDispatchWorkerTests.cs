using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
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

    /// <summary>
    /// Backlog id 80 — this test's own claim ("one failing item does not
    /// stop the worker") never depended on cross-item ORDER, only on both
    /// items eventually being processed; the exact-sequence assertion
    /// ([first, second]) was incidental to the pre-fix single-sequential-
    /// worker implementation. Pinned to <c>DegreeOfParallelism = 1</c> here,
    /// deliberately, so this test keeps proving exactly what it always
    /// proved — a failing dispatch does not stop the NEXT one on the SAME
    /// loop — without weakening it into a membership check that would also
    /// pass under a broken worker. The new parallelism itself is proven by
    /// <see cref="HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch"/>
    /// below, at its production default.
    /// </summary>
    [Fact]
    public async Task OneFailingItem_DoesNotStopTheWorker()
    {
        var signal = new ChannelSagaCommandSignal(NullLogger<ChannelSagaCommandSignal>.Instance);
        var dispatcher = new RecordingFakeDispatcher { ThrowOnFirst = true };
        await using var provider = BuildProvider(signal, dispatcher, degreeOfParallelism: 1);
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

    /// <summary>
    /// Backlog id 80's own reproduction — FAILS on the pre-fix single
    /// sequential worker (verbatim failure recorded in
    /// progress/impl_saga_command_fast_path_is_head_of_line_blocked.md).
    /// Order A's responder never answers: <see cref="PerOrderGatedFakeDispatcher"/>
    /// blocks A's <c>DispatchAsync</c> call FOREVER (never released inside
    /// this test), simulating the worst case OrdersSagaCommandOptions.cs's
    /// own header names (~16.5s) with a bound far shorter than even that —
    /// this test bounds order B at 2s, so it proves the property rather
    /// than merely outrunning a slow-but-bounded delay. Order B's command is
    /// signalled AFTER order A's, while A is confirmed still blocked (never
    /// a race left to chance — <c>Started(orderA)</c> is awaited first). At
    /// the PRODUCTION default (<c>DegreeOfParallelism = 8</c>, unset here),
    /// order B must reach dispatched well inside the 2s bound.
    /// </summary>
    [Fact]
    public async Task HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch()
    {
        var signal = new ChannelSagaCommandSignal(NullLogger<ChannelSagaCommandSignal>.Instance);
        var dispatcher = new PerOrderGatedFakeDispatcher();
        await using var provider = BuildProvider(signal, dispatcher);
        var worker = provider.GetRequiredService<SagaCommandDispatchWorker>();

        await worker.StartAsync(CancellationToken.None);

        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var commandA = new SagaCommandRef(orderA, SagaCommandKind.DespatchCreate);
        var commandB = new SagaCommandRef(orderB, SagaCommandKind.StockReserve);

        dispatcher.BlockForever(orderA);

        try
        {
            signal.Signal(commandA);
            await dispatcher.Started(orderA).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dispatcher.Completed(orderA).IsCompleted, "order A's dispatch must genuinely still be blocked before order B is signalled.");

            signal.Signal(commandB);

            var bound = TimeSpan.FromSeconds(2);
            try
            {
                await dispatcher.Completed(orderB).WaitAsync(bound);
            }
            catch (TimeoutException)
            {
                Assert.Fail(
                    $"order B's command {commandB.Command} for order {commandB.OrderId} did not reach dispatched within the {bound} bound — " +
                    $"the fast path's dispatch worker is head-of-line blocked behind order {orderA}'s unresponsive dispatch (backlog id 80).");
            }
        }
        finally
        {
            // Order A's gate is NEVER released for the purpose of the
            // assertions above (that is the whole point) — but it IS
            // released here, before teardown, so the loop still awaiting
            // it can actually return. Without this, BackgroundService.StopAsync(CancellationToken.None)
            // awaits ExecuteAsync's Task.WhenAll forever, since one of its
            // parallel loops would never unblock — observed live while
            // arming this test (the confirming green run hung until this
            // fix was added).
            dispatcher.Release(orderA);
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// Bullet 3's "bounded parallelism" is itself a countable claim, not
    /// only the head-of-line reproduction's absence-of-blocking above — a
    /// worker that fired one unbounded task per signalled item would ALSO
    /// pass that test, so this proves the bound is real on both sides: with
    /// <c>DegreeOfParallelism = 2</c> and six orders signalled at once,
    /// observed concurrency never exceeds 2 (the bound holds) AND reaches
    /// exactly 2 at least once (parallelism is genuine, not accidentally
    /// serial) — so this test would fail against BOTH the pre-fix single
    /// worker (never reaches 2) and a hypothetical unbounded fix (would
    /// exceed 2).
    /// </summary>
    [Fact]
    public async Task DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders()
    {
        var signal = new ChannelSagaCommandSignal(NullLogger<ChannelSagaCommandSignal>.Instance);
        var dispatcher = new ConcurrencyTrackingFakeDispatcher();
        const int degree = 2;
        await using var provider = BuildProvider(signal, dispatcher, degreeOfParallelism: degree);
        var worker = provider.GetRequiredService<SagaCommandDispatchWorker>();

        await worker.StartAsync(CancellationToken.None);

        var orders = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var orderId in orders)
        {
            signal.Signal(new SagaCommandRef(orderId, SagaCommandKind.StockReserve));
        }

        // Give every reader loop a chance to pick up an item and reach the
        // gate — a fixed pacing delay, not a race: DispatchAsync below does
        // no I/O before incrementing the concurrency counter, so this is
        // comfortably long enough for every loop to have started.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        dispatcher.ReleaseAll();
        await Task.WhenAll(orders.Select(o => dispatcher.Completed(o))).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            dispatcher.MaxObservedConcurrency <= degree,
            $"observed {dispatcher.MaxObservedConcurrency} concurrent dispatches with DegreeOfParallelism={degree} — the worker exceeded its own configured bound.");
        Assert.True(
            dispatcher.MaxObservedConcurrency >= 2,
            $"observed only {dispatcher.MaxObservedConcurrency} concurrent dispatch(es) — this run never demonstrated genuine parallelism, so the <= bound above would trivially pass even against the pre-fix sequential worker.");

        await worker.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(ChannelSagaCommandSignal signal, ISagaCommandDispatcher dispatcher, int? degreeOfParallelism = null)
    {
        var sagaOptions = new OrdersSagaOptions();
        if (degreeOfParallelism is { } degree)
        {
            sagaOptions.Dispatch.DegreeOfParallelism = degree;
        }

        var services = new ServiceCollection();
        services.AddSingleton(signal);
        services.AddScoped(_ => dispatcher);
        services.AddSingleton<ILogger<SagaCommandDispatchWorker>>(NullLogger<SagaCommandDispatchWorker>.Instance);
        services.AddSingleton<IOptions<OrdersSagaOptions>>(Options.Create(sagaOptions));
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

    /// <summary>
    /// Backlog id 80's reproduction fixture — a per-order-id gate, so ONE
    /// order's dispatch can be made to block forever while a DIFFERENT
    /// order's dispatch is observed to complete (or not) within a bound.
    /// Thread-safe: multiple orders can be in flight concurrently, which is
    /// exactly the property under test.
    /// </summary>
    private sealed class PerOrderGatedFakeDispatcher : ISagaCommandDispatcher
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource> _started = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource> _completed = new();

        public void BlockForever(Guid orderId) => Gate(orderId);

        /// <summary>Teardown-only — releases a blocked order's gate so the worker's own loop can return. Never called before this test's assertions run; see the caller's own comment.</summary>
        public void Release(Guid orderId) => Gate(orderId).TrySetResult();

        public Task Started(Guid orderId) => StartedSource(orderId).Task;

        public Task Completed(Guid orderId) => CompletedSource(orderId).Task;

        public async Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
        {
            StartedSource(orderId).TrySetResult();

            if (_gates.TryGetValue(orderId, out var gate))
            {
                await gate.Task.ConfigureAwait(false);
            }

            CompletedSource(orderId).TrySetResult();
        }

        public Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken) => throw new NotSupportedException();

        private TaskCompletionSource Gate(Guid orderId) => _gates.GetOrAdd(orderId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        private TaskCompletionSource StartedSource(Guid orderId) => _started.GetOrAdd(orderId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        private TaskCompletionSource CompletedSource(Guid orderId) => _completed.GetOrAdd(orderId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <summary>
    /// Backlog id 80's boundedness fixture — every <see cref="DispatchAsync"/>
    /// call increments a shared counter on entry, records the running
    /// maximum, blocks on ONE shared release gate, then decrements on the
    /// way out. Lock-free (<see cref="Interlocked"/>), so it never itself
    /// becomes a point of serialisation that would understate concurrency.
    /// </summary>
    private sealed class ConcurrencyTrackingFakeDispatcher : ISagaCommandDispatcher
    {
        private readonly TaskCompletionSource _releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource> _completed = new();
        private int _current;
        private int _maxObserved;

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObserved);

        public void ReleaseAll() => _releaseGate.TrySetResult();

        public Task Completed(Guid orderId) => CompletedSource(orderId).Task;

        public async Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
        {
            var concurrent = Interlocked.Increment(ref _current);
            RecordMax(concurrent);

            try
            {
                await _releaseGate.Task.ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }

            CompletedSource(orderId).TrySetResult();
        }

        public Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken) => throw new NotSupportedException();

        private void RecordMax(int candidate)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref _maxObserved);
                if (candidate <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxObserved, candidate, observed) != observed);
        }

        private TaskCompletionSource CompletedSource(Guid orderId) => _completed.GetOrAdd(orderId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
