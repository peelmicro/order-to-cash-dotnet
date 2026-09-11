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
/// <c>observability_reliability</c>, design.md §4.4 (<c>OR3</c>) —
/// <see cref="SagaCommandDispatcher"/>'s own orchestration of the first-park
/// hook, over a FAKE <see cref="ISagaFirstParkDeadLetterHandler"/> (no
/// database, no transaction — the hook's OWN internal behaviour, including
/// the "not called when the claim reports already-dead-lettered" case, is
/// <see cref="SagaFirstParkDeadLetterHandlerTests"/>'s job). Two cases, per
/// tasks.md A2e: called exactly once with the row's accumulated attempts and
/// the last error when <see cref="ISagaCommandStore.ParkAsync"/> reports it
/// performed the transition; never called when it reports it did not.
/// </summary>
public sealed class SagaCommandDispatcherFirstParkTests
{
    private static readonly Guid _orderId = Guid.NewGuid();
    private static readonly Guid _commandId = Guid.NewGuid();

    [Fact]
    public async Task OR3_CallsTheFirstParkHookExactlyOnce_WithTheRowsAccumulatedAttemptsAndTheLastError_WhenParkAsyncReportsTheTransition()
    {
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed(attemptsAlreadyParked: 4), ParkReturns = true };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new SagaCommandTransportError(RpcSubjects.StockReserve, "no responder is subscribed."));
        var firstPark = new RecordingFirstParkDeadLetterHandler();

        var dispatcher = BuildDispatcher(store, sagaCommands, delay, firstPark);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        var call = Assert.Single(firstPark.Calls);
        Assert.Equal(_commandId, call.Claimed.Id);
        // 4 already-parked attempts (the row's OWN attempts field, captured
        // at claim time) + 3 this-cycle attempts (MaxAttempts, the default
        // policy) = 7 — never re-read from a fresh store query.
        Assert.Equal(7, call.Attempts);
        Assert.Contains("no responder", call.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OR3_DoesNotCallTheFirstParkHook_WhenParkAsyncReportsNoTransition()
    {
        // A racing dispatcher already reported the row `sent` — SO5's own
        // race-safety. Dead-lettering a row that just turned out to have
        // succeeded would be wrong regardless (design.md §4.4).
        var store = new FakeSagaCommandStore { ClaimResult = BuildClaimed(attemptsAlreadyParked: 0), ParkReturns = false };
        var delay = new FakeSagaRetryDelay();
        var sagaCommands = new FakeSagaCommands(_ => throw new SagaCommandTransportError(RpcSubjects.StockReserve, "no responder is subscribed."));
        var firstPark = new RecordingFirstParkDeadLetterHandler();

        var dispatcher = BuildDispatcher(store, sagaCommands, delay, firstPark);

        await dispatcher.DispatchAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);

        Assert.Empty(firstPark.Calls);
        // ParkAsync was still called — SO5's own park bookkeeping is
        // unaffected by whether the transition actually happened.
        Assert.Single(store.ParkCalls);
    }

    private static SagaCommandDispatcher BuildDispatcher(FakeSagaCommandStore store, FakeSagaCommands sagaCommands, FakeSagaRetryDelay delay, ISagaFirstParkDeadLetterHandler firstPark) =>
        new(store, sagaCommands, delay, firstPark, Options.Create(new OrdersSagaOptions()), NullLogger<SagaCommandDispatcher>.Instance);

    private static SagaCommandRecord BuildClaimed(int attemptsAlreadyParked)
    {
        var payload = System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(new StockReserveRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", [])));
        return new SagaCommandRecord(_commandId, _orderId, "ORD-000001", SagaCommandKind.StockReserve, payload, Guid.NewGuid(), attemptsAlreadyParked);
    }

    private sealed class FakeSagaRetryDelay : ISagaRetryDelay
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingFirstParkDeadLetterHandler : ISagaFirstParkDeadLetterHandler
    {
        public List<(SagaCommandRecord Claimed, int Attempts, string LastError)> Calls { get; } = [];

        public Task HandleAsync(SagaCommandRecord claimed, int attempts, string lastError, CancellationToken cancellationToken)
        {
            Calls.Add((claimed, attempts, lastError));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaCommandStore : ISagaCommandStore
    {
        public SagaCommandRecord? ClaimResult { get; set; }

        public bool ParkReturns { get; set; } = true;

        public List<(Guid Id, int AttemptsMade, string LastError)> ParkCalls { get; } = [];

        public Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken) => Task.FromResult(ClaimResult);

        public Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
        {
            ParkCalls.Add((commandId, attemptsMade, lastError));
            return Task.FromResult(ParkReturns);
        }

        public Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasPendingCompensationAsync(Guid orderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSagaCommands(Func<int, Task<StockReserveReplyPayload>> reserveStock) : ISagaCommands
    {
        public Task<StockReserveReplyPayload> ReserveStockAsync(StockReserveRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => reserveStock(1);

        public Task<StockReleaseReplyPayload> ReleaseStockAsync(StockReleaseRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CreditHoldReplyPayload> HoldCreditAsync(CreditHoldRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InvoiceIssueReplyPayload> IssueInvoiceAsync(InvoiceIssueRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CreditReleaseReplyPayload> ReleaseCreditAsync(CreditReleaseRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
