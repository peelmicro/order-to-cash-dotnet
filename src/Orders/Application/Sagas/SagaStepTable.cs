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
/// <c>Application/Sagas/</c>. Fourteen rows, four skips (SO2 — the fourth,
/// <c>order.saga_failed.v1</c>, did not exist when #7 wrote its own
/// thirteen-row / three-skip table).
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
    private static readonly FrozenDictionary<string, SagaStep> _rows = BuildRows().ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Looks up the step for a consumed <c>eventType</c> — absent (a future, uncatalogued fact) returns <see langword="null"/>, and the caller treats that identically to an explicit <see cref="SagaStep.Skip"/> (design.md §5.1 step 1).</summary>
    public static SagaStep? For(string eventType) => _rows.GetValueOrDefault(eventType);

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

    private static IEnumerable<KeyValuePair<string, SagaStep>> BuildRows()
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

        yield return Pair(
            "stock.released.v1",
            new SagaStep.Cancel(OrderStatus.StockReserved, MapReason, CompensationStepsFrom));

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

        yield return Pair(
            "credit.released.v1",
            new SagaStep.Advance(
                OrderStatus.Paid,
                (order, fact) => order.Complete(fact.OccurredAt, UniqueId.From(fact.EventId)),
                CommandAfter: null));

        // SO2 — the four facts the orchestrator produces itself. Consuming
        // them would be a loop; SagaFactsConsumer filters them out before any
        // I/O (design.md §3.5), and this row is the belt-and-braces second
        // layer.
        yield return Pair("order.confirmed.v1", new SagaStep.Skip());
        yield return Pair("order.completed.v1", new SagaStep.Skip());
        yield return Pair("order.cancelled.v1", new SagaStep.Skip());
        yield return Pair("order.saga_failed.v1", new SagaStep.Skip());
    }

    private static KeyValuePair<string, SagaStep> Pair(string eventType, SagaStep step) => new(eventType, step);
}
