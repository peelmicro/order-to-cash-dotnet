using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Observability;

/// <summary>The one implementation of <see cref="ISagaCompletionRecorder"/> — <c>otc_saga_completion_ms</c>, ONE instrument, tagged <c>outcome</c> (design.md §7 — "distinguishes completed from cancelled by attribute, not by a second instrument").</summary>
public sealed class SagaCompletionRecorder : ISagaCompletionRecorder
{
    public void Record(string outcome, TimeSpan duration) =>
        OtcMetrics.SagaCompletionMs.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));
}
