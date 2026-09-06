using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS6 — <c>order.completed.v1</c>.</summary>
public sealed class OrderCompletedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222227");

    [Fact]
    public void Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId()
    {
        var envelope = new Envelope<OrderCompletedPayload>(
            Guid.NewGuid(),
            "order.completed.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T14:00:00Z"),
            new OrderCompletedPayload("ORD-000001", "CarrefourEs", "COMP01", "USD", 124250, DateTimeOffset.Parse("2026-09-01T14:00:00Z")));

        var message = OrderCompletedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Order ORD-000001 completed (correlationId: {_correlationId})", message.Subject);
        // D2 (feature 23 review round 1) — this template had NO recipient assertion; a
        // corrupted RecipientFor(payload.CompanyCode) instead of RetailerCode reported green.
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Order ORD-000001 is complete", message.Text, StringComparison.Ordinal);
        Assert.Contains("despatched, invoiced and paid", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Order <strong>ORD-000001</strong> is complete", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
        Assert.Contains("1242.50 USD", message.Html, StringComparison.Ordinal);
    }
}
