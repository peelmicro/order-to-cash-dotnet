using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS4 — <c>invoice.issued.v1</c>.</summary>
public sealed class InvoiceIssuedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222225");

    [Fact]
    public void Build_ProducesASubjectCarryingTheInvoiceReferenceAndTheCorrelationId()
    {
        var envelope = new Envelope<InvoiceIssuedPayload>(
            Guid.NewGuid(),
            "invoice.issued.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T12:00:00Z"),
            new InvoiceIssuedPayload("ORD-000001", "INV-000001", DateTimeOffset.Parse("2026-09-01T12:00:00Z"), "CarrefourEs", "COMP01", "USD", [new InvoiceLine("SKU-1", 10, 12425)], 124250, 0, 124250));

        var message = InvoiceIssuedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Invoice INV-000001 issued (correlationId: {_correlationId})", message.Subject);
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Invoice INV-000001 has been issued for order ORD-000001", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Invoice <strong>INV-000001</strong> has been issued for order ORD-000001", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
        Assert.Contains("1242.50 USD", message.Html, StringComparison.Ordinal);
    }
}
