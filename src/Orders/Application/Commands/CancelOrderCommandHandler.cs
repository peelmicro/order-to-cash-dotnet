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
/// <b>A disclosed race, not fixed here.</b> Every fact-driven saga step
/// (<see cref="SagaFactHandler"/>) defends its precondition with R25's
/// equality-only check: a fact that finds the order somewhere other than
/// the expected status is safely ignored. This handler reads the order's
/// status and enqueues a compensating command inside ONE transaction, but
/// the saga's own FORWARD progress (e.g. <c>credit.approved.v1</c> arriving
/// and advancing <c>stock_reserved</c> -&gt; <c>credit_approved</c> -&gt;
/// <c>confirmed</c>) runs in an INDEPENDENT transaction on an INDEPENDENT
/// consumer, and nothing serialises the two against each other. If the
/// order advances PAST the status this handler observed before the
/// compensating fact (<c>stock.released.v1</c> or <c>credit.released.v1</c>)
/// arrives, R25's precondition-unmet check — correctly, by design — ignores
/// that fact, which can strand a released resource on an order that keeps
/// progressing (e.g. a genuinely-released reservation on an order R25 lets
/// carry on toward <c>despatched</c>). This is the SAME class of race #7
/// found live and disclosed for the identical mechanism, worse on the
/// <c>credit_approved</c>/<c>confirmed</c> branch than the <c>stock_reserved</c>
/// one (see <c>progress/impl_orders_cancel_responder.md</c>'s "a genuinely
/// new finding" section) — a real, narrow window, not fixed here: closing it
/// needs either a transactional re-check immediately before a forward-progress
/// command's own fast-path dispatch, or this handler actively superseding an
/// already-owed forward command, both genuine saga-design decisions outside
/// this feature's bounded scope.
///
/// <b>The operator note (acceptance bullet 4) now reaches the read-model
/// timeline — feature <c>operator_note_reaches_the_timeline</c>, SA-2.</b>
/// <c>asyncapi.yaml</c>'s <c>OrderCancelledPayload</c> gained an optional
/// <c>note</c> field (SA-2, the amendment this feature's brief exists to
/// apply), so this handler now threads <see cref="CancelOrderCommand.Note"/>
/// into <see cref="Order.Cancel"/> on the ONE branch that cancels
/// synchronously and has a note to carry — the immediate/<c>default</c>
/// branch below (an order in <c>placed</c>, or a terminal status
/// <see cref="Order.Cancel"/> itself refuses). The
/// <c>stock_reserved</c>/<c>credit_approved</c>/<c>confirmed</c> branches
/// enqueue compensation and let a LATER, fact-driven call to
/// <see cref="Order.Cancel"/> (<c>SagaFactHandler</c>, an independent
/// transaction reacting to <c>stock.released.v1</c>/<c>credit.released.v1</c>)
/// complete the cancellation — that caller has no access to this request's
/// note (neither <c>StockReleaseRequestPayload</c> nor
/// <c>CreditReleaseRequestPayload</c> carries one; SA-2 touched only
/// <c>OrderCancelledPayload</c>), so a note supplied against one of those
/// two branches is genuinely NOT carried to the eventual timeline entry —
/// disclosed here rather than silently dropped, and out of this feature's
/// bounded scope (its brief names <c>src/Contracts/</c>, <c>src/Orders/</c>,
/// <c>src/Projector/</c> and does not ask for a new wire field on either
/// release request).
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
                        fastPathSignal = await BeginCreditReleaseCompensationAsync(order, ct).ConfigureAwait(false);
                        return new CancelOrderResult(order.Id.Value, order.OrderReference.Value, order.Status, CancellationReason: null, CompensationPlanned: ["credit_release", "stock_release"]);

                    case OrderStatus.StockReserved:
                        fastPathSignal = await BeginStockReleaseCompensationAsync(order, ct).ConfigureAwait(false);
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
    /// header comment named as this feature's job, not its own.
    /// </summary>
    private async Task<SagaCommandKind?> BeginCreditReleaseCompensationAsync(Order order, CancellationToken cancellationToken)
    {
        var payload = new CreditReleaseRequestPayload(order.OrderReference.Value, order.RetailerCode, order.CompanyCode);
        var payloadJson = System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));

        var outcome = await commandStore.EnqueueAsync(
            order.Id.Value,
            order.OrderReference.Value,
            SagaCommandKind.CreditRelease,
            payloadJson,
            triggeringEventId: UniqueId.New().Value,
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
    /// factory.
    /// </summary>
    private async Task<SagaCommandKind?> BeginStockReleaseCompensationAsync(Order order, CancellationToken cancellationToken)
    {
        var payload = new StockReleaseRequestPayload(order.OrderReference.Value, "order_cancelled");
        var payloadJson = System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));

        var outcome = await commandStore.EnqueueAsync(
            order.Id.Value,
            order.OrderReference.Value,
            SagaCommandKind.StockRelease,
            payloadJson,
            triggeringEventId: UniqueId.New().Value,
            cancellationToken).ConfigureAwait(false);

        return outcome == EnqueueOutcome.Enqueued ? SagaCommandKind.StockRelease : null;
    }
}
