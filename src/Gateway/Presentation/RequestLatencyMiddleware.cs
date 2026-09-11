using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using OrderToCash.Gateway.Infrastructure.Observability;

namespace OrderToCash.Gateway.Presentation;

/// <summary>
/// OR5/design.md §7 — <c>otc_request_latency_ms</c>, tagged by endpoint.
/// Registered AFTER <c>UseRouting()</c> (design.md §6's ordering) so
/// <see cref="HttpContext.GetEndpoint"/> is populated, and BEFORE rate
/// limiting/auth, so it wraps everything from there to the response —
/// including the error path: <see cref="Endpoint"/> already precedes it in
/// the pipeline, so a rethrow this middleware lets pass through still
/// records, in a <c>finally</c>, before <c>ProblemJsonMiddleware</c>
/// (which sits OUTSIDE it) translates the exception to a response.
/// </summary>
public sealed class RequestLatencyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            var endpointTag = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            OtcMetrics.RequestLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("endpoint", endpointTag));
        }
    }
}
