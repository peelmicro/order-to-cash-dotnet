using System.Globalization;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Seed.Domain.Data;
using OrderToCash.Seed.Domain.Deterministic;
using OrderToCash.SharedKernel;

namespace OrderToCash.Seed.Domain.Sagas;

/// <summary>
/// The fabricated saga history — ported from #7's
/// <c>apps/seed/src/data/sagas.data.ts</c>: 5 <c>completed</c> orders and
/// exactly 1 <c>cancelled</c> order (reason <c>credit_rejected</c>, its
/// total ending <c>.99</c> — the credit simulator's
/// <c>simulated_cents_rule</c> affordance). Every fact of every saga is
/// built here ONCE and then fanned out by the Infrastructure writers into
/// the three MS-SQL databases (as already-published outbox rows) and the
/// MongoDB <c>order_timeline</c> document, so the four stores can never
/// disagree about what happened.
///
/// <b>References.</b> <c>ORD-000001..006</c>, <c>DES-000001..005</c>,
/// <c>INV-000001..005</c> are consumed by this seed. This seed must run
/// before any live order, since it owns sequences 1..6 (or 1..5) outright
/// on an empty database — matching #7's own documented precondition, and
/// deliberately NOT reconciled against the number-sequence tables here
/// (see progress/impl_seed_job.md).
///
/// <b>Fact ordering (occurredAt).</b> Every completed saga follows the
/// happy path step table of specs/shared/saga.md §3.1 exactly, one instant
/// apart; the cancelled saga follows §4.2 (Path B — release, then cancel).
/// <c>aggregateId</c> is the id of the aggregate that actually produced the
/// fact, matching specs/shared/asyncapi.yaml's own examples — never the
/// order id for facts Orders did not produce itself.
///
/// <b>Causal chain (causationId).</b> The two root facts
/// (<c>order.placed.v1</c>, <c>payment.received.v1</c>) cite a synthetic
/// deterministic command id; every other fact cites the eventId of the fact
/// that triggered it — one link shorter than a live saga's causal chain,
/// but complete and reconstructible, per specs/outbox_and_idempotency/design.md §3.5.
/// </summary>
public static class SagaFixtures
{
    public static readonly DateTime BaseDate = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Deterministic id of the Fulfillment <c>StockItem</c> row for one (company, product) pair — shared with the Stock builder so both agree on the same id.</summary>
    public static Guid StockRowId(string companyCode, string productCode) =>
        DeterministicId.Of($"stock:{companyCode}:{productCode}");

    public static readonly IReadOnlyList<OrderSagaFixture> All =
    [
        BuildCompleted(new BuildInput(
            1,
            "CarrefourEs",
            "IBERFOODS",
            BaseDate,
            [
                new LineInput("PRD-0002", 5),
                new LineInput("PRD-0003", 3),
            ])),
        BuildCompleted(new BuildInput(
            2,
            "CarrefourFr",
            "FRESHFR",
            BaseDate.AddDays(1),
            [
                new LineInput("PRD-0002", 4),
                new LineInput("PRD-0008", 2),
            ])),
        BuildCompleted(new BuildInput(
            3,
            "LeroyMerlinEs",
            "TOOLIBERIA",
            BaseDate.AddDays(2),
            [
                new LineInput("PRD-0004", 10),
                new LineInput("PRD-0005", 6),
            ])),
        BuildCompleted(new BuildInput(
            4,
            "AldiDe",
            "GERMANFOODS",
            BaseDate.AddDays(3),
            [
                new LineInput("PRD-0002", 8),
                new LineInput("PRD-0003", 4),
            ])),
        BuildCompleted(new BuildInput(
            5,
            "AldiGb",
            "UKDISTRIB",
            BaseDate.AddDays(4),
            [
                new LineInput("PRD-0009", 20),
                new LineInput("PRD-0010", 15),
            ])),
        BuildCancelled(new BuildInput(
            6,
            "CarrefourEs",
            "IBERFOODS",
            BaseDate.AddDays(5),
            [
                new LineInput("PRD-0001", 1),
            ])),
    ];

    public static readonly IReadOnlyList<OrderSagaFixture> Completed =
        [.. All.Where(saga => saga.Status == "completed")];

    public static readonly IReadOnlyList<OrderSagaFixture> Cancelled =
        [.. All.Where(saga => saga.Status == "cancelled")];

    private sealed record LineInput(string ProductCode, int Quantity, long? UnitPrice = null, long LineDiscount = 0);

    private sealed record BuildInput(int Sequence, string RetailerCode, string CompanyCode, DateTime OrderDate, IReadOnlyList<LineInput> Lines);

    private static IReadOnlyList<OrderLineFixture> ResolveLines(IReadOnlyList<LineInput> lines) =>
        [.. lines.Select(line =>
        {
            var product = Products.ByCode(line.ProductCode);
            return new OrderLineFixture(
                line.ProductCode,
                product.Name,
                line.Quantity,
                line.UnitPrice ?? product.Price,
                line.LineDiscount);
        })];

    private static (long InitialAmount, long InitialDiscount, long TotalAmount) SumLines(IReadOnlyList<OrderLineFixture> lines)
    {
        var initialAmount = lines.Sum(line => line.UnitPrice * line.Quantity);
        var initialDiscount = lines.Sum(line => line.LineDiscount);
        return (initialAmount, initialDiscount, initialAmount - initialDiscount);
    }

    private static IReadOnlyList<OrderLine> OrderPlacedLines(IReadOnlyList<OrderLineFixture> lines) =>
        [.. lines.Select(line => new OrderLine(line.ProductCode, line.Description, line.Quantity, line.UnitPrice, line.LineDiscount))];

    /// <summary>Builds one completed saga — the full happy path of specs/shared/saga.md §3.1.</summary>
    private static OrderSagaFixture BuildCompleted(BuildInput input)
    {
        var retailer = Retailers.ByCode(input.RetailerCode);
        var company = Companies.ByCode(input.CompanyCode);
        var currency = Currencies.All.First(c => c.Code == retailer.CurrencyCode);
        var lines = ResolveLines(input.Lines);
        var (initialAmount, initialDiscount, totalAmount) = SumLines(lines);
        var t0 = input.OrderDate;

        var orderId = DeterministicId.Of($"order:{input.Sequence}");
        var orderReference = new OrderNumber(input.Sequence).Value;
        var credit = Credits.ByRetailerAndCompany(input.RetailerCode, input.CompanyCode);
        var despatchId = DeterministicId.Of($"order:{input.Sequence}:despatch");
        var despatchReference = BusinessReference.Despatch(input.Sequence);
        var invoiceId = DeterministicId.Of($"order:{input.Sequence}:invoice");
        var invoiceReference = BusinessReference.Invoice(input.Sequence);
        var paymentId = DeterministicId.Of($"order:{input.Sequence}:payment");
        var paymentReference = $"PAY-SEED-{input.Sequence.ToString("D6", CultureInfo.InvariantCulture)}";
        var firstStockItemId = StockRowId(input.CompanyCode, lines[0].ProductCode);

        var tStockReserved = t0.AddMinutes(1);
        var tCreditApproved = t0.AddMinutes(2);
        var tOrderConfirmed = tCreditApproved.AddSeconds(30);
        var tDespatched = t0.AddMinutes(3);
        var tInvoiceIssued = t0.AddMinutes(4);
        var tPaymentReceived = t0.AddDays(1);
        var tCreditReleased = tPaymentReceived.AddSeconds(5);
        var tCompleted = tPaymentReceived.AddSeconds(10);

        Guid EventId(string eventType) => DeterministicId.Of($"order:{input.Sequence}:event:{eventType}");
        Guid OutboxId(string eventType) => DeterministicId.Of($"order:{input.Sequence}:outbox:{eventType}");

        var reservations = lines
            .Select(line => new ReservationFixture(
                DeterministicId.Of($"order:{input.Sequence}:reservation:{line.ProductCode}"),
                input.CompanyCode,
                input.RetailerCode,
                line.ProductCode,
                line.Quantity,
                "consumed",
                tStockReserved,
                tDespatched))
            .ToArray();

        var reservationRefs = reservations
            .Select(r => new ReservationRef(r.Id, r.ProductCode, r.Units))
            .ToArray();

        var despatchLines = lines.Select(line => new DespatchLine(line.ProductCode, line.Quantity)).ToArray();
        var invoiceLines = lines.Select(line => new InvoiceLine(line.ProductCode, line.Quantity, line.UnitPrice)).ToArray();

        var orderPlacedPayload = new OrderPlacedPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            retailer.Gln,
            company.Gln,
            currency.Code,
            t0,
            OrderPlacedLines(lines),
            initialAmount,
            initialDiscount,
            totalAmount);

        var stockReservedPayload = new StockReservedPayload(orderReference, input.CompanyCode, reservationRefs, input.RetailerCode);

        var creditApprovedPayload = new CreditApprovedPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            credit.Code,
            currency.Code,
            totalAmount,
            credit.CreditLimit - totalAmount);

        var orderConfirmedPayload = new OrderConfirmedPayload(orderReference, input.RetailerCode, input.CompanyCode, currency.Code, totalAmount, tOrderConfirmed);

        var orderDespatchedPayload = new OrderDespatchedPayload(orderReference, despatchReference, tDespatched, input.CompanyCode, input.RetailerCode, despatchLines);

        var invoiceIssuedPayload = new InvoiceIssuedPayload(
            orderReference,
            invoiceReference,
            tInvoiceIssued,
            input.RetailerCode,
            input.CompanyCode,
            currency.Code,
            invoiceLines,
            initialAmount,
            initialDiscount,
            totalAmount);

        var paymentReceivedPayload = new PaymentReceivedPayload(orderReference, invoiceReference, paymentReference, currency.Code, totalAmount, tPaymentReceived, "test");

        var creditReleasedPayload = new CreditReleasedPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            currency.Code,
            totalAmount,
            credit.CreditLimit,
            "invoice_paid",
            credit.Code);

        var orderCompletedPayload = new OrderCompletedPayload(orderReference, input.RetailerCode, input.CompanyCode, currency.Code, totalAmount, tCompleted);

        var orderPlacedEventId = EventId("order.placed.v1");
        var stockReservedEventId = EventId("stock.reserved.v1");
        var creditApprovedEventId = EventId("credit.approved.v1");
        var orderConfirmedEventId = EventId("order.confirmed.v1");
        var orderDespatchedEventId = EventId("order.despatched.v1");
        var invoiceIssuedEventId = EventId("invoice.issued.v1");
        var paymentReceivedEventId = EventId("payment.received.v1");
        var creditReleasedEventId = EventId("credit.released.v1");
        var orderCompletedEventId = EventId("order.completed.v1");

        var orderPlacedCausationId = DeterministicId.Of($"order:{input.Sequence}:command:orders.create");
        var stockReservedCausationId = orderPlacedEventId;
        var creditApprovedCausationId = stockReservedEventId;
        var orderConfirmedCausationId = creditApprovedEventId;
        var orderDespatchedCausationId = creditApprovedEventId;
        var invoiceIssuedCausationId = orderDespatchedEventId;
        var paymentReceivedCausationId = DeterministicId.Of($"order:{input.Sequence}:command:payment.register");
        var creditReleasedCausationId = paymentReceivedEventId;
        var orderCompletedCausationId = creditReleasedEventId;

        var ordersOutbox = new[]
        {
            new OutboxFixture(OutboxId("order.placed.v1"), orderPlacedEventId, "order.placed.v1", orderId, orderId, orderPlacedCausationId, orderPlacedPayload, t0, t0),
            new OutboxFixture(OutboxId("order.confirmed.v1"), orderConfirmedEventId, "order.confirmed.v1", orderId, orderId, orderConfirmedCausationId, orderConfirmedPayload, tOrderConfirmed, tOrderConfirmed),
            new OutboxFixture(OutboxId("order.completed.v1"), orderCompletedEventId, "order.completed.v1", orderId, orderId, orderCompletedCausationId, orderCompletedPayload, tCompleted, tCompleted),
        };

        var fulfillmentOutbox = new[]
        {
            new OutboxFixture(OutboxId("stock.reserved.v1"), stockReservedEventId, "stock.reserved.v1", firstStockItemId, orderId, stockReservedCausationId, stockReservedPayload, tStockReserved, tStockReserved),
            new OutboxFixture(OutboxId("order.despatched.v1"), orderDespatchedEventId, "order.despatched.v1", despatchId, orderId, orderDespatchedCausationId, orderDespatchedPayload, tDespatched, tDespatched),
        };

        var billingOutbox = new[]
        {
            new OutboxFixture(OutboxId("credit.approved.v1"), creditApprovedEventId, "credit.approved.v1", credit.Id, orderId, creditApprovedCausationId, creditApprovedPayload, tCreditApproved, tCreditApproved),
            new OutboxFixture(OutboxId("invoice.issued.v1"), invoiceIssuedEventId, "invoice.issued.v1", invoiceId, orderId, invoiceIssuedCausationId, invoiceIssuedPayload, tInvoiceIssued, tInvoiceIssued),
            new OutboxFixture(OutboxId("payment.received.v1"), paymentReceivedEventId, "payment.received.v1", invoiceId, orderId, paymentReceivedCausationId, paymentReceivedPayload, tPaymentReceived, tPaymentReceived),
            new OutboxFixture(OutboxId("credit.released.v1"), creditReleasedEventId, "credit.released.v1", credit.Id, orderId, creditReleasedCausationId, creditReleasedPayload, tCreditReleased, tCreditReleased),
        };

        var timeline = new[]
        {
            new TimelineEntryFixture(orderPlacedEventId, "order.placed.v1", t0, $"Order {orderReference} placed for {input.RetailerCode}", orderPlacedCausationId),
            new TimelineEntryFixture(stockReservedEventId, "stock.reserved.v1", tStockReserved, $"Stock reserved for {reservations.Length} line(s)", stockReservedCausationId),
            new TimelineEntryFixture(creditApprovedEventId, "credit.approved.v1", tCreditApproved, $"Credit hold of {totalAmount} {currency.Code} approved", creditApprovedCausationId),
            new TimelineEntryFixture(orderConfirmedEventId, "order.confirmed.v1", tOrderConfirmed, "Order confirmed (ORDRSP)", orderConfirmedCausationId),
            new TimelineEntryFixture(orderDespatchedEventId, "order.despatched.v1", tDespatched, $"Despatch {despatchReference} created", orderDespatchedCausationId),
            new TimelineEntryFixture(invoiceIssuedEventId, "invoice.issued.v1", tInvoiceIssued, $"Invoice {invoiceReference} issued", invoiceIssuedCausationId),
            new TimelineEntryFixture(paymentReceivedEventId, "payment.received.v1", tPaymentReceived, $"Payment {paymentReference} received", paymentReceivedCausationId),
            new TimelineEntryFixture(creditReleasedEventId, "credit.released.v1", tCreditReleased, "Credit exposure released — invoice paid", creditReleasedCausationId),
            new TimelineEntryFixture(orderCompletedEventId, "order.completed.v1", tCompleted, $"Order {orderReference} completed", orderCompletedCausationId),
        };

        return new OrderSagaFixture(
            input.Sequence,
            orderId,
            orderReference,
            t0,
            input.RetailerCode,
            input.CompanyCode,
            currency.Code,
            "completed",
            null,
            lines,
            initialAmount,
            initialDiscount,
            totalAmount,
            tCompleted,
            ordersOutbox,
            reservations,
            new DespatchFixture(
                despatchId,
                despatchReference,
                tDespatched,
                input.CompanyCode,
                input.RetailerCode,
                [.. lines.Select(line => new DespatchItemFixture(line.ProductCode, line.Quantity))]),
            fulfillmentOutbox,
            [
                new CreditLedgerEntryFixture(DeterministicId.Of($"order:{input.Sequence}:credit-item:hold"), credit.Id, orderReference, totalAmount, "hold", tCreditApproved),
                new CreditLedgerEntryFixture(DeterministicId.Of($"order:{input.Sequence}:credit-item:consume"), credit.Id, orderReference, totalAmount, "consume", tInvoiceIssued),
                new CreditLedgerEntryFixture(DeterministicId.Of($"order:{input.Sequence}:credit-item:release"), credit.Id, orderReference, totalAmount, "release", tCreditReleased),
            ],
            new InvoiceFixture(
                invoiceId,
                invoiceReference,
                tInvoiceIssued,
                initialAmount,
                initialDiscount,
                totalAmount,
                "paid",
                tPaymentReceived,
                [.. lines.Select(line => new InvoiceItemFixture(line.ProductCode, line.Quantity, line.UnitPrice))],
                new PaymentFixture(paymentId, paymentReference, totalAmount, tPaymentReceived, "test")),
            billingOutbox,
            timeline);
    }

    /// <summary>Builds the one cancelled saga — the compensation path of specs/shared/saga.md §4.2 (release, then cancel).</summary>
    private static OrderSagaFixture BuildCancelled(BuildInput input)
    {
        var retailer = Retailers.ByCode(input.RetailerCode);
        var company = Companies.ByCode(input.CompanyCode);
        var currency = Currencies.All.First(c => c.Code == retailer.CurrencyCode);
        var lines = ResolveLines(input.Lines);
        var (initialAmount, initialDiscount, totalAmount) = SumLines(lines);
        if (totalAmount % 100 != 99)
        {
            throw new InvalidOperationException(
                $"BuildCancelled: order {input.Sequence} total {totalAmount} does not end in .99 — the simulated_cents_rule demo requires it");
        }

        var t0 = input.OrderDate;
        var orderId = DeterministicId.Of($"order:{input.Sequence}");
        var orderReference = new OrderNumber(input.Sequence).Value;
        var credit = Credits.ByRetailerAndCompany(input.RetailerCode, input.CompanyCode);
        var firstStockItemId = StockRowId(input.CompanyCode, lines[0].ProductCode);

        var tStockReserved = t0.AddMinutes(1);
        var tCreditRejected = t0.AddMinutes(2);
        var tStockReleased = t0.AddMinutes(3);
        var tCancelled = tStockReleased.AddSeconds(30);

        Guid EventId(string eventType) => DeterministicId.Of($"order:{input.Sequence}:event:{eventType}");
        Guid OutboxId(string eventType) => DeterministicId.Of($"order:{input.Sequence}:outbox:{eventType}");

        var reservations = lines
            .Select(line => new ReservationFixture(
                DeterministicId.Of($"order:{input.Sequence}:reservation:{line.ProductCode}"),
                input.CompanyCode,
                input.RetailerCode,
                line.ProductCode,
                line.Quantity,
                "released",
                tStockReserved,
                tStockReleased))
            .ToArray();

        var reservationRefs = reservations
            .Select(r => new ReservationRef(r.Id, r.ProductCode, r.Units))
            .ToArray();

        var orderPlacedPayload = new OrderPlacedPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            retailer.Gln,
            company.Gln,
            currency.Code,
            t0,
            OrderPlacedLines(lines),
            initialAmount,
            initialDiscount,
            totalAmount,
            "demo — compensation path (credit_rejected, .99 rule)");

        var stockReservedPayload = new StockReservedPayload(orderReference, input.CompanyCode, reservationRefs, input.RetailerCode);

        var creditRejectedPayload = new CreditRejectedPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            currency.Code,
            totalAmount,
            credit.CreditLimit,
            "simulated_cents_rule",
            credit.Code);

        var stockReleasedPayload = new StockReleasedPayload(orderReference, input.CompanyCode, reservationRefs, "credit_rejected", input.RetailerCode);

        var orderPlacedEventId = EventId("order.placed.v1");
        var stockReservedEventId = EventId("stock.reserved.v1");
        var creditRejectedEventId = EventId("credit.rejected.v1");
        var stockReleasedEventId = EventId("stock.released.v1");
        var orderCancelledEventId = EventId("order.cancelled.v1");

        var orderPlacedCausationId = DeterministicId.Of($"order:{input.Sequence}:command:orders.create");
        var stockReservedCausationId = orderPlacedEventId;
        var creditRejectedCausationId = stockReservedEventId;
        var stockReleasedCausationId = creditRejectedEventId;
        var orderCancelledCausationId = stockReleasedEventId;

        var releasedUnits = reservations.Sum(r => r.Units);

        var orderCancelledPayload = new OrderCancelledPayload(
            orderReference,
            input.RetailerCode,
            input.CompanyCode,
            "credit_rejected",
            tCancelled,
            [
                new CompensationStep(
                    "stock_released",
                    "stock.released.v1",
                    tStockReleased,
                    stockReleasedEventId,
                    $"{releasedUnits} unit(s) released back to stock"),
            ]);

        var ordersOutbox = new[]
        {
            new OutboxFixture(OutboxId("order.placed.v1"), orderPlacedEventId, "order.placed.v1", orderId, orderId, orderPlacedCausationId, orderPlacedPayload, t0, t0),
            new OutboxFixture(OutboxId("order.cancelled.v1"), orderCancelledEventId, "order.cancelled.v1", orderId, orderId, orderCancelledCausationId, orderCancelledPayload, tCancelled, tCancelled),
        };

        var fulfillmentOutbox = new[]
        {
            new OutboxFixture(OutboxId("stock.reserved.v1"), stockReservedEventId, "stock.reserved.v1", firstStockItemId, orderId, stockReservedCausationId, stockReservedPayload, tStockReserved, tStockReserved),
            new OutboxFixture(OutboxId("stock.released.v1"), stockReleasedEventId, "stock.released.v1", firstStockItemId, orderId, stockReleasedCausationId, stockReleasedPayload, tStockReleased, tStockReleased),
        };

        var billingOutbox = new[]
        {
            new OutboxFixture(OutboxId("credit.rejected.v1"), creditRejectedEventId, "credit.rejected.v1", credit.Id, orderId, creditRejectedCausationId, creditRejectedPayload, tCreditRejected, tCreditRejected),
        };

        var timeline = new[]
        {
            new TimelineEntryFixture(orderPlacedEventId, "order.placed.v1", t0, $"Order {orderReference} placed for {input.RetailerCode}", orderPlacedCausationId),
            new TimelineEntryFixture(stockReservedEventId, "stock.reserved.v1", tStockReserved, $"Stock reserved for {reservations.Length} line(s)", stockReservedCausationId),
            new TimelineEntryFixture(
                creditRejectedEventId,
                "credit.rejected.v1",
                tCreditRejected,
                $"Credit hold of {totalAmount} {currency.Code} rejected (simulated_cents_rule)",
                creditRejectedCausationId,
                new Dictionary<string, object> { ["reason"] = "simulated_cents_rule", ["requestedAmount"] = totalAmount }),
            new TimelineEntryFixture(
                stockReleasedEventId,
                "stock.released.v1",
                tStockReleased,
                $"{releasedUnits} unit(s) released back to stock (compensation)",
                stockReleasedCausationId),
            new TimelineEntryFixture(
                orderCancelledEventId,
                "order.cancelled.v1",
                tCancelled,
                $"Order {orderReference} cancelled (credit_rejected)",
                orderCancelledCausationId,
                new Dictionary<string, object> { ["cancellationReason"] = "credit_rejected" }),
        };

        return new OrderSagaFixture(
            input.Sequence,
            orderId,
            orderReference,
            t0,
            input.RetailerCode,
            input.CompanyCode,
            currency.Code,
            "cancelled",
            "credit_rejected",
            lines,
            initialAmount,
            initialDiscount,
            totalAmount,
            tCancelled,
            ordersOutbox,
            reservations,
            null,
            fulfillmentOutbox,
            [],
            null,
            billingOutbox,
            timeline);
    }
}
