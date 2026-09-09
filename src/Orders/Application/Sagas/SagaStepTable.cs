using System.Collections.Frozen;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// A direct transcription of <c>specs/shared/saga.md</c> §3.1 and §4 plus the
/// consumption map §5 — a static, declarative map from <c>eventType</c> to a
/// <see cref="SagaStep"/> (design.md §4.1). Pure data and pure functions:
/// <b>zero</b> <c>Microsoft.*</c>, <c>Confluent.*</c>, <c>NATS.*</c>, EF Core
/// or <c>System.Text.Json</c> reference in this file or its neighbours in
/// <c>Application/Sagas/</c>. Fourteen rows (<c>eventType</c> keys), four
/// skips (SO2 — the fourth, <c>order.saga_failed.v1</c>, did not exist when
/// #7 wrote its own thirteen-row / three-skip table). Two of the fourteen
/// rows, <c>credit.released.v1</c> and <c>stock.released.v1</c>, carry MORE
/// THAN ONE <see cref="SagaStep"/> variant each (feature
/// <c>orders_cancel_responder</c>'s operator-cancel compensation for an
/// order that already held a credit hold) — see <see cref="Variants"/> and
/// <see cref="ForStatus"/>.
/// </summary>
/// <remarks>
/// <c>occurredAt</c> and <c>causationId</c> on every emitted aggregate call
/// come from the CONSUMED FACT, never from a clock: <c>occurredAt</c> is
/// <see cref="SagaFact.OccurredAt"/> (the moment the fact became true in the
/// domain, not the moment it was consumed) and <c>causationId</c> is
/// <see cref="SagaFact.EventId"/>. This is what makes every fact this
/// feature causes the aggregate to emit chain correctly for R12.
/// </remarks>
public static class SagaStepTable
{
    private static readonly FrozenDictionary<string, IReadOnlyList<SagaStep>> _rows = BuildRows().ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Looks up the SINGLE step for a consumed <c>eventType</c> — absent (a
    /// future, uncatalogued fact) returns <see langword="null"/>, and the
    /// caller treats that identically to an explicit <see cref="SagaStep.Skip"/>
    /// (design.md §5.1 step 1). Works unchanged for every fact type EXCEPT
    /// <c>credit.released.v1</c> and <c>stock.released.v1</c> (feature
    /// <c>orders_cancel_responder</c>'s operator-cancel compensation gives
    /// both of those more than one legal precondition) — for those two, this
    /// throws rather than silently picking one; callers that need to resolve
    /// by the order's CURRENT status must use <see cref="ForStatus"/>.
    /// </summary>
    public static SagaStep? For(string eventType)
    {
        var variants = _rows.GetValueOrDefault(eventType);
        if (variants is null)
        {
            return null;
        }

        if (variants.Count > 1)
        {
            throw new InvalidOperationException(
                $"SagaStepTable.For(\"{eventType}\") is ambiguous — {variants.Count} variants exist; use SagaStepTable.ForStatus(eventType, status) instead.");
        }

        return variants[0];
    }

    /// <summary>Every variant catalogued for <c>eventType</c> — one element for twelve of the fourteen fact types, three for <c>credit.released.v1</c> and <c>stock.released.v1</c>. <see langword="null"/> for an uncatalogued <c>eventType</c>.</summary>
    public static IReadOnlyList<SagaStep>? Variants(string eventType) => _rows.GetValueOrDefault(eventType);

    /// <summary>
    /// The variant (if any) whose precondition equals <paramref name="status"/>
    /// — the general form of R25's equality-only precondition check, now
    /// covering an <c>eventType</c> with more than one legal precondition.
    /// For a single-variant <c>eventType</c> this is exactly the original
    /// equality check; <see langword="null"/> means no catalogued variant
    /// applies (the caller records <c>PreconditionUnmet</c>, generalised the
    /// same way). Never called for <see cref="SagaStep.Skip"/> rows — those
    /// are filtered out by the caller before any status is even loaded
    /// (design.md §5.1 step 1); <see cref="PreconditionOf"/> would throw on
    /// one.
    /// </summary>
    public static SagaStep? ForStatus(string eventType, OrderStatus status)
    {
        var variants = _rows.GetValueOrDefault(eventType);
        return variants?.FirstOrDefault(step => PreconditionOf(step) == status);
    }

    private static OrderStatus PreconditionOf(SagaStep step) => step switch
    {
        SagaStep.Advance advance => advance.Precondition,
        SagaStep.Cancel cancel => cancel.Precondition,
        _ => throw new InvalidOperationException($"SagaStepTable.PreconditionOf: unexpected step shape {step} — Skip rows have no precondition and must never reach here."),
    };

    /// <summary>
    /// <c>stock.released.v1</c>'s reason mapping (SO7, R28) —
    /// <c>credit_rejected</c> → <see cref="CancellationReason.CreditRejected"/>,
    /// <c>order_cancelled</c> → <see cref="CancellationReason.OperatorCancelled"/>.
    /// Both are legal from <see cref="OrderStatus.StockReserved"/>; an
    /// illegal pairing is refused by <see cref="Domain.Order.Cancel"/> itself
    /// (<see cref="Domain.Errors.CancellationReasonNotApplicableError"/>),
    /// not by this mapping.
    /// </summary>
    public static CancellationReason MapReason(SagaFact fact)
    {
        var payload = (StockReleasedPayload)fact.Payload;

        return payload.Reason switch
        {
            "credit_rejected" => CancellationReason.CreditRejected,
            "order_cancelled" => CancellationReason.OperatorCancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(fact), payload.Reason, "stock.released.v1 carried a reason outside the closed set {credit_rejected, order_cancelled}."),
        };
    }

    /// <summary>
    /// One compensation step, built from the observed <c>stock.released.v1</c>
    /// fact itself (SO7) — the aggregate never observes the compensating fact
    /// directly; the orchestrator supplies it.
    /// </summary>
    public static IReadOnlyList<OrderCompensationStep> CompensationStepsFrom(SagaFact fact) =>
        [new OrderCompensationStep(CompensationStepKind.StockReleased, UniqueId.From(fact.EventId), fact.EventType, fact.OccurredAt, Summary: null)];

    /// <summary>
    /// <c>stock.released.v1</c>'s <c>credit_approved</c>/<c>confirmed</c>
    /// variants (feature <c>orders_cancel_responder</c>) unwind TWO
    /// acquisitions, not one — credit hold, then stock reservation, reverse
    /// order of acquisition (saga.md §4.3 point 3: "both steps must be
    /// visible"). This function has no cross-fact state anywhere in this
    /// codebase to source the EARLIER <c>credit.released.v1</c> fact's own
    /// <c>eventId</c>/<c>occurredAt</c> from — <see cref="SagaFact"/> only
    /// ever carries the ONE fact currently being processed (here,
    /// <c>stock.released.v1</c> itself). The synthesised <c>credit_released</c>
    /// entry below therefore carries NO <c>eventId</c> (the wire schema's own
    /// field is optional, <c>CompensationStep.eventId</c>) and reuses the
    /// CURRENT fact's <c>occurredAt</c> rather than fabricate an earlier one
    /// — a disclosed limitation, not a silent gap, matching #7's identical
    /// trade-off (<c>saga-steps.ts:79-97</c>'s own header comment, ledger row
    /// below).
    /// </summary>
    public static IReadOnlyList<OrderCompensationStep> CompensationStepsFromCreditThenStockRelease(SagaFact fact) =>
    [
        new OrderCompensationStep(
            CompensationStepKind.CreditReleased,
            EventId: null,
            EventType: "credit.released.v1",
            OccurredAt: fact.OccurredAt,
            Summary: "credit released — reason: order_cancelled (reverse order of acquisition, released before stock)"),
        .. CompensationStepsFrom(fact),
    ];

    private static IEnumerable<KeyValuePair<string, IReadOnlyList<SagaStep>>> BuildRows()
    {
        yield return Pair(
            "order.placed.v1",
            new SagaStep.Advance(OrderStatus.Placed, Apply: null, SagaCommandKind.StockReserve));

        yield return Pair(
            "stock.reserved.v1",
            new SagaStep.Advance(
                OrderStatus.Placed,
                (order, fact) => order.MarkStockReserved(fact.OccurredAt),
                SagaCommandKind.CreditHold));

        yield return Pair(
            "stock.rejected.v1",
            new SagaStep.Cancel(
                OrderStatus.Placed,
                Reason: static _ => CancellationReason.StockRejected,
                CompensationSteps: static _ => []));

        yield return Pair(
            "credit.approved.v1",
            new SagaStep.Advance(
                OrderStatus.StockReserved,
                (order, fact) =>
                {
                    order.ApproveCredit(fact.OccurredAt);
                    order.Confirm(fact.OccurredAt, UniqueId.From(fact.EventId));
                },
                SagaCommandKind.DespatchCreate));

        yield return Pair(
            "credit.rejected.v1",
            new SagaStep.Advance(OrderStatus.StockReserved, Apply: null, SagaCommandKind.StockRelease));

        // stock.released.v1 — three variants (feature orders_cancel_responder).
        // The original stock_reserved variant (R28/SO7) is UNCHANGED; the two
        // new ones complete the operator-cancel compensation for an order
        // that already held a credit hold when the operator cancelled it —
        // credit.release was issued and processed FIRST (the next row down),
        // and this fact arrives while the order is STILL credit_approved/
        // confirmed (that earlier step's own apply is a no-op, mirroring
        // credit.rejected.v1's R27 no-op).
        yield return PairVariants(
            "stock.released.v1",
            [
                new SagaStep.Cancel(OrderStatus.StockReserved, MapReason, CompensationStepsFrom),
                new SagaStep.Cancel(OrderStatus.CreditApproved, MapReason, CompensationStepsFromCreditThenStockRelease),
                new SagaStep.Cancel(OrderStatus.Confirmed, MapReason, CompensationStepsFromCreditThenStockRelease),
            ]);

        yield return Pair(
            "order.despatched.v1",
            new SagaStep.Advance(
                OrderStatus.Confirmed,
                (order, fact) => order.MarkDespatched(fact.OccurredAt),
                SagaCommandKind.InvoiceIssue));

        yield return Pair(
            "invoice.issued.v1",
            new SagaStep.Advance(
                OrderStatus.Despatched,
                (order, fact) => order.MarkInvoiced(fact.OccurredAt),
                CommandAfter: null));

        yield return Pair(
            "payment.received.v1",
            new SagaStep.Advance(
                OrderStatus.Invoiced,
                (order, fact) => order.MarkPaid(fact.OccurredAt),
                CommandAfter: null));

        // credit.released.v1 — three variants (feature orders_cancel_responder).
        // The original paid variant (R24) is UNCHANGED. The two new ones are
        // the FIRST step of the credit_approved/confirmed operator-cancel
        // compensation (saga.md §4.3): apply is a no-op (status stays where
        // it is, mirroring credit.rejected.v1's R27 no-op) and the step owes
        // stock.release next — the reverse-order-of-acquisition chain
        // completes when THAT fact's own stock.released.v1 variant (above)
        // fires the cancellation.
        yield return PairVariants(
            "credit.released.v1",
            [
                new SagaStep.Advance(
                    OrderStatus.Paid,
                    (order, fact) => order.Complete(fact.OccurredAt, UniqueId.From(fact.EventId)),
                    CommandAfter: null),
                new SagaStep.Advance(OrderStatus.CreditApproved, Apply: null, SagaCommandKind.StockRelease),
                new SagaStep.Advance(OrderStatus.Confirmed, Apply: null, SagaCommandKind.StockRelease),
            ]);

        // SO2 — the four facts the orchestrator produces itself. Consuming
        // them would be a loop; SagaFactsConsumer filters them out before any
        // I/O (design.md §3.5), and this row is the belt-and-braces second
        // layer.
        yield return Pair("order.confirmed.v1", new SagaStep.Skip());
        yield return Pair("order.completed.v1", new SagaStep.Skip());
        yield return Pair("order.cancelled.v1", new SagaStep.Skip());
        yield return Pair("order.saga_failed.v1", new SagaStep.Skip());
    }

    private static KeyValuePair<string, IReadOnlyList<SagaStep>> Pair(string eventType, SagaStep step) => new(eventType, [step]);

    private static KeyValuePair<string, IReadOnlyList<SagaStep>> PairVariants(string eventType, IReadOnlyList<SagaStep> steps) => new(eventType, steps);
}
