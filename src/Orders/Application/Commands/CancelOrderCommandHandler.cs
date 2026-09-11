using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The <c>orders.cancel</c> command handler (feature
/// <c>orders_cancel_responder</c>) — operator-initiated cancellation, a NEW
/// saga trigger distinct from the fact-driven R19-R29 flow this Application
/// layer already has: an RPC request, not a consumed fact. <c>saga.md</c>
/// §4.3's generalisation table is the exact spec this class transcribes,
/// branching on the order's CURRENT status at the moment of the request:
/// <list type="bullet">
/// <item><c>credit_approved</c>/<c>confirmed</c> — releases the credit hold
/// FIRST (reverse order of acquisition: it was acquired SECOND, after
/// stock), then stock, via the SAME durable <see cref="ISagaCommandStore"/>
/// mechanism every other saga command uses. The order stays where it is
/// until <c>credit.released.v1</c> then <c>stock.released.v1</c> arrive —
/// the EXISTING, EXTENDED <see cref="SagaStepTable"/> completes the chain
/// (no orchestration written here for the second step).</item>
/// <item><c>stock_reserved</c> — releases stock directly (reason
/// <c>order_cancelled</c>). The order stays <c>stock_reserved</c> until
/// <c>stock.released.v1</c> arrives; that fact's EXISTING <c>stock_reserved</c>
/// variant (R28/SO7, already reason-parametric via <see cref="SagaStepTable.MapReason"/>)
/// completes the cancellation with ZERO changes needed for THIS branch.</item>
/// <item><c>placed</c>, or a terminal status — <see cref="Order.Cancel"/> is
/// the ONE guard for both outcomes, reused VERBATIM (bullet 2's own acceptance
/// text: "no new domain modeling"): the immediate branch calls it and it
/// succeeds; the terminal branch calls it and it throws
/// <see cref="Errors.OrderNotCancellableError"/>, which this handler does
/// NOT catch — it propagates to the responder's error mapping, exactly like
/// every other domain refusal in this codebase.</item>
/// </list>
/// </summary>
/// <remarks>
/// <b>A disclosed race — CLOSED by feature <c>operator_cancel_races_saga_forward_progress</c>
/// (id 62).</b> Every fact-driven saga step (<see cref="SagaFactHandler"/>)
/// defends its precondition with R25's equality-only check: a fact that
/// finds the order somewhere other than the expected status is safely
/// ignored. This handler reads the order's status and enqueues a
/// compensating command inside ONE transaction, while the saga's own
/// FORWARD progress (e.g. <c>credit.approved.v1</c> arriving and advancing
/// <c>stock_reserved</c> -&gt; <c>confirmed</c>, or <c>order.despatched.v1</c>
/// advancing past <c>confirmed</c>) runs in an INDEPENDENT transaction on an
/// INDEPENDENT consumer. Two defences now close the window id 62's own
/// remarks here used to describe as open: (1) <see cref="IOrderRepository.GetByIdAsync"/>
/// takes an explicit <c>UPDLOCK, ROWLOCK</c> on the order row
/// (<c>EfCoreOrderRepository</c>'s own remarks), serialising this handler's
/// read against any TRUE-CONCURRENT <see cref="SagaFactHandler"/> transaction
/// for the SAME order; (2) <see cref="SagaFactHandler"/> itself checks, for
/// every genuine forward-progress <c>Advance</c> step, whether an
/// operator-cancel compensation is ALREADY enqueued for this order
/// (<see cref="ISagaCommandStore.HasPendingCompensationAsync"/>) and
/// SUPERSEDES the fact (records it <c>SagaIgnoredFactMarker.Superseded</c>,
/// never applies it) rather than letting it advance the order out from
/// under a pending compensation — closing the WIDE, non-concurrent window
/// too (the despatch.create command the saga dispatched on reaching
/// <c>confirmed</c>, BEFORE the operator ever cancelled, can still reply
/// seconds later; superseding, not locking, is what stops THAT reply from
/// stranding the compensation). This is the SAME class of race #7 found
/// live and disclosed for the identical mechanism (its own
/// <c>orders-cancel.integration.spec.ts:70-82</c>/<c>:138-152</c>) and
/// never fixed — worse on the <c>credit_approved</c>/<c>confirmed</c> branch
/// than the <c>stock_reserved</c> one (see
/// <c>progress/impl_orders_cancel_responder.md</c>'s "a genuinely new
/// finding" section, and id 62's own
/// <c>progress/impl_operator_cancel_races_saga_forward_progress.md</c> for
/// the full reproduction/fix/arming record).
///
/// <b>The operator note reaches the read-model timeline on EVERY branch —
/// feature <c>operator_note_reaches_the_timeline</c> (SA-2) closed the
/// immediate branch; feature
/// <c>operator_note_survives_the_compensation_branches</c> (id 71) closes
/// the other three.</b> <c>asyncapi.yaml</c>'s <c>OrderCancelledPayload</c>
/// gained an optional <c>note</c> field (SA-2), so this handler threads
/// <see cref="CancelOrderCommand.Note"/> into <see cref="Order.Cancel"/> on
/// the immediate/<c>default</c> branch below directly. The
/// <c>stock_reserved</c>/<c>credit_approved</c>/<c>confirmed</c> branches
/// have no direct access to <see cref="Order.Cancel"/> at all — they enqueue
/// compensation and let a LATER, fact-driven call
/// (<c>SagaFactHandler.ApplyStepAsync</c>, an independent transaction
/// reacting to <c>stock.released.v1</c>/<c>credit.released.v1</c>) complete
/// the cancellation. Neither <c>StockReleaseRequestPayload</c> nor
/// <c>CreditReleaseRequestPayload</c> carries a note (SA-2 touched only
/// <c>OrderCancelledPayload</c>), so the note cannot travel on the RPC wire
/// — instead <see cref="BeginCreditReleaseCompensationAsync"/> and
/// <see cref="BeginStockReleaseCompensationAsync"/> both store it inside the
/// synthetic <c>orders.cancel.requested</c> envelope, in the SAME
/// <c>saga_commands.triggering_event_envelope</c> column feature 27 added
/// (design.md §4.1, not §4.2 — §4.2 is fact-triggered threading through
/// <c>SagaFactsConsumer</c>/<c>SagaFactHandler</c>, which this RPC-triggered
/// enqueue is not) — porting #7's own <c>buildTriggeringEnvelope</c>
/// (<c>cancel-order.handler.ts:272-286</c>) verbatim, including the note.
/// R29 does not require this envelope (an operator cancel has no
/// triggering fact in R29's sense); it is #7's own named diagnostic
/// convention, ported here for parity, not a requirement.
/// <c>SagaFactHandler.ApplyStepAsync</c> reads it back via
/// <see cref="ISagaCommandStore.FindOperatorCancelNoteAsync"/> when the
/// compensating fact finally completes the cancellation. #7 itself never
/// reads this back — it only ever used the envelope for the DLQ diagnostic
/// — so this half is #8-only work, mandated by backlog id 71 directly (see
/// that feature's own <c>progress/impl_*.md</c> for the full ledger).
/// </remarks>
public sealed class CancelOrderCommandHandler(
    IUnitOfWork unitOfWork,
    IOrderRepository orders,
    ISagaCommandStore commandStore,
    ISagaCommandSignal signal,
    IClock clock) : ICommandHandler<CancelOrderCommand, CancelOrderResult>
{
    public async Task<CancelOrderResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var orderId = UniqueId.From(command.OrderId);
        SagaCommandKind? fastPathSignal = null;

        var result = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var order = await orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
                    ?? throw new OrderNotFoundError(command.OrderId);

                switch (order.Status)
                {
                    case OrderStatus.CreditApproved or OrderStatus.Confirmed:
                        fastPathSignal = await BeginCreditReleaseCompensationAsync(order, command.Note, ct).ConfigureAwait(false);
                        return new CancelOrderResult(order.Id.Value, order.OrderReference.Value, order.Status, CancellationReason: null, CompensationPlanned: ["credit_release", "stock_release"]);

                    case OrderStatus.StockReserved:
                        fastPathSignal = await BeginStockReleaseCompensationAsync(order, command.Note, ct).ConfigureAwait(false);
                        return new CancelOrderResult(order.Id.Value, order.OrderReference.Value, order.Status, CancellationReason: null, CompensationPlanned: ["stock_release"]);

                    default:
                        // `placed`, OR a terminal status Order.Cancel itself
                        // refuses (despatched/invoiced/paid/completed/already-
                        // cancelled) — the ONE guard for both outcomes, reused
                        // verbatim. No status-set check is written here
                        // (bullet 2/3's own acceptance text): a refusal throws
                        // Order.Cancel's own OrderNotCancellableError, left
                        // uncaught so it reaches the responder's error mapping.
                        var now = clock.UtcNow;
                        order.Cancel(CancellationReason.OperatorCancelled, compensationSteps: [], now, UniqueId.New(), note: command.Note);
                        await orders.SaveChangesAsync(ct).ConfigureAwait(false);
                        return new CancelOrderResult(order.Id.Value, order.OrderReference.Value, order.Status, order.CancellationReason, CompensationPlanned: []);
                }
            },
            cancellationToken).ConfigureAwait(false);

        // The fast path (design.md §5.5) — best-effort, strictly AFTER
        // commit, matching ISagaCommandSignal.Signal's own documented
        // constraint. A crash between commit and this call still leaves the
        // durable `pending` row for the sweeper to resume (SO3) — the same
        // crash-window composition every fact-driven dispatch-owed event
        // already relies on.
        if (fastPathSignal is { } kind)
        {
            signal.Signal(new SagaCommandRef(command.OrderId, kind));
        }

        return result;
    }

    /// <summary>
    /// <c>credit_approved</c>/<c>confirmed</c> branch, first step: enqueues
    /// <c>credit.release</c> — reason is not a caller-supplied field on
    /// <see cref="CreditReleaseRequestPayload"/> at all (the RPC always
    /// releases with <c>order_cancelled</c>, <c>asyncapi.yaml</c>'s own
    /// description). Built inline, NOT through
    /// <see cref="SagaCommandRequestFactory"/>: that factory builds requests
    /// FROM a triggering fact (design.md §6.3), and this enqueue has none —
    /// it is RPC-triggered, exactly the "own enqueue path" the factory's own
    /// header comment named as this feature's job, not its own. The
    /// <c>triggeringEventEnvelope</c>/<c>Topic</c> pair below is a SEPARATE
    /// concern from the RPC request payload above: id 71's synthetic
    /// <c>orders.cancel.requested</c> envelope, R29's dead-letter bookkeeping
    /// and this order's operator note, never the real request.
    /// </summary>
    private async Task<SagaCommandKind?> BeginCreditReleaseCompensationAsync(Order order, string? note, CancellationToken cancellationToken)
    {
        var payload = new CreditReleaseRequestPayload(order.OrderReference.Value, order.RetailerCode, order.CompanyCode);
        var payloadJson = System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));

        var requestId = UniqueId.New();
        var envelope = OperatorCancelRequestedEnvelope.Build(order.Id, requestId, clock.UtcNow, note);

        var outcome = await commandStore.EnqueueAsync(
            order.Id.Value,
            order.OrderReference.Value,
            SagaCommandKind.CreditRelease,
            payloadJson,
            triggeringEventId: requestId.Value,
            triggeringEventEnvelope: envelope,
            triggeringEventTopic: OperatorCancelRequestedEnvelope.Topic,
            cancellationToken).ConfigureAwait(false);

        return outcome == EnqueueOutcome.Enqueued ? SagaCommandKind.CreditRelease : null;
    }

    /// <summary>
    /// <c>stock_reserved</c> branch: enqueues <c>stock.release</c> with
    /// reason <c>order_cancelled</c> — distinct from R27's fact-driven
    /// <c>credit_rejected</c> reason, the exact contextual split
    /// <see cref="SagaCommandRequestFactory.StockReleaseReasonFor"/> makes
    /// for the fact-driven caller. This caller has no triggering fact at
    /// all, so it builds the request inline rather than going through that
    /// factory. The <c>triggeringEventEnvelope</c>/<c>Topic</c> pair below
    /// is the SAME id-71 synthetic envelope <see cref="BeginCreditReleaseCompensationAsync"/>
    /// stores, never the real request.
    /// </summary>
    private async Task<SagaCommandKind?> BeginStockReleaseCompensationAsync(Order order, string? note, CancellationToken cancellationToken)
    {
        var payload = new StockReleaseRequestPayload(order.OrderReference.Value, "order_cancelled");
        var payloadJson = System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));

        var requestId = UniqueId.New();
        var envelope = OperatorCancelRequestedEnvelope.Build(order.Id, requestId, clock.UtcNow, note);

        var outcome = await commandStore.EnqueueAsync(
            order.Id.Value,
            order.OrderReference.Value,
            SagaCommandKind.StockRelease,
            payloadJson,
            triggeringEventId: requestId.Value,
            triggeringEventEnvelope: envelope,
            triggeringEventTopic: OperatorCancelRequestedEnvelope.Topic,
            cancellationToken).ConfigureAwait(false);

        return outcome == EnqueueOutcome.Enqueued ? SagaCommandKind.StockRelease : null;
    }
}
