using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace OrderToCash.Gateway.Presentation;

/// <summary>
/// design.md §6's scope-push table, Gateway row — pushes the request-scoped
/// <c>correlationId</c> <see cref="CorrelationIdMiddleware"/> already
/// stamped into <see cref="HttpContext.Items"/> onto the logging scope, so
/// EVERY log line produced while handling this request — including
/// <c>ProblemJsonMiddleware</c>'s own failure line — carries it (R58/OR7).
/// Registered IMMEDIATELY after <see cref="CorrelationIdMiddleware"/> and
/// BEFORE <see cref="Problem.ProblemJsonMiddleware"/> (<c>GatewayHost.Configure</c>) —
/// the ordering <c>tasks.md</c> A3g arms.
/// </summary>
public sealed class CorrelationLoggingScopeMiddleware(RequestDelegate next, ILogger<CorrelationLoggingScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is Guid guid
            ? guid
            : Guid.NewGuid();

        using (logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId }))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
