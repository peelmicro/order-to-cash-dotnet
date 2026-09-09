using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Presentation.Dto;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>GET /stock</c>, <c>POST /stock/replenish</c> (openapi.yaml <c>fulfillment</c> tag).</summary>
public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/stock", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var (page, pageSize) = RequestParsing.ParsePageParams(context.Request);
            var companyCode = context.Request.Query["companyCode"].FirstOrDefault();
            var productCode = context.Request.Query["productCode"].FirstOrDefault();
            var belowThreshold = string.Equals(context.Request.Query["belowThreshold"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);

            var result = await dispatcher.QueryAsync<ListStockQuery, StockListReplyPayload>(
                new ListStockQuery(page, pageSize, companyCode, productCode, belowThreshold), cancellationToken).ConfigureAwait(false);

            return Results.Json(result);
        });

        app.MapPost("/stock/replenish", async (ReplenishStockRequestDto body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (body.Lines is not { Count: > 0 })
            {
                throw new GatewayRequestValidationError("lines", "at least one line is required.");
            }

            var command = new ReplenishStockCommand(
                body.CompanyCode,
                body.Lines.Select(l => new StockReplenishRequestLine(l.ProductCode, l.Units)).ToList());

            var result = await dispatcher.SendAsync<ReplenishStockCommand, StockReplenishReplyPayload>(command, cancellationToken).ConfigureAwait(false);

            return Results.Json(result);
        });

        return app;
    }
}
