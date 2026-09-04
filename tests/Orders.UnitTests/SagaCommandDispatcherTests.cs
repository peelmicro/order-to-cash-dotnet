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
