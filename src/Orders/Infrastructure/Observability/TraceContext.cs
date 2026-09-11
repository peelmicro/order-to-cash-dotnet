using System.Diagnostics;
using NATS.Client.Core;

namespace OrderToCash.Orders.Infrastructure.Observability;

/// <summary>
/// W3C trace-context carrier helpers (design.md §5.2, §5.3, §5.5, ledger
/// L21/L24) — <see cref="Activity.Id"/>, when <see cref="Activity.IdFormat"/>
/// is <see cref="ActivityIdFormat.W3C"/> (the .NET default), IS the W3C
/// <c>traceparent</c> string verbatim, so no hand-rolled formatting is
/// needed either to emit or to parse one
/// (<see cref="ActivityContext.TryParse(string, string?, out ActivityContext)"/>).
/// Every "continued" assertion in this feature extracts the header back and
/// compares the TRACE id to the real originating one — never merely checks
/// a header is present (design.md §5.5).
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

    /// <summary>Parses a stored/received W3C <c>traceparent</c> (plus an optional <c>tracestate</c>) back into an <see cref="ActivityContext"/> — <see langword="null"/> when the string is <see langword="null"/>, empty or malformed.</summary>
    public static ActivityContext? ContextFromTraceParent(string? traceParent, string? traceState = null) =>
        !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var context)
            ? context
            : null;

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

    /// <summary>Extracts a received NATS message's trace context, or <see langword="null"/> when no <c>traceparent</c> header is present or it does not parse.</summary>
    public static ActivityContext? ExtractNats(NatsHeaders? headers)
    {
        if (headers is null || !headers.TryGetLastValue(TraceParentHeader, out var traceParent))
        {
            return null;
        }

        headers.TryGetLastValue(TraceStateHeader, out var traceState);
        return ContextFromTraceParent(traceParent, traceState);
    }

    /// <summary>Extracts a consumed Kafka message's trace context from its already-decoded string headers, or <see langword="null"/> when absent/unparseable.</summary>
    public static ActivityContext? ExtractKafka(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(TraceParentHeader, out var traceParent))
        {
            return null;
        }

        headers.TryGetValue(TraceStateHeader, out var traceState);
        return ContextFromTraceParent(traceParent, traceState);
    }
}
