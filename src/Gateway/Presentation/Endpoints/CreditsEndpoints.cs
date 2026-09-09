using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>GET /credits</c> (openapi.yaml <c>billing</c> tag).</summary>
public static class CreditsEndpoints
{
    public static IEndpointRouteBuilder MapCreditsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/credits", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var (page, pageSize) = RequestParsing.ParsePageParams(context.Request);
            var retailerCode = context.Request.Query["retailerCode"].FirstOrDefault();
            var companyCode = context.Request.Query["companyCode"].FirstOrDefault();

            var result = await dispatcher.QueryAsync<ListCreditsQuery, CreditListReplyPayload>(
                new ListCreditsQuery(page, pageSize, retailerCode, companyCode), cancellationToken).ConfigureAwait(false);

            return Results.Json(result);
        });

        return app;
    }
}
