using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The direct analogue of #7's five RxJS <c>ofType</c> streams in
/// <c>order.sagas.ts</c> — one file, same name, for a one-to-one benchmark
/// mapping (design.md §5.5). Each of the five classes below does exactly one
/// thing: turn a dispatch-owed application event into a
/// <see cref="SagaCommandRef"/> signal (SO3's fast path). The durable
/// <c>saga_commands</c> row — already committed by the time these run — is
/// the actual guarantee; this hop is only ever a latency optimisation over
/// it (design.md §5.5's own closing sentence).
/// </summary>
public sealed class OrderPlacedFactRecordedHandler(ISagaCommandSignal signal) : IEventHandler<OrderPlacedFactRecorded>
{
    public Task HandleAsync(OrderPlacedFactRecorded @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockReserve));
        return Task.CompletedTask;
    }
}

public sealed class OrderMarkedStockReservedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedStockReserved>
{
    public Task HandleAsync(OrderMarkedStockReserved @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditHold));
        return Task.CompletedTask;
    }
}

public sealed class CreditRejectionRecordedHandler(ISagaCommandSignal signal) : IEventHandler<CreditRejectionRecorded>
{
    public Task HandleAsync(CreditRejectionRecorded @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockRelease));
        return Task.CompletedTask;
    }
}

public sealed class OrderConfirmedBySagaHandler(ISagaCommandSignal signal) : IEventHandler<OrderConfirmedBySaga>
{
    public Task HandleAsync(OrderConfirmedBySaga @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.DespatchCreate));
        return Task.CompletedTask;
    }
}

public sealed class OrderMarkedDespatchedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedDespatched>
{
    public Task HandleAsync(OrderMarkedDespatched @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.InvoiceIssue));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The sixth of these classes — SA-4's own second step of the
/// stock-then-credit operator-cancel compensation: <c>stock.release</c> has
/// already been processed (the FIRST step, enqueued directly by
/// <c>CancelOrderCommandHandler</c>, outside this fast-path mechanism
/// entirely), and the resulting <c>stock.released.v1</c> fact's
/// <c>credit_approved</c>/<c>confirmed</c> variant now owes
/// <c>credit.release</c>.
/// </summary>
public sealed class StockReleasedForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<StockReleasedForCancellationRecorded>
{
    public Task HandleAsync(StockReleasedForCancellationRecorded @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));
        return Task.CompletedTask;
    }
}

/// <summary>
/// The seventh of these classes — id 62 fix round 1, F1. Signals
/// <see cref="SagaCommandKind.CreditRelease"/>, never
/// <see cref="SagaCommandKind.DespatchCreate"/>: this event exists
/// specifically so <c>HandleCreditApprovedFactCommandHandler</c> never has
/// to route a late-approval enqueue through <see cref="OrderConfirmedBySagaHandler"/>'s
/// own hard-coded despatch signal.
/// </summary>
public sealed class LateCreditApprovalForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<LateCreditApprovalForCancellationRecorded>
{
    public Task HandleAsync(LateCreditApprovalForCancellationRecorded @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));
        return Task.CompletedTask;
    }
}
