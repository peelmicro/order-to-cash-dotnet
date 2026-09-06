using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `F9`'s own coverage gap closed: <see cref="PaymentReceived"/> has NO live
/// caller in this feature (feature 22's seam), so
/// <see cref="BillingFactPayloadMapper"/>'s arm for it is otherwise
/// unreachable from any integration test. Asserts the mapped payload's
/// fields against the domain event's OWN fields — corruption, not merely
/// presence (feature 17's defect).
/// </summary>
public sealed class BillingFactPayloadMapperTests
{
    [Fact]
    public void ToPayload_MapsPaymentReceived_FieldByFieldFromTheDomainEvent()
    {
        var mapper = new BillingFactPayloadMapper();
        var valueDate = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);
        var domainEvent = new PaymentReceived(
            EventId: UniqueId.New(),
            AggregateId: UniqueId.New(),
            CorrelationId: UniqueId.New(),
            CausationId: UniqueId.New(),
            OccurredAt: DateTimeOffset.UtcNow,
            OrderReference: OrderNumber.Parse("ORD-000001"),
            InvoiceReference: "INV-000001",
            PaymentReference: "PMT-000001",
            Amount: new Money(12_500, "EUR"),
            ValueDate: valueDate,
            Source: "operator");

        var payload = Assert.IsType<PaymentReceivedPayload>(mapper.ToPayload(domainEvent));

        Assert.Equal("ORD-000001", payload.OrderReference);
        Assert.Equal("INV-000001", payload.InvoiceReference);
        Assert.Equal("PMT-000001", payload.PaymentReference);
        Assert.Equal("EUR", payload.Currency);
        Assert.Equal(12_500, payload.Amount);
        Assert.Equal(valueDate, payload.ValueDate);
        Assert.Equal("operator", payload.Source);
    }

    [Fact]
    public void ToPayload_MapsInvoiceIssued_FieldByFieldFromTheDomainEvent()
    {
        var mapper = new BillingFactPayloadMapper();
        var invoiceDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var line = InvoiceLine.Create(UniqueId.New(), "SKU-1", new Quantity(2), new Money(1_000, "EUR"));
        var domainEvent = new InvoiceIssued(
            EventId: UniqueId.New(),
            AggregateId: UniqueId.New(),
            CorrelationId: UniqueId.New(),
            CausationId: UniqueId.New(),
            OccurredAt: invoiceDate,
            OrderReference: OrderNumber.Parse("ORD-000002"),
            InvoiceReference: "INV-000002",
            InvoiceDate: invoiceDate,
            RetailerCode: "CarrefourEs",
            CompanyCode: "IBERFOODS",
            Currency: "EUR",
            Lines: [line],
            Amount: new Money(2_000, "EUR"),
            Discount: Money.Zero("EUR"),
            TotalAmount: new Money(2_000, "EUR"));

        var payload = Assert.IsType<InvoiceIssuedPayload>(mapper.ToPayload(domainEvent));

        Assert.Equal("ORD-000002", payload.OrderReference);
        Assert.Equal("INV-000002", payload.InvoiceReference);
        Assert.Equal(invoiceDate, payload.InvoiceDate);
        Assert.Equal("CarrefourEs", payload.RetailerCode);
        Assert.Equal("IBERFOODS", payload.CompanyCode);
        Assert.Equal("EUR", payload.Currency);
        var payloadLine = Assert.Single(payload.Lines);
        Assert.Equal("SKU-1", payloadLine.ProductCode);
        Assert.Equal(2, payloadLine.Units);
        Assert.Equal(1_000, payloadLine.UnitPrice);
        Assert.Equal(2_000, payload.Amount);
        Assert.Equal(0, payload.Discount);
        Assert.Equal(2_000, payload.TotalAmount);
    }
}
