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
/// The sixth of these classes (feature <c>orders_cancel_responder</c>) — the
/// SECOND step of the reverse-order-of-acquisition operator-cancel
/// compensation: <c>credit.release</c> has already been processed (the FIRST
/// step, enqueued directly by <c>CancelOrderCommandHandler</c>, outside this
/// fast-path mechanism entirely), and the resulting <c>credit.released.v1</c>
/// fact's <c>credit_approved</c>/<c>confirmed</c> variant now owes
/// <c>stock.release</c> — the SAME owed command <see cref="CreditRejectionRecordedHandler"/>
/// signals for R27's unrelated branch.
/// </summary>
public sealed class CreditReleasedForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<CreditReleasedForCancellationRecorded>
{
    public Task HandleAsync(CreditReleasedForCancellationRecorded @event, CancellationToken cancellationToken)
    {
        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockRelease));
        return Task.CompletedTask;
    }
}
