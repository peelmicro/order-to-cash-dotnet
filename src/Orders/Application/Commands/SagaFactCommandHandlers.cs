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
/// (<c>stock.rejected.v1</c>, <c>credit.released.v1</c>, <c>invoice.issued.v1</c>,
/// <c>payment.received.v1</c> — every one of their variants never owes a
/// follow-up command), so their handlers simply delegate. SA-4 (the
/// human-gated shared-spec amendment ruled 2026-09-11) moved the
/// STOCK-RELEASED-owes-CREDIT-RELEASE conditional shape from
/// <c>credit.released.v1</c> to <c>stock.released.v1</c>: its
/// <c>credit_approved</c>/<c>confirmed</c> variant now owes
/// <see cref="Sagas.SagaCommandKind.CreditRelease"/>, so
/// <see cref="HandleStockReleasedFactCommandHandler"/> is the conditional
/// one now, the same shape as <see cref="HandleCreditRejectedFactCommandHandler"/>
/// — and <c>credit.released.v1</c>'s own handler is a plain delegation,
/// matching the other three that never owe anything.
///
/// <c>credit.approved.v1</c> (id 62 fix round 1, F1) is the ONE fact type
/// where the SAME <c>ICommandHandler</c> must choose between TWO events,
/// because <see cref="SagaFactHandler"/>'s late-approval branch enqueues a
/// DIFFERENT command than the fact type's own ordinary <see cref="SagaStepTable"/>
/// row — see <see cref="HandleCreditApprovedFactCommandHandler"/>'s own
/// remarks.
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

/// <summary>
/// Id 62 fix round 1, F1 — <c>credit.approved.v1</c> is the ONE fact type
/// whose owed command is NOT fully determined by <see cref="SagaStepTable"/>
/// alone: SA-4's late-approval branch (<c>SagaFactHandler.HandleAsync</c>'s
/// own <c>lateForAnAcceptedOperatorCancel</c> check) enqueues
/// <see cref="Sagas.SagaCommandKind.CreditRelease"/> directly, bypassing the
/// ordinary Advance's <c>DespatchCreate</c> entirely. This handler
/// therefore chooses the event by what <see cref="SagaFactResult.Enqueued"/>
/// actually names, never by the fact type alone — publishing
/// <see cref="OrderConfirmedBySaga"/> (which
/// <see cref="OrderConfirmedBySagaHandler"/> turns into a HARD-CODED
/// <c>DespatchCreate</c> signal) for a late approval would claim a
/// <c>saga_commands</c> row that was never enqueued and leave the REAL
/// <c>credit.release</c> row to the sweeper alone.
/// </summary>
public sealed class HandleCreditApprovedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditApprovedFactCommand>
{
    public async Task HandleAsync(HandleCreditApprovedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            object dispatchOwedEvent = enqueued.Command == SagaCommandKind.CreditRelease
                ? new LateCreditApprovalForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId)
                : new OrderConfirmedBySaga(enqueued.OrderId, command.Fact.CorrelationId);

            await dispatcher.PublishAsync(dispatchOwedEvent, cancellationToken).ConfigureAwait(false);
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

/// <summary>
/// SA-4: <c>stock.released.v1</c> now owes a command on its
/// <c>credit_approved</c>/<c>confirmed</c> variant (the original
/// <c>stock_reserved</c> variant, R28/SO7, still owes nothing — it is a
/// <c>Cancel</c>, which never owes a follow-up command) — no longer a plain
/// delegation. Mirrors <see cref="HandleCreditRejectedFactCommandHandler"/>'s
/// exact conditional-publish shape, one line different: the event type.
/// </summary>
public sealed class HandleStockReleasedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleStockReleasedFactCommand>
{
    public async Task HandleAsync(HandleStockReleasedFactCommand command, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command.Fact, cancellationToken).ConfigureAwait(false);

        if (result is { Outcome: SagaFactOutcome.Processed, Enqueued: { } enqueued })
        {
            await dispatcher.PublishAsync(new StockReleasedForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
        }
    }
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
/// SA-4: <c>credit.released.v1</c> no longer owes anything on EITHER
/// variant — its <c>credit_approved</c>/<c>confirmed</c> variant is now the
/// COMPLETING <c>Cancel</c> step (never owes a follow-up command, same as
/// its original <c>paid</c> variant, R24) — back to a plain delegation,
/// matching <see cref="HandleStockRejectedFactCommandHandler"/>'s shape.
/// </summary>
public sealed class HandleCreditReleasedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleCreditReleasedFactCommand>
{
    public Task HandleAsync(HandleCreditReleasedFactCommand command, CancellationToken cancellationToken) =>
        handler.HandleAsync(command.Fact, cancellationToken);
}
