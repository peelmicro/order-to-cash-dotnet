using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Health;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary>
/// <c>GET /health/live</c> and <c>GET /health/ready</c> (openapi.yaml,
/// both <c>security: []</c>) — design.md §8.1: the Gateway already IS a
/// <see cref="Microsoft.AspNetCore.Builder.WebApplication"/>, so these two
/// routes are mapped straight into its existing pipeline rather than
/// through a separate <c>HealthProbeService</c>/port the other five
/// services need. Mapped in <c>GatewayHost.Configure</c> BEFORE
/// <c>BearerAuthenticationMiddleware</c>, matching <c>openapi.yaml</c>'s
/// <c>security: []</c> on both paths — and covered by
/// <c>DocsAndAnonymousRouteHttpTests</c>' literal public-route set (design.md
/// §8.1's own words).
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Json(HealthCheckAggregator.Live()))
            .AllowAnonymous();

        app.MapGet("/health/ready", async (IEnumerable<IHealthCheck> checks, CancellationToken ct) =>
        {
            var (statusCode, body) = await HealthCheckAggregator.ReadyAsync(checks.ToList(), ct).ConfigureAwait(false);
            return Results.Json(body, statusCode: statusCode);
        }).AllowAnonymous();

        return app;
    }
}
