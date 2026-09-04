using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// Ten one-line <see cref="ICommandHandler{TCommand}"/> wrappers over the ONE
/// <see cref="SagaFactHandler"/> (design.md §5.1, §5.3). Publishes the
/// matching dispatch-owed event through <see cref="IDispatcher.PublishAsync"/>
/// ONLY when the outcome is <see cref="SagaFactOutcome.Processed"/> AND a
/// command was enqueued — i.e. strictly after the transaction committed
/// (§5.1 step 4). Five facts own no dispatch-owed event at all
/// (<c>stock.rejected.v1</c>, <c>stock.released.v1</c>, <c>invoice.issued.v1</c>,
/// <c>payment.received.v1</c>, <c>credit.released.v1</c> — their step never
/// owes a follow-up command), so their handlers simply delegate.
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

public sealed class HandleCreditReleasedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleCreditReleasedFactCommand>
{
    public Task HandleAsync(HandleCreditReleasedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}
