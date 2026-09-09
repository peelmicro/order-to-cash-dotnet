using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Presentation.Dto;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>GET /catalog/products</c>, <c>GET /catalog/retailers</c>, <c>GET /catalog/companies</c> (openapi.yaml <c>catalog</c> tag).</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/catalog/products", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var reply = await QueryAsync(context, dispatcher, CatalogReferenceKinds.Products, cancellationToken).ConfigureAwait(false);
            return Results.Json(new ItemsResponseDto<ProductPayload>(reply.Products ?? []));
        });

        app.MapGet("/catalog/retailers", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var reply = await QueryAsync(context, dispatcher, CatalogReferenceKinds.Retailers, cancellationToken).ConfigureAwait(false);
            return Results.Json(new ItemsResponseDto<PartyPayload>(reply.Retailers ?? []));
        });

        app.MapGet("/catalog/companies", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var reply = await QueryAsync(context, dispatcher, CatalogReferenceKinds.Companies, cancellationToken).ConfigureAwait(false);
            return Results.Json(new ItemsResponseDto<PartyPayload>(reply.Companies ?? []));
        });

        return app;
    }

    private static Task<CatalogReferenceListReplyPayload> QueryAsync(HttpContext context, IDispatcher dispatcher, string kind, CancellationToken cancellationToken)
    {
        var includeDisabled = string.Equals(context.Request.Query["includeDisabled"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        return dispatcher.QueryAsync<ListCatalogQuery, CatalogReferenceListReplyPayload>(new ListCatalogQuery(kind, includeDisabled), cancellationToken);
    }
}
