namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// OR5/design.md §7 — <c>otc_dlq_depth</c>, recorded once per
/// <c>OutboxRelay.RunOnceAsync</c> cycle. A port so <c>OutboxRelay.cs</c>
/// (the byte-identical outbox-relay-family file) stays free of any
/// service-specific topic list — Fulfillment/Billing register a no-op
/// (neither owns a <c>.dlq</c> topic: design.md §13, "Fulfillment/Billing
/// fact consumers — neither receives a FactRetryDispatcher copy"), Orders
/// registers the real Kafka-backed implementation.
/// </summary>
public interface IDlqDepthGauge
{
    Task RecordAsync(CancellationToken cancellationToken);
}
