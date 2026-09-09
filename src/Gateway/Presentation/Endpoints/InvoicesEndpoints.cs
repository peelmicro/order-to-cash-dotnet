using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Presentation.Dto;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>GET /invoices</c>, <c>POST /invoices/{id}/payments</c> (openapi.yaml <c>billing</c> tag).</summary>
public static class InvoicesEndpoints
{
    public static IEndpointRouteBuilder MapInvoicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var (page, pageSize) = RequestParsing.ParsePageParams(context.Request);
            var status = context.Request.Query["status"].FirstOrDefault();
            var retailerCode = context.Request.Query["retailerCode"].FirstOrDefault();
            var companyCode = context.Request.Query["companyCode"].FirstOrDefault();
            var orderReference = context.Request.Query["orderReference"].FirstOrDefault();
            int? issuedBeforeMinutes = int.TryParse(context.Request.Query["issuedBeforeMinutes"].FirstOrDefault(), out var minutes) ? minutes : null;

            var query = new ListInvoicesQuery(page, pageSize, status, retailerCode, companyCode, orderReference, issuedBeforeMinutes);
            var result = await dispatcher.QueryAsync<ListInvoicesQuery, InvoiceListReplyPayload>(query, cancellationToken).ConfigureAwait(false);

            return Results.Json(result);
        });

        app.MapPost("/invoices/{id}/payments", async (string id, RegisterPaymentRequestDto body, IDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            var invoiceId = RequestParsing.ParseId("id", id);

            var command = new RegisterPaymentCommand(invoiceId, body.PaymentReference, body.Amount.Amount, body.Amount.Currency, body.ValueDate, body.Source);
            var result = await dispatcher.SendAsync<RegisterPaymentCommand, RegisterPaymentResult>(command, cancellationToken).ConfigureAwait(false);

            context.Response.Headers["X-Correlation-Id"] = result.CorrelationId.ToString();

            if (result.Reply.Outcome == "duplicate")
            {
                context.Response.Headers["Idempotent-Replay"] = "true";
                return Results.Json(result.Reply, statusCode: StatusCodes.Status200OK);
            }

            return Results.Json(result.Reply, statusCode: StatusCodes.Status201Created);
        });

        return app;
    }
}
