using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Projector.IntegrationTests.TestSupport;

/// <summary>
/// Builders for all fourteen facts — every field settable by the caller, so
/// <c>PR37</c>'s sentinels have a source to inject (<c>I1</c>).
/// </summary>
public static class EnvelopeBuilders
{
    public static Envelope<OrderPlacedPayload> OrderPlaced(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string buyerGln = "4006381333931", string supplierGln = "5001234567890", string currency = "EUR",
        DateTimeOffset? orderDate = null, IReadOnlyList<OrderLine>? lines = null,
        long initialAmount = 1000, long initialDiscount = 0, long totalAmount = 1000)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<OrderPlacedPayload>(
            eventId ?? Guid.NewGuid(), "order.placed.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new OrderPlacedPayload(orderReference, retailerCode, companyCode, buyerGln, supplierGln, currency,
                orderDate ?? DateTimeOffset.UtcNow, lines ?? [new OrderLine("SKU1", "Widget", 2, 500, 0)],
                initialAmount, initialDiscount, totalAmount));
    }

    public static Envelope<StockReservedPayload> StockReserved(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string companyCode = "COM01", IReadOnlyList<ReservationRef>? reservations = null, string? retailerCode = "RET01")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<StockReservedPayload>(
            eventId ?? Guid.NewGuid(), "stock.reserved.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new StockReservedPayload(orderReference, companyCode, reservations ?? [new ReservationRef(Guid.NewGuid(), "SKU1", 2)], retailerCode));
    }

    public static Envelope<StockRejectedPayload> StockRejected(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string companyCode = "COM01", IReadOnlyList<Shortage>? shortages = null, string reason = "insufficient_stock", string? retailerCode = "RET01")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<StockRejectedPayload>(
            eventId ?? Guid.NewGuid(), "stock.rejected.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new StockRejectedPayload(orderReference, companyCode, shortages ?? [new Shortage("SKU1", 5, 2)], reason, retailerCode));
    }

    public static Envelope<StockReleasedPayload> StockReleased(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string companyCode = "COM01", IReadOnlyList<ReservationRef>? released = null, string reason = "order_cancelled", string? retailerCode = "RET01")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<StockReleasedPayload>(
            eventId ?? Guid.NewGuid(), "stock.released.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new StockReleasedPayload(orderReference, companyCode, released ?? [new ReservationRef(Guid.NewGuid(), "SKU1", 2)], reason, retailerCode));
    }

    public static Envelope<CreditApprovedPayload> CreditApproved(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string creditCode = "CR-000001", string currency = "EUR", long heldAmount = 1000, long availableCreditAfter = 4000)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<CreditApprovedPayload>(
            eventId ?? Guid.NewGuid(), "credit.approved.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new CreditApprovedPayload(orderReference, retailerCode, companyCode, creditCode, currency, heldAmount, availableCreditAfter));
    }

    public static Envelope<CreditRejectedPayload> CreditRejected(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string currency = "EUR", long requestedAmount = 1000, long availableCredit = 200, string reason = "insufficient_credit")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<CreditRejectedPayload>(
            eventId ?? Guid.NewGuid(), "credit.rejected.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new CreditRejectedPayload(orderReference, retailerCode, companyCode, currency, requestedAmount, availableCredit, reason));
    }

    public static Envelope<CreditReleasedPayload> CreditReleased(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string currency = "EUR", long releasedAmount = 1000, long availableCreditAfter = 5000, string reason = "invoice_paid")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        return new Envelope<CreditReleasedPayload>(
            eventId ?? Guid.NewGuid(), "credit.released.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            new CreditReleasedPayload(orderReference, retailerCode, companyCode, currency, releasedAmount, availableCreditAfter, reason));
    }

    public static Envelope<OrderConfirmedPayload> OrderConfirmed(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01", string currency = "EUR", long totalAmount = 1000)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<OrderConfirmedPayload>(
            eventId ?? Guid.NewGuid(), "order.confirmed.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new OrderConfirmedPayload(orderReference, retailerCode, companyCode, currency, totalAmount, when));
    }

    public static Envelope<OrderDespatchedPayload> OrderDespatched(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string despatchReference = "DES-000001", string companyCode = "COM01", string retailerCode = "RET01", IReadOnlyList<DespatchLine>? lines = null)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<OrderDespatchedPayload>(
            eventId ?? Guid.NewGuid(), "order.despatched.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new OrderDespatchedPayload(orderReference, despatchReference, when, companyCode, retailerCode, lines ?? [new DespatchLine("SKU1", 2)]));
    }

    public static Envelope<InvoiceIssuedPayload> InvoiceIssued(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string invoiceReference = "INV-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string currency = "EUR", IReadOnlyList<InvoiceLine>? lines = null, long amount = 1000, long discount = 0, long totalAmount = 1000)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<InvoiceIssuedPayload>(
            eventId ?? Guid.NewGuid(), "invoice.issued.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new InvoiceIssuedPayload(orderReference, invoiceReference, when, retailerCode, companyCode, currency, lines ?? [new InvoiceLine("SKU1", 2, 500)], amount, discount, totalAmount));
    }

    public static Envelope<PaymentReceivedPayload> PaymentReceived(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string invoiceReference = "INV-000001", string paymentReference = "PAY-000001",
        string currency = "EUR", long amount = 1000, string source = "bank_transfer")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<PaymentReceivedPayload>(
            eventId ?? Guid.NewGuid(), "payment.received.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new PaymentReceivedPayload(orderReference, invoiceReference, paymentReference, currency, amount, when, source));
    }

    public static Envelope<OrderCompletedPayload> OrderCompleted(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01", string currency = "EUR", long totalAmount = 1000)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<OrderCompletedPayload>(
            eventId ?? Guid.NewGuid(), "order.completed.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new OrderCompletedPayload(orderReference, retailerCode, companyCode, currency, totalAmount, when));
    }

    public static Envelope<OrderCancelledPayload> OrderCancelled(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string retailerCode = "RET01", string companyCode = "COM01",
        string cancellationReason = "buyer_requested", IReadOnlyList<CompensationStep>? compensationSteps = null,
        string? note = null)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<OrderCancelledPayload>(
            eventId ?? Guid.NewGuid(), "order.cancelled.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new OrderCancelledPayload(orderReference, retailerCode, companyCode, cancellationReason, when, compensationSteps ?? [], note));
    }

    public static Envelope<OrderSagaFailedPayload> OrderSagaFailed(
        Guid? eventId = null, Guid? correlationId = null, Guid? causationId = null, DateTimeOffset? occurredAt = null,
        string orderReference = "ORD-000001", string command = "credit.hold", int attempts = 5, string lastError = "timeout")
    {
        var correlation = correlationId ?? Guid.NewGuid();
        var when = occurredAt ?? DateTimeOffset.UtcNow;
        return new Envelope<OrderSagaFailedPayload>(
            eventId ?? Guid.NewGuid(), "order.saga_failed.v1", correlation, correlation, causationId ?? Guid.NewGuid(),
            when, new OrderSagaFailedPayload(orderReference, command, attempts, lastError, when));
    }

    /// <summary>One builder per catalogued eventType — the completeness set <c>I1</c>'s test asserts against <c>FactCatalog</c>.</summary>
    public static readonly IReadOnlyDictionary<string, Func<object>> ByEventType = new Dictionary<string, Func<object>>(StringComparer.Ordinal)
    {
        ["order.placed.v1"] = () => OrderPlaced(),
        ["stock.reserved.v1"] = () => StockReserved(),
        ["stock.rejected.v1"] = () => StockRejected(),
        ["stock.released.v1"] = () => StockReleased(),
        ["credit.approved.v1"] = () => CreditApproved(),
        ["credit.rejected.v1"] = () => CreditRejected(),
        ["credit.released.v1"] = () => CreditReleased(),
        ["order.confirmed.v1"] = () => OrderConfirmed(),
        ["order.despatched.v1"] = () => OrderDespatched(),
        ["invoice.issued.v1"] = () => InvoiceIssued(),
        ["payment.received.v1"] = () => PaymentReceived(),
        ["order.completed.v1"] = () => OrderCompleted(),
        ["order.cancelled.v1"] = () => OrderCancelled(),
        ["order.saga_failed.v1"] = () => OrderSagaFailed(),
    };
}
