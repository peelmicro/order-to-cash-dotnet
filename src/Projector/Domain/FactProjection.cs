using OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// <c>Project(FactEnvelope) : ProjectionDelta</c> — a pure, total function
/// from one fact to a store-agnostic description of its effect. A
/// <b>switch on <c>envelope.Payload</c>'s CLR type</b>, fourteen arms,
/// <c>_ =&gt; throw new UnknownFactTypeError(...)</c> — switching on the type
/// rather than on the <c>eventType</c> string is what makes <c>PR36</c>
/// structural: the fourteen <c>eventType</c> literals live in exactly one
/// table in the whole repository (<c>FactCatalog.PayloadTypesByEventType</c>),
/// and this file contains none of them in a routing position.
/// </summary>
public static class FactProjection
{
    public static ProjectionDelta Project(FactEnvelope envelope) => envelope.Payload switch
    {
        OrderPlacedPayload payload => ProjectOrderPlaced(envelope, payload),
        StockReservedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.StockReserved(payload)),
        StockRejectedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.StockRejected(payload)),
        StockReleasedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.StockReleased(payload)),
        CreditApprovedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.CreditApproved(payload)),
        CreditRejectedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.CreditRejected(payload)),
        CreditReleasedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.CreditReleased(payload)),
        OrderConfirmedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.OrderConfirmed(payload)),
        OrderDespatchedPayload payload => ProjectReference(
            envelope, payload.GetType(), Summaries.OrderDespatched(payload),
            despatchReference: payload.DespatchReference),
        InvoiceIssuedPayload payload => ProjectReference(
            envelope, payload.GetType(), Summaries.InvoiceIssued(payload),
            invoiceReference: payload.InvoiceReference),
        PaymentReceivedPayload payload => ProjectReference(
            envelope, payload.GetType(), Summaries.PaymentReceived(payload),
            paymentReference: payload.PaymentReference),
        OrderCompletedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.OrderCompleted(payload)),
        OrderCancelledPayload payload => ProjectCancellation(envelope, payload),
        OrderSagaFailedPayload payload => ProjectSimple(envelope, payload.GetType(), Summaries.OrderSagaFailed(payload)),
        _ => throw new UnknownFactTypeError(envelope.EventType),
    };

    private static ProjectionDelta ProjectOrderPlaced(FactEnvelope envelope, OrderPlacedPayload payload)
    {
        var header = new OrderHeaderDelta(
            payload.OrderReference,
            payload.OrderDate,
            payload.RetailerCode,
            payload.BuyerGln,
            payload.CompanyCode,
            payload.SupplierGln,
            payload.Currency,
            payload.InitialAmount,
            payload.InitialDiscount,
            payload.TotalAmount,
            [.. payload.Lines.Select(line => new OrderItemDelta(line.ProductCode, line.Quantity, line.UnitPrice, line.LineDiscount))]);

        return new ProjectionDelta(
            envelope.CorrelationId,
            BuildEntry(envelope, Summaries.OrderPlaced(payload)),
            OrderStatusRank.ImpliedStatusOf(typeof(OrderPlacedPayload)),
            OrderStatusRank.RankOf(typeof(OrderPlacedPayload)),
            CancellationReasonIfAbsent: null,
            DespatchReferenceIfAbsent: null,
            InvoiceReferenceIfAbsent: null,
            PaymentReferenceIfAbsent: null,
            Header: header);
    }

    private static ProjectionDelta ProjectCancellation(FactEnvelope envelope, OrderCancelledPayload payload) =>
        new(
            envelope.CorrelationId,
            BuildEntry(envelope, Summaries.OrderCancelled(payload)),
            OrderStatusRank.ImpliedStatusOf(typeof(OrderCancelledPayload)),
            OrderStatusRank.RankOf(typeof(OrderCancelledPayload)),
            CancellationReasonIfAbsent: payload.CancellationReason,
            DespatchReferenceIfAbsent: null,
            InvoiceReferenceIfAbsent: null,
            PaymentReferenceIfAbsent: null,
            Header: null);

    private static ProjectionDelta ProjectReference(
        FactEnvelope envelope,
        Type payloadType,
        SummaryResult summary,
        string? despatchReference = null,
        string? invoiceReference = null,
        string? paymentReference = null) =>
        new(
            envelope.CorrelationId,
            BuildEntry(envelope, summary),
            OrderStatusRank.ImpliedStatusOf(payloadType),
            OrderStatusRank.RankOf(payloadType),
            CancellationReasonIfAbsent: null,
            DespatchReferenceIfAbsent: despatchReference,
            InvoiceReferenceIfAbsent: invoiceReference,
            PaymentReferenceIfAbsent: paymentReference,
            Header: null);

    private static ProjectionDelta ProjectSimple(FactEnvelope envelope, Type payloadType, SummaryResult summary) =>
        new(
            envelope.CorrelationId,
            BuildEntry(envelope, summary),
            OrderStatusRank.ImpliedStatusOf(payloadType),
            OrderStatusRank.RankOf(payloadType),
            CancellationReasonIfAbsent: null,
            DespatchReferenceIfAbsent: null,
            InvoiceReferenceIfAbsent: null,
            PaymentReferenceIfAbsent: null,
            Header: null);

    /// <summary>
    /// <c>PR14</c>: every timestamp comes from the envelope's own
    /// <c>OccurredAt</c> — never a clock. <c>PR30</c>: <c>causationId</c>
    /// carried verbatim, never derived from <c>eventType</c>.
    /// </summary>
    private static TimelineEntryDelta BuildEntry(FactEnvelope envelope, SummaryResult summary) =>
        new(
            envelope.EventId,
            envelope.EventType,
            envelope.OccurredAt,
            summary.Summary,
            summary.Detail,
            envelope.CausationId);
}
