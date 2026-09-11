namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// OR5/design.md §7 — <c>otc_saga_completion_ms</c>, recorded by
/// <c>SagaFactHandler</c> when a transition lands the order on
/// <c>completed</c> or <c>cancelled</c>. A port (rather than a direct
/// reference to <c>Infrastructure.Observability.OtcMetrics</c>) because
/// <c>Application/</c> depends inwards only — CLAUDE.md's Clean
/// Architecture rule ("presentation → application → domain") — and the
/// concrete OTel instrument is an Infrastructure concern.
/// </summary>
public interface ISagaCompletionRecorder
{
    /// <summary>Records ONE completion — never two for the same order (the compensation-completing case, design.md §11 cases 74-78) — tagged <paramref name="outcome"/> (<c>"completed"</c> or <c>"cancelled"</c>).</summary>
    void Record(string outcome, TimeSpan duration);
}
