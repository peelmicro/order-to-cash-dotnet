using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.Observability;

namespace OrderToCash.Billing.Infrastructure.Outbox;

/// <summary>Billing owns no <c>.dlq</c> topic (design.md §13), so <c>otc_dlq_depth</c> is always <c>0</c> here — recorded, not omitted, so the instrument still exists on this service's own <c>service.name</c>.</summary>
public sealed class NoOpDlqDepthGauge : IDlqDepthGauge
{
    public Task RecordAsync(CancellationToken cancellationToken)
    {
        OtcMetrics.DlqDepth.Record(0);
        return Task.CompletedTask;
    }
}
