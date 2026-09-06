using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS1 — <c>order.placed.v1</c>.</summary>
public sealed class OrderPlacedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId()
    {
        var envelope = BuildEnvelope();

        var message = OrderPlacedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Order ORD-000001 placed (correlationId: {_correlationId})", message.Subject);
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Order ORD-000001 has been placed", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Total: 1242.50 USD", message.Text, StringComparison.Ordinal);
        Assert.Contains("1242.50 USD", message.Html, StringComparison.Ordinal);
        Assert.Contains("Order <strong>ORD-000001</strong> has been placed", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
    }

    private static Envelope<OrderPlacedPayload> BuildEnvelope() => new(
        Guid.NewGuid(),
        "order.placed.v1",
        Guid.NewGuid(),
        _correlationId,
        Guid.NewGuid(),
        DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
        new OrderPlacedPayload(
            "ORD-000001",
            "CarrefourEs",
            "COMP01",
            "1234567890123",
            "9876543210987",
            "USD",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            [new OrderLine("SKU-1", "Widget", 10, 12425, 0)],
            124250,
            0,
            124250));
}
