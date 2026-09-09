using Microsoft.AspNetCore.Http;

namespace OrderToCash.Gateway.Presentation;

/// <summary>Stamps every request with a correlation id (R58: "every log line produced while handling the request") before anything else runs, so a later failure can reuse the SAME id rather than minting a second one.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string ItemKey = "CorrelationId";

    public Task InvokeAsync(HttpContext context)
    {
        context.Items[ItemKey] = Guid.NewGuid();
        return next(context);
    }
}
