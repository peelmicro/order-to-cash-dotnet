using System.Collections.Concurrent;
using NATS.Client.Core;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 62 (SA-4) — a real, ARBITRATING stand-in for Fulfillment's
/// TWO saga responders that contest the SAME stock reservation
/// (<c>specs/shared/saga.md</c> §4.3, "The despatch already requested"):
/// <c>stock.release</c> and <c>despatch.create</c>, sharing ONE
/// reservation state (<c>reserved</c> / <c>released</c> / <c>consumed</c>)
/// under ONE lock, mirroring #8's real <c>IStockItemRepository.LockForOrderAsync</c>
/// arbitration (the ported-idiom ledger row in this feature's own
/// <c>progress/impl_operator_cancel_races_saga_forward_progress.md</c>).
/// Every OTHER saga command (<c>stock.reserve</c>, <c>credit.hold</c>,
/// <c>credit.release</c>, <c>invoice.issue</c>) is answered by the
/// EXISTING, non-arbitrating <see cref="StandInSagaResponders"/> factories
/// — this class exists only for the two that race.
/// </summary>
internal sealed class RecordingFulfillmentStandIn : IAsyncDisposable
{
    private enum Reservation
    {
        Reserved,
        Released,
        Consumed,
    }

    /// <summary>A payload no real request ever produces — <see cref="StandInRpcResponder{TRequest,TReply}"/>'s own precedent.</summary>
    private static readonly byte[] _probeMarker = "\"__otc_fulfillment_standin_probe__\""u8.ToArray();

    private readonly object _gate = new();
    private Reservation _reservation = Reservation.Reserved;
    private readonly ConcurrentQueue<string> _commandsProcessed = new();
    private readonly ConcurrentQueue<string> _stockReleaseOutcomes = new();

    private readonly INatsConnection _connection;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _stockReleaseLoop;
    private readonly Task _despatchCreateLoop;

    /// <summary>
    /// The ordering barrier <c>Confirmed_ReleaseWins</c> uses (CLAUDE.md:
    /// "a barrier or a controlled release, never repetition") — the
    /// <c>despatch.create</c> handler awaits this BEFORE evaluating or
    /// mutating the shared reservation state, so a test can force
    /// <c>stock.release</c>'s own already-received request to be applied
    /// first, deterministically. Starts OPEN — ordinary use (both other
    /// tests in this suite) needs no gate at all.
    /// </summary>
    public TestGate HoldDespatchCreateGate { get; } = new();

    /// <summary>The commands actually PROCESSED (reservation state evaluated and, where legal, mutated), in order — never merely "received over the wire": a request blocked on <see cref="HoldDespatchCreateGate"/> has not yet been processed.</summary>
    public IReadOnlyList<string> CommandsProcessed => [.. _commandsProcessed];

    /// <summary>
    /// Review round 1, A4 — bullet 6's "stock.release releases nothing" is
    /// otherwise unobservable: <see cref="CommandsProcessed"/> records which
    /// commands ran, not what they returned. Each <c>stock.release</c>
    /// reply's own <c>outcome</c> field ("released" the first time the
    /// reservation is released, "already_released" every time after,
    /// mirroring <c>OrderStockReservation.Release</c>'s own no-op), in
    /// order.
    /// </summary>
    public IReadOnlyList<string> StockReleaseOutcomes => [.. _stockReleaseOutcomes];

    private RecordingFulfillmentStandIn(INatsConnection connection)
    {
        _connection = connection;
        _stockReleaseLoop = RunAsync(RpcSubjects.StockRelease, HandleStockReleaseAsync, _cts.Token);
        _despatchCreateLoop = RunAsync(RpcSubjects.DespatchCreate, HandleDespatchCreateAsync, _cts.Token);
    }

    /// <summary>Starts the stand-in and blocks until BOTH real round-trip probes confirm both subscriptions are live — <see cref="StandInRpcResponder{TRequest,TReply}"/>'s own discipline, doubled.</summary>
    public static async Task<RecordingFulfillmentStandIn> StartAsync(string natsUrl, CancellationToken cancellationToken)
    {
        var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
        var standIn = new RecordingFulfillmentStandIn(connection);

        try
        {
            await SagaIntegrationTestSupport.WaitUntilReachableAsync(connection, RpcSubjects.StockRelease, _probeMarker, cancellationToken).ConfigureAwait(false);
            await SagaIntegrationTestSupport.WaitUntilReachableAsync(connection, RpcSubjects.DespatchCreate, _probeMarker, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await standIn.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallowed deliberately (StandInRpcResponder's own D2
                // discipline) — the probe's own failure is what must reach
                // the caller.
            }

            throw;
        }

        return standIn;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        // A request forever blocked on a CLOSED gate would otherwise hang
        // this teardown indefinitely — open it unconditionally before
        // awaiting the loops, so a test that forgot to re-open its own
        // gate never leaks a live subscription into the next test.
        HoldDespatchCreateGate.Open();

        try
        {
            await Task.WhenAll(_stockReleaseLoop, _despatchCreateLoop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected — exactly what cancelling the loops above causes.
        }
        finally
        {
            try
            {
                // A PING/PONG round trip AFTER the UNSUB fences — the SAME
                // discipline StandInRpcResponder/StandInFulfillmentStockCheckResponder
                // already establish, so a later test's real request can
                // never race this stand-in's own teardown.
                await _connection.PingAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort fence only — a connection already broken has nothing left to fence.
            }

            await _connection.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
        }
    }

    private async Task RunAsync(string subject, Func<byte[], Task<byte[]?>> handle, CancellationToken cancellationToken)
    {
        await foreach (var message in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (message.Data is null)
            {
                continue;
            }

            if (message.Data.AsSpan().SequenceEqual(_probeMarker))
            {
                await message.ReplyAsync(_probeMarker, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            var replyBytes = await handle(message.Data).ConfigureAwait(false);
            if (replyBytes is null)
            {
                continue;
            }

            await message.ReplyAsync(replyBytes, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Mirrors <c>OrderStockReservation.Release</c> (Fulfillment's own
    /// domain method, <c>StockReservationService.cs:111-114</c>): a
    /// reservation already <c>consumed</c> or <c>released</c> releases
    /// nothing and is a plain success no-op (<c>already_released</c>) — no
    /// fact this stand-in emits, matching production, where NOTHING is
    /// published for that outcome either. Never gated — <c>stock.release</c>
    /// always processes as soon as its own request is received; only
    /// <c>despatch.create</c> can be held.
    /// </summary>
    private Task<byte[]?> HandleStockReleaseAsync(byte[] data)
    {
        var request = RpcJson.Deserialize<StockReleaseRequestPayload>(data);

        byte[] replyBytes;
        lock (_gate)
        {
            string outcome;
            if (_reservation == Reservation.Reserved)
            {
                _reservation = Reservation.Released;
                outcome = "released";
            }
            else
            {
                outcome = "already_released";
            }

            replyBytes = RpcJson.Serialize(new StockReleaseReplyPayload(outcome, request.OrderReference, Released: []));
            _stockReleaseOutcomes.Enqueue(outcome);
            _commandsProcessed.Enqueue("stock.release");
        }

        return Task.FromResult<byte[]?>(replyBytes);
    }

    /// <summary>
    /// Mirrors <c>DespatchCreationService.CreateAsync</c>'s own refusal
    /// (<c>NoReservedStockForDespatchError</c> → <c>PRECONDITION_FAILED</c>,
    /// <c>StockErrorMapper.cs:49</c>): nothing reserved to consume is
    /// refused, terminal, no fact. Awaits <see cref="HoldDespatchCreateGate"/>
    /// BEFORE touching shared state — the one seam a test can hold closed to
    /// force a specific, deterministic arbitration outcome.
    /// </summary>
    private async Task<byte[]?> HandleDespatchCreateAsync(byte[] data)
    {
        var request = RpcJson.Deserialize<DespatchCreateRequestPayload>(data);

        await HoldDespatchCreateGate.WaitAsync().ConfigureAwait(false);

        byte[] replyBytes;
        lock (_gate)
        {
            if (_reservation == Reservation.Reserved)
            {
                _reservation = Reservation.Consumed;
                replyBytes = RpcJson.Serialize(new DespatchCreateReplyPayload(request.OrderReference, "DES-000001", DateTimeOffset.UtcNow, Created: true, Lines: []));
            }
            else
            {
                replyBytes = RpcJson.Serialize(new RpcErrorPayload("PRECONDITION_FAILED", $"No reserved stock to despatch for order {request.OrderReference}."));
            }

            _commandsProcessed.Enqueue("despatch.create");
        }

        return replyBytes;
    }
}

/// <summary>
/// A minimal async open/closed gate — <see cref="Open"/> releases every
/// currently- and future-waiting caller; <see cref="Reset"/> closes it
/// again for a NEW wait cycle. Starts OPEN. Not general-purpose
/// synchronisation — <see cref="RecordingFulfillmentStandIn"/>'s own
/// deterministic ordering barrier (CLAUDE.md: "a barrier or a controlled
/// release, never repetition"), nothing else needs it.
/// </summary>
internal sealed class TestGate
{
    private TaskCompletionSource _tcs = Completed();

    public void Reset() => _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open() => _tcs.TrySetResult();

    public Task WaitAsync() => _tcs.Task;

    private static TaskCompletionSource Completed()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult();
        return tcs;
    }
}
