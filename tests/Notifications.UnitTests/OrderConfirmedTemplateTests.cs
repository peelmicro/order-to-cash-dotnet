using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS2 — <c>order.confirmed.v1</c>.</summary>
public sealed class OrderConfirmedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222223");

    [Fact]
    public void Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId()
    {
        var envelope = new Envelope<OrderConfirmedPayload>(
            Guid.NewGuid(),
            "order.confirmed.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T10:05:00Z"),
            new OrderConfirmedPayload("ORD-000001", "CarrefourEs", "COMP01", "USD", 124250, DateTimeOffset.Parse("2026-09-01T10:05:00Z")));

        var message = OrderConfirmedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Order ORD-000001 confirmed (correlationId: {_correlationId})", message.Subject);
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Order ORD-000001 has been confirmed", message.Text, StringComparison.Ordinal);
        Assert.Contains("confirmed — stock reserved and credit approved", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Order <strong>ORD-000001</strong> has been confirmed", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
        Assert.Contains("1242.50 USD", message.Html, StringComparison.Ordinal);
    }
}
