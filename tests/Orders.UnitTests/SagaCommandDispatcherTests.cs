using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Saga;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §6.2 — SO4's retry/backoff, SO6's "a business rejection is
/// marked sent and never retried", SO5's park on exhaustion, and the SO11
/// zero-row-claim no-op — against a fake port and a fake delay (no real NATS,
/// no wall-clock wait).
/// </summary>
public sealed class SagaCommandDispatcherTests
{
    private static readonly Guid _orderId = Guid.NewGuid();
    private static readonly Guid _commandId = Guid.NewGuid();

    [Fact]
    public async Task SO4_RetriesATimedOutCommandUpToMaxAttemptsWithTheConfiguredBackoffSchedule()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed() };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(attempt => attempt < 3
            ? throw new SagaCommandTimeoutError(RpcSubjects.StockReserve, 5_000)
            : Task.FromResult(new StockReserveReplyPayload("accepted", "ORD-000001")));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(3, sagaCommands.CallCount);
        Assert.Equal([500, 1000], delay.Delays);
        Assert.Single(store.MarkSentCalls);
        Assert.Empty(store.ParkCalls);
    }

    [Fact]
    public async Task ABusinessRejectionIsMarkedSentAndNeverRetried()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed() };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => Task.FromResult(new StockReserveReplyPayload("rejected", "ORD-000001", Shortages: [])));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(1, sagaCommands.CallCount);
        Assert.Empty(delay.Delays);
        Assert.Single(store.MarkSentCalls);
        Assert.Empty(store.ParkCalls);
    }

    [Fact]
    public async Task ExhaustionParksWithTheAccumulatedAttemptsAndTheLastError()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed() };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new SagaCommandTransportError(RpcSubjects.StockReserve, "no responder is subscribed."));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(3, sagaCommands.CallCount);
        Assert.Empty(store.MarkSentCalls);
        var parked = Assert.Single(store.ParkCalls);
        Assert.Equal(_commandId, parked.Id);
        Assert.Equal(3, parked.AttemptsMade);
        Assert.Contains("no responder", parked.LastError, StringComparison.Ordinal);
    }

    // Feature 42 — the adapter now classifies a terminal RpcError business
    // rejection (e.g. PRECONDITION_FAILED) as SagaCommandBusinessRejectionError,
    // distinct from SagaCommandTransportError. Proves the dispatcher's
    // short-circuit: NO further in-line attempts, NO backoff delay, and a
    // terminal "rejected" resolution — never ParkAsync's retry-eligible path.
    [Fact]
    public async Task R42_ATerminalBusinessRejectionCallsThePortExactlyOnceDelaysZeroTimesAndRejectsRatherThanParking()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed() };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new SagaCommandBusinessRejectionError(RpcSubjects.StockReserve, "PRECONDITION_FAILED", "reservation already consumed"));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(1, sagaCommands.CallCount); // NOT MaxAttempts (3) — no in-line retry at all.
        Assert.Empty(delay.Delays); // NOT SO4's backoff schedule — no delay is ever awaited.
        Assert.Empty(store.MarkSentCalls);
        Assert.Empty(store.ParkCalls); // never ParkAsync's retry-eligible path.
        var rejected = Assert.Single(store.RejectCalls);
        Assert.Equal(_commandId, rejected.Id);
        Assert.Equal(1, rejected.AttemptsMade);
        Assert.Contains("PRECONDITION_FAILED", rejected.LastError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A genuinely TRANSIENT rejection (design.md §6.1's UNAVAILABLE/TIMEOUT/
    /// INTERNAL_ERROR — carried here as SagaCommandTransportError, the
    /// adapter's own classification of it) is UNCHANGED by feature 42: still
    /// retried to exhaustion and still parks the old way, never calling
    /// RejectAsync. This is the acceptance bullet "a genuinely retryable
    /// transport failure ... is unaffected — still retried exactly as
    /// today", proven directly against the SAME dispatcher instance the
    /// terminal test above exercises.
    /// </summary>
    [Fact]
    public async Task R42_ATransientRpcErrorIsUnaffectedByTheTerminalClassification_StillRetriedToExhaustionAndParked()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed() };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new SagaCommandTransportError(RpcSubjects.StockReserve, "INTERNAL_ERROR: boom"));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(3, sagaCommands.CallCount); // full retry budget, unaffected.
        Assert.Equal([500, 1000], delay.Delays);
        Assert.Empty(store.MarkSentCalls);
        Assert.Empty(store.RejectCalls); // the terminal path was never taken.
        var parked = Assert.Single(store.ParkCalls);
        Assert.Equal(3, parked.AttemptsMade);
    }

    [Fact]
    public async Task AZeroRowClaimDispatchesNothing()
    {
        var store = new FakeSagaCommandStore { ClaimResult = null };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new InvalidOperationException("must not be called"));

        var dispatcher = BuildDispatcher(store, sagaCommands, delay);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Equal(0, sagaCommands.CallCount);
        Assert.Empty(store.MarkSentCalls);
        Assert.Empty(store.ParkCalls);
    }

    private static SagaCommandDispatcher BuildDispatcher(FakeSagaCommandStore store, FakeSagaCommands sagaCommands, FakeSagaRetryDelay delay) =>
        new(store, sagaCommands, delay, Options.Create(new OrdersSagaOptions()), NullLogger<SagaCommandDispatcher>.Instance);

    private static SagaCommandRecord BuildClaimed()
    {
        var payload = Encoding.UTF8.GetString(RpcJson.Serialize(new StockReserveRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", [])));
        return new SagaCommandRecord(_commandId, _orderId, "ORD-000001", SagaCommandKind.StockReserve, payload, Guid.NewGuid(), 0);
    }

    private sealed class FakeSagaRetryDelay : ISagaRetryDelay
    {
        public List<int> Delays { get; } = [];

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            Delays.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public SagaCommandRecord? ClaimResult { get; set; }

        public List<Guid> MarkSentCalls { get; } = [];

        public List<(Guid Id, int AttemptsMade, string LastError)> ParkCalls { get; } = [];

        public List<(Guid Id, int AttemptsMade, string LastError)> RejectCalls { get; } = [];

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => Task.FromResult(ClaimResult);

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken)
        {
            MarkSentCalls.Add(commandId);
            return Task.CompletedTask;
        }

        public Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
        {
            ParkCalls.Add((commandId, attemptsMade, lastError));
            return Task.CompletedTask;
        }

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
        {
            RejectCalls.Add((commandId, attemptsMade, lastError));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaCommands(Func<int, Task<StockReserveReplyPayload>> reserveStock) : ISagaCommands
    {
        public int CallCount { get; private set; }

        public Task<StockReserveReplyPayload> ReserveStockAsync(StockReserveRequestPayload request, CancellationToken cancellationToken)
        {
            CallCount++;
            return reserveStock(CallCount);
        }

        public Task<StockReleaseReplyPayload> ReleaseStockAsync(StockReleaseRequestPayload request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CreditHoldReplyPayload> HoldCreditAsync(CreditHoldRequestPayload request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InvoiceIssueReplyPayload> IssueInvoiceAsync(InvoiceIssueRequestPayload request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
