using System.Diagnostics;

namespace OrderToCash.Notifications.Infrastructure.Observability;

/// <summary>
/// W3C trace-context carrier helpers (design.md §5.3, §5.5, ledger L22,
/// L24) — <see cref="ActivityContext.TryParse(string, string?, out ActivityContext)"/>
/// restores a received <c>traceparent</c>/<c>tracestate</c> pair without
/// any hand-rolled parsing. Every "continued" assertion in this feature
/// extracts the header back and compares the TRACE id to the real
/// originating one — never merely checks a header is present (design.md
/// §5.5). Notifications only ever CONSUMES a fact (it owns no outbox and no
/// outbound NATS call), so only the Kafka extraction half is built here.
/// </summary>
public static class TraceContext
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";

    /// <summary>Parses a received W3C <c>traceparent</c> (plus an optional <c>tracestate</c>) back into an <see cref="ActivityContext"/> — <see langword="null"/> when the string is <see langword="null"/>, empty or malformed.</summary>
    public static ActivityContext? ContextFromTraceParent(string? traceParent, string? traceState = null) =>
        !string.IsNullOrEmpty(traceParent) && ActivityContext.TryParse(traceParent, traceState, out var context)
            ? context
            : null;

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
