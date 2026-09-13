using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The <c>orders.cancel</c> command handler (feature
/// <c>orders_cancel_responder</c>, its <c>credit_approved</c>/<c>confirmed</c>
/// branch redesigned by SA-4 — the human-gated shared-spec amendment ruled
/// 2026-09-11, closing feature
/// <c>operator_cancel_races_saga_forward_progress</c>, id 62) —
/// operator-initiated cancellation, a NEW saga trigger distinct from the
/// fact-driven R19-R29 flow this Application layer already has: an RPC
/// request, not a consumed fact. <c>saga.md</c> §4.3's generalisation table
/// is the exact spec this class transcribes, branching on the order's
/// CURRENT status at the moment of the request:
/// <list type="bullet">
/// <item><c>stock_reserved</c>, <c>credit_approved</c> or <c>confirmed</c> —
/// releases stock FIRST (reason <c>order_cancelled</c>), via the SAME
/// durable <see cref="ISagaCommandStore"/> mechanism every other saga
/// command uses. At <c>stock_reserved</c> that is the ONLY acquisition; at
/// <c>credit_approved</c>/<c>confirmed</c> stock is also the CONTESTED
/// resource — a <c>despatch.create</c> may already be in flight for this
/// same reservation (§4.3's "The despatch already requested") — so
/// Fulfillment's own one-lock arbitration decides which of the two wins,
/// never this handler. The order stays where it is until the compensating
/// fact(s) arrive; the EXISTING, EXTENDED <see cref="SagaStepTable"/>
/// completes the chain (no orchestration written here for the second
/// step).</item>
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
/// (id 62), by SA-4's arbitration, not by a broad supersede guard.</b> Every
/// fact-driven saga step (<see cref="SagaFactHandler"/>) defends its
/// precondition with R25's equality-only check: a fact that finds the order
/// somewhere other than the expected status is safely ignored. This handler
/// reads the order's status and enqueues a compensating command inside ONE
/// transaction, while the saga's own FORWARD progress (e.g.
/// <c>credit.approved.v1</c> arriving and advancing
/// <c>stock_reserved</c> -&gt; <c>confirmed</c>, or <c>order.despatched.v1</c>
/// advancing past <c>confirmed</c>) runs in an INDEPENDENT transaction on an
/// INDEPENDENT consumer. Id 62's first pass closed this with two mechanisms:
/// the row lock below, unchanged, plus a broad "supersede any forward-progress
/// Advance step while a compensation is pending" guard in
/// <see cref="SagaFactHandler"/>. SA-4 (the human-gated shared-spec
/// amendment ruled 2026-09-11) found that guard strands a hold when a late
/// <c>credit.approved.v1</c> arrives AFTER the compensation has already
/// completed, and RETIRED it in favour of two narrower mechanisms: (1)
/// <see cref="IOrderRepository.GetByIdAsync"/> takes an explicit
/// <c>UPDLOCK, ROWLOCK</c> on the order row (<c>EfCoreOrderRepository</c>'s
/// own remarks), serialising this handler's read against any
/// TRUE-CONCURRENT <see cref="SagaFactHandler"/> transaction for the SAME
/// order — UNCHANGED from the first pass; (2) at <c>credit_approved</c>/<c>confirmed</c>
/// this handler now releases stock FIRST (the CONTESTED resource — a
/// <c>despatch.create</c> already in flight wants the SAME reservation) and
/// lets Fulfillment's own one-lock arbitration decide which of the two
/// wins (saga.md §4.3, "The despatch already requested") — no supersede
/// needed, because there is no longer a forward-progress step left for it
/// to race; and a late <c>credit.approved.v1</c> for an order whose
/// operator cancellation was already accepted (<c>stock_reserved</c> with
/// the compensation under way, or already <c>cancelled</c>) is handled
/// directly by <see cref="SagaFactHandler"/> (issues <c>credit.release</c>
/// only — §4.3, "A credit approval that arrives after the cancellation").
/// This closes the SAME class of race #7 found live and disclosed for the
/// identical mechanism (its own
/// <c>orders-cancel.integration.spec.ts:70-82</c>/<c>:138-152</c>) and
/// never fixed — see id 62's own
/// <c>progress/impl_operator_cancel_races_saga_forward_progress.md</c> for
/// the full reproduction/fix/arming record, including the "Rework pass 2 —
/// SA-4" section this SA-4 redesign is recorded under.
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
/// — instead <see cref="BeginStockReleaseCompensationAsync"/> (the ONE
/// direct enqueue site since SA-4 — both the <c>stock_reserved</c> and the
/// <c>credit_approved</c>/<c>confirmed</c> branch call it) stores it inside the
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
    IRpcRequestSerializer serializer,
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
                    // SA-4 — the SAME direct enqueue for all three
                    // compensating statuses: stock is released FIRST always
                    // (the only acquisition at stock_reserved; the
                    // CONTESTED resource at credit_approved/confirmed,
                    // where Fulfillment's own one-lock arbitration decides
                    // it against a despatch.create already in flight —
                    // saga.md §4.3, "The despatch already requested"). The
                    // reply's compensationPlanned names ONE hop from
                    // stock_reserved, TWO — stock then credit, in release
                    // order — from credit_approved/confirmed.
                    case OrderStatus.StockReserved or OrderStatus.CreditApproved or OrderStatus.Confirmed:
                        fastPathSignal = await BeginStockReleaseCompensationAsync(order, command.Note, ct).ConfigureAwait(false);
                        IReadOnlyList<string> compensationPlanned = order.Status == OrderStatus.StockReserved
                            ? ["stock_release"]
                            : ["stock_release", "credit_release"];
                        return new CancelOrderResult(order.Id.Value, order.OrderReference.Value, order.Status, CancellationReason: null, CompensationPlanned: compensationPlanned);

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
    /// SA-4 — the ONE direct enqueue site: enqueues <c>stock.release</c>
    /// with reason <c>order_cancelled</c> — distinct from R27's fact-driven
    /// <c>credit_rejected</c> reason, the exact contextual split
    /// <see cref="SagaCommandRequestFactory.StockReleaseReasonFor"/> makes
    /// for the fact-driven caller. This caller has no triggering fact at
    /// all, so it builds the request inline rather than going through that
    /// factory. Called from BOTH the <c>stock_reserved</c> branch (the only
    /// acquisition) and the <c>credit_approved</c>/<c>confirmed</c> branch
    /// (the contested resource, released first — §4.3, "The despatch
    /// already requested"); when the latter's release actually wins the
    /// race, <c>stock.released.v1</c>'s <c>credit_approved</c>/<c>confirmed</c>
    /// Advance variant (<see cref="SagaStepTable"/>) owes <c>credit.release</c>
    /// next, completing the chain with NO orchestration written here. The
    /// <c>triggeringEventEnvelope</c>/<c>Topic</c> pair below is id 71's
    /// synthetic <c>orders.cancel.requested</c> envelope, R29's dead-letter
    /// bookkeeping and this order's operator note, never the real request.
    /// </summary>
    private async Task<SagaCommandKind?> BeginStockReleaseCompensationAsync(Order order, string? note, CancellationToken cancellationToken)
    {
        var payload = new StockReleaseRequestPayload(order.OrderReference.Value, "order_cancelled");
        var payloadJson = System.Text.Encoding.UTF8.GetString(serializer.Serialize(payload));

        var requestId = UniqueId.New();
        var envelope = OperatorCancelRequestedEnvelope.Build(order.Id, requestId, clock.UtcNow, note, serializer);

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
