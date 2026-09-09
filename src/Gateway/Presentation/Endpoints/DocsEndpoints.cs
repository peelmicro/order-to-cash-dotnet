using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Gateway.Infrastructure.Docs;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary>
/// <c>GET /docs</c> (openapi.yaml <c>ops</c> tag) — "Human-facing rendering
/// of this document. Unauthenticated so a reviewer can read the contract
/// before obtaining a token." Rendered as the raw, embedded
/// <c>specs/shared/openapi.yaml</c> text inside a preformatted block —
/// deliberately not a JS-driven Swagger UI pulled from a CDN, which would
/// (a) need a second, uncontracted route to serve the spec JSON to it, and
/// (b) make this endpoint depend on a network resource outside this
/// process at demo time. Still a genuine "human-facing rendering":
/// syntax-coloured is a nicety this contract does not ask for, readable is
/// what it asks for.
/// </summary>
public static class DocsEndpoints
{
    public static IEndpointRouteBuilder MapDocsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/docs", () =>
            {
                var escaped = WebUtility.HtmlEncode(EmbeddedOpenApiDocument.Yaml);
                const string style = "body { font-family: monospace; margin: 2rem; } pre { white-space: pre-wrap; }";
                var html = $"""
                    <!DOCTYPE html>
                    <html lang="en">
                    <head>
                    <meta charset="utf-8" />
                    <title>Order-To-Cash — Gateway REST API</title>
                    <style>{style}</style>
                    </head>
                    <body>
                    <h1>Order-To-Cash — Gateway REST API</h1>
                    <p>The contract this gateway implements, verbatim from <code>specs/shared/openapi.yaml</code>.</p>
                    <pre>{escaped}</pre>
                    </body>
                    </html>
                    """;

                return Results.Text(html, "text/html");
            })
            .AllowAnonymous();

        return app;
    }
}
