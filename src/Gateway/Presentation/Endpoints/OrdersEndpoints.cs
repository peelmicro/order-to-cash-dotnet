using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Presentation.Dto;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>POST /orders</c>, <c>GET /orders</c>, <c>GET /orders/{id}</c>, <c>POST /orders/{id}/cancel</c> (openapi.yaml <c>orders</c> tag). <c>GET /orders/stream</c> is feature <c>gateway_sse_push</c>'s own <c>StreamEndpoints.cs</c> — a literal route mapped separately, AFTER this file's own <c>GET /orders/{id}</c>, in <c>GatewayHost.Configure</c>.</summary>
public static class OrdersEndpoints
{
    private const int ProjectionPendingRetryAfterSeconds = 2;

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", PlaceOrderAsync);
        app.MapGet("/orders", ListOrdersAsync);
        app.MapGet("/orders/{id}", GetOrderAsync);
        app.MapPost("/orders/{id}/cancel", CancelOrderAsync);

        return app;
    }

    private static async Task<IResult> PlaceOrderAsync(
        HttpContext context,
        PlaceOrderRequestDto body,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        if (body.Lines is not { Count: > 0 })
        {
            throw new GatewayRequestValidationError("lines", "at least one line is required.");
        }

        Guid? idempotencyKey = null;
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var raw) && !string.IsNullOrEmpty(raw))
        {
            if (!Guid.TryParse(raw!, out var parsed))
            {
                throw new GatewayRequestValidationError("Idempotency-Key", $"\"{raw}\" is not a valid Idempotency-Key.");
            }

            idempotencyKey = parsed;
        }

        var command = new PlaceOrderCommand(
            body.RetailerCode,
            body.CompanyCode,
            body.Currency,
            body.Lines.Select(l => new PlaceOrderCommandLine(l.ProductCode, l.Quantity, l.UnitPrice, l.LineDiscount)).ToList(),
            body.OrderDiscount,
            body.Notes,
            idempotencyKey);

        var result = await dispatcher.SendAsync<PlaceOrderCommand, PlaceOrderResult>(command, cancellationToken).ConfigureAwait(false);

        context.Response.Headers.Location = $"/orders/{result.OrderId}";
        context.Response.Headers["X-Correlation-Id"] = result.OrderId.ToString();

        return Results.Json(result, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListOrdersAsync(HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var (page, pageSize) = RequestParsing.ParsePageParams(context.Request);
        var status = RequestParsing.ToStringArray(context.Request, "status");
        var retailerCode = context.Request.Query["retailerCode"].FirstOrDefault();
        var companyCode = context.Request.Query["companyCode"].FirstOrDefault();
        var orderReference = context.Request.Query["orderReference"].FirstOrDefault();

        var filter = new OrderListFilter(status, retailerCode, companyCode, orderReference, page, pageSize);
        var result = await dispatcher.QueryAsync<ListOrdersQuery, OrderSummaryPageResult>(new ListOrdersQuery(filter), cancellationToken).ConfigureAwait(false);

        return Results.Json(result);
    }

    private static async Task<IResult> GetOrderAsync(HttpContext context, string id, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var orderId = RequestParsing.ParseId("id", id);
        var result = await dispatcher.QueryAsync<GetOrderQuery, GetOrderResult>(new GetOrderQuery(orderId), cancellationToken).ConfigureAwait(false);

        switch (result.Kind)
        {
            case GetOrderResultKind.Found:
                return Results.Json(result.Detail);

            case GetOrderResultKind.Pending:
                // R55 — "an order identifier the caller has just been
                // given": GetOrderQueryHandler only reaches this branch
                // when IssuedOrderWindow confirms THIS gateway itself
                // handed out orderId recently. An explicit "projection
                // pending" indication, never a bare 404.
                context.Response.Headers.RetryAfter = ProjectionPendingRetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(
                    new ProjectionPendingResponseDto(
                        orderId,
                        "projection_pending",
                        "The order was accepted and is not projected yet. Subscribe to /orders/stream or retry.",
                        ProjectionPendingRetryAfterSeconds * 1000),
                    statusCode: StatusCodes.Status202Accepted);

            default:
                // `unknown` — openapi.yaml's own words: "A genuine 404
                // means the identifier is unknown to the system." This id
                // was never handed out by this gateway (or the recency
                // window on it has long since expired).
                throw new GatewayNotFoundError($"no order for id \"{orderId}\"");
        }
    }

    private static async Task<IResult> CancelOrderAsync(string id, CancelOrderRequestDto? body, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var orderId = RequestParsing.ParseId("id", id);
        var result = await dispatcher.SendAsync<CancelOrderCommand, CancelOrderResult>(
            new CancelOrderCommand(orderId, body?.Note), cancellationToken).ConfigureAwait(false);

        return Results.Json(result, statusCode: StatusCodes.Status202Accepted);
    }
}
