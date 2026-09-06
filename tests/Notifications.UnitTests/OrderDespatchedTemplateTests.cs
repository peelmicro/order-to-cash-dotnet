using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS3 — <c>order.despatched.v1</c>.</summary>
public sealed class OrderDespatchedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222224");

    [Fact]
    public void Build_ProducesASubjectCarryingTheDespatchReferenceAndTheCorrelationId()
    {
        var envelope = new Envelope<OrderDespatchedPayload>(
            Guid.NewGuid(),
            "order.despatched.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            new OrderDespatchedPayload("ORD-000001", "DES-000001", DateTimeOffset.Parse("2026-09-01T11:00:00Z"), "COMP01", "CarrefourEs", [new DespatchLine("SKU-1", 10)]));

        var message = OrderDespatchedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Order ORD-000001 despatched (DES-000001) (correlationId: {_correlationId})", message.Subject);
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Order ORD-000001 has been despatched", message.Text, StringComparison.Ordinal);
        Assert.Contains("Despatch reference: DES-000001", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Lines: SKU-1 x10", message.Text, StringComparison.Ordinal);
        Assert.Contains("Order <strong>ORD-000001</strong> has been despatched", message.Html, StringComparison.Ordinal);
        Assert.Contains("Despatch reference: DES-000001", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
        Assert.Contains("Lines: SKU-1 x10", message.Html, StringComparison.Ordinal);
    }
}
