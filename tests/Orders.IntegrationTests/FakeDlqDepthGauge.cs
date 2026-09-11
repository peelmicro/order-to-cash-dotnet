using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// A no-op <see cref="IDlqDepthGauge"/> for every <see cref="OutboxRelay"/>
/// test that is not itself about <c>otc_dlq_depth</c> — the real
/// <c>KafkaDlqDepthGauge</c> makes one bounded-timeout admin/watermark
/// round trip per <c>.dlq</c> topic on every <c>RunOnceAsync</c> call, which
/// would slow down (and could destabilise) every OTHER outbox test that has
/// nothing to do with this metric. <c>MetricsExposureTests</c> and
/// <c>KafkaDlqDepthTests</c> use the real implementation directly.
/// </summary>
internal sealed class FakeDlqDepthGauge : IDlqDepthGauge
{
    public int CallCount { get; private set; }

    public Task RecordAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.CompletedTask;
    }
}
