using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// Ten one-line <see cref="ICommandHandler{TCommand}"/> wrappers over the ONE
/// <see cref="SagaFactHandler"/> (design.md §5.1, §5.3). Publishes the
/// matching dispatch-owed event through <see cref="IDispatcher.PublishAsync"/>
/// ONLY when the outcome is <see cref="SagaFactOutcome.Processed"/> AND a
/// command was enqueued — i.e. strictly after the transaction committed
/// (§5.1 step 4). Four facts own no dispatch-owed event at all
/// (<c>stock.rejected.v1</c>, <c>stock.released.v1</c>, <c>invoice.issued.v1</c>,
/// <c>payment.received.v1</c> — every one of their variants never owes a
/// follow-up command), so their handlers simply delegate.
/// <c>credit.released.v1</c> (feature <c>orders_cancel_responder</c>) is NO
/// LONGER one of these: its <c>credit_approved</c>/<c>confirmed</c>
/// variants DO owe <see cref="Sagas.SagaCommandKind.StockRelease"/>, so
/// <see cref="HandleCreditReleasedFactCommandHandler"/> is conditional, the
/// same shape as <see cref="HandleCreditRejectedFactCommandHandler"/>.
/// </summary>
public sealed class HandleOrderPlacedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleOrderPlacedFactCommand>
{
    public async Task HandleAsync(HandleOrderPlacedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new OrderPlacedFactRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HandleStockReservedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleStockReservedFactCommand>
{
    public async Task HandleAsync(HandleStockReservedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new OrderMarkedStockReserved(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HandleStockRejectedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleStockRejectedFactCommand>
{
    public Task HandleAsync(HandleStockRejectedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}

public sealed class HandleCreditApprovedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditApprovedFactCommand>
{
    public async Task HandleAsync(HandleCreditApprovedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new OrderConfirmedBySaga(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HandleCreditRejectedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditRejectedFactCommand>
{
    public async Task HandleAsync(HandleCreditRejectedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new CreditRejectionRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HandleStockReleasedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleStockReleasedFactCommand>
{
    public Task HandleAsync(HandleStockReleasedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}

public sealed class HandleOrderDespatchedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleOrderDespatchedFactCommand>
{
    public async Task HandleAsync(HandleOrderDespatchedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new OrderMarkedDespatched(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class HandleInvoiceIssuedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleInvoiceIssuedFactCommand>
{
    public Task HandleAsync(HandleInvoiceIssuedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}

public sealed class HandlePaymentReceivedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandlePaymentReceivedFactCommand>
{
    public Task HandleAsync(HandlePaymentReceivedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}

/// <summary>
/// Feature <c>orders_cancel_responder</c>: <c>credit.released.v1</c> now owes
/// a command on its <c>credit_approved</c>/<c>confirmed</c> variants (the
/// original <c>paid</c> variant, R24, still owes nothing) — no longer a
/// plain delegation. Mirrors <see cref="HandleCreditRejectedFactCommandHandler"/>'s
/// exact conditional-publish shape, one line different: the event type.
/// </summary>
public sealed class HandleCreditReleasedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditReleasedFactCommand>
{
    public async Task HandleAsync(HandleCreditReleasedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new CreditReleasedForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
}
