using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>NS7 — <c>order.cancelled.v1</c>.</summary>
public sealed class OrderCancelledTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222228");

    /// <summary>R2-D6 / id 59 — bracketed away from envelope.OccurredAt (2026-09-01T15:00:00Z below).</summary>
    private static readonly DateTimeOffset _cancelledAt = DateTimeOffset.Parse("2026-08-31T21:50:35Z");

    [Fact]
    public void Build_ProducesASubjectCarryingTheOrderReferenceAndTheReason()
    {
        var envelope = new Envelope<OrderCancelledPayload>(
            Guid.NewGuid(),
            "order.cancelled.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T15:00:00Z"),
            new OrderCancelledPayload(
                "ORD-000001",
                "CarrefourEs",
                "COMP01",
                "stock_rejected",
                _cancelledAt,
                // D1 (review round 2 of ids 58/59) — `Step` must NOT be a prefix of
                // `EventType`: the old fixture ("credit.release", "credit.released.v1")
                // let `step.Step` be substituted with `step.EventType` and stay green,
                // because "credit.released.v1" CONTAINS "credit.release" as a prefix
                // and the assertion below is `Assert.Contains`. "warehouse.release" is
                // not a prefix of "credit.released.v1" in either direction.
                [new CompensationStep("warehouse.release", "credit.released.v1", DateTimeOffset.Parse("2026-09-01T15:00:00Z"))]));

        var message = OrderCancelledTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Order ORD-000001 cancelled (correlationId: {_correlationId})", message.Subject);
        // D2 (feature 23 review round 1) — this template had NO recipient assertion; a
        // corrupted RecipientFor(payload.CompanyCode) instead of RetailerCode reported green.
        Assert.Equal("carrefoures@retailer.order-to-cash.example", message.To);
        Assert.Contains("Order ORD-000001 has been cancelled", message.Text, StringComparison.Ordinal);
        Assert.Contains("Reason: stock_rejected", message.Text, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Text, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Text, StringComparison.Ordinal);
        Assert.Contains("Compensation steps: warehouse.release", message.Text, StringComparison.Ordinal);
        Assert.Contains($"Cancelled at: {_cancelledAt:O}", message.Text, StringComparison.Ordinal);
        Assert.Contains("Order <strong>ORD-000001</strong> has been cancelled", message.Html, StringComparison.Ordinal);
        Assert.Contains("Reason: stock_rejected", message.Html, StringComparison.Ordinal);
        Assert.Contains("Retailer: CarrefourEs", message.Html, StringComparison.Ordinal);
        Assert.Contains("Company: COMP01", message.Html, StringComparison.Ordinal);
        Assert.Contains("Compensation steps: warehouse.release", message.Html, StringComparison.Ordinal);
        Assert.Contains($"Cancelled at: {_cancelledAt:O}", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RendersNoCompensationStepsAsNone()
    {
        // D5 (review round 2 of ids 58/59) — bracketed away from envelope.OccurredAt
        // (2026-09-01T15:00:00Z below), same shape as R2-D6/id 59's fix above, applied
        // here too for consistency even though this test does not assert the
        // "Cancelled at:" line — harmless today, but the collision is exactly what this
        // feature exists to remove.
        var envelope = new Envelope<OrderCancelledPayload>(
            Guid.NewGuid(),
            "order.cancelled.v1",
            Guid.NewGuid(),
            _correlationId,
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-09-01T15:00:00Z"),
            new OrderCancelledPayload("ORD-000001", "CarrefourEs", "COMP01", "operator_cancelled", _cancelledAt, []));

        var message = OrderCancelledTemplate.Build(envelope);

        Assert.Contains("Compensation steps: none", message.Text, StringComparison.Ordinal);
    }
}
