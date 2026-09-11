using System.Diagnostics;
using NATS.Client.Core;

namespace OrderToCash.Gateway.Infrastructure.Observability;

/// <summary>
/// W3C trace-context carrier helpers (design.md §5.2, §5.5, ledger L21) —
/// <see cref="Activity.Id"/>, when <see cref="Activity.IdFormat"/> is
/// <see cref="ActivityIdFormat.W3C"/> (the .NET default), IS the W3C
/// <c>traceparent</c> string verbatim, so no hand-rolled formatting is
/// needed to emit one. The Gateway is an outbound-only NATS caller (it
/// never extracts a NATS context), so only the injection half is built
/// here — Orders' own <c>TraceContext</c> carries the extraction half its
/// three responder subjects need.
/// </summary>
public static class TraceContext
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";

    /// <summary>The active span's own <c>traceparent</c>, or <see langword="null"/> when none is active — never fabricated (OR4's "no span active" clause).</summary>
    public static string? ActiveTraceParent() => Activity.Current?.Id;

    /// <summary>The active span's own <c>tracestate</c>, or <see langword="null"/> when none is set.</summary>
    public static string? ActiveTraceState() =>
        string.IsNullOrEmpty(Activity.Current?.TraceStateString) ? null : Activity.Current.TraceStateString;

    /// <summary>
    /// Injects the active span's trace context into a FRESH
    /// <see cref="NatsHeaders"/> instance — never onto a shared/reused
    /// instance (<see cref="NatsHeaders"/> is documented not thread-safe,
    /// ledger L21). A no-op when no span is active.
    /// </summary>
    public static void InjectNats(NatsHeaders headers)
    {
        var traceParent = ActiveTraceParent();
        if (traceParent is null)
        {
            return;
        }

        headers[TraceParentHeader] = traceParent;
        var traceState = ActiveTraceState();
        if (traceState is not null)
        {
            headers[TraceStateHeader] = traceState;
        }
    }
}
