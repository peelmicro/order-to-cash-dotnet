namespace OrderToCash.Billing.Application.Ports;

/// <summary>
/// OR5/design.md §7 — <c>otc_dlq_depth</c>, recorded once per
/// <c>OutboxRelay.RunOnceAsync</c> cycle. A port so <c>OutboxRelay.cs</c>
/// (the byte-identical outbox-relay-family file) stays free of any
/// service-specific topic list. Billing owns no <c>.dlq</c> topic
/// (design.md §13, "Fulfillment/Billing fact consumers — neither receives a
/// FactRetryDispatcher copy"), so this service registers the no-op.
/// </summary>
public interface IDlqDepthGauge
{
    Task RecordAsync(CancellationToken cancellationToken);
}
