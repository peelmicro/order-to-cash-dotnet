using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Domain.Orders;

namespace OrderToCash.Gateway.Application.Commands;

public sealed record PlaceOrderCommandLine(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

public sealed record PlaceOrderCommand(
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<PlaceOrderCommandLine> Lines,
    long? OrderDiscount,
    string? Notes,
    Guid? IdempotencyKey) : ICommand<PlaceOrderResult>;

public sealed record PlaceOrderResult(
    Guid OrderId,
    string OrderReference,
    string Status,
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    DateTimeOffset OrderDate,
    bool ProjectionPending);

/// <summary>
/// <c>POST /orders</c> → NATS RPC <c>orders.create</c> (R13). Ported from
/// #7's <c>apps/gateway/src/application/commands/place-order.command.ts</c>.
/// <c>Idempotency-Key</c> (if supplied) travels as the RPC payload's own
/// <c>requestId</c> — the field <c>OrdersCreateResponder</c> reads as
/// <c>OrdersCreateRequestPayload.RequestId</c> — never the same value as
/// the RPC transport's own <c>x-request-id</c> header, which identifies
/// this specific attempt for tracing and is fresh on every call.
/// </summary>
public sealed class PlaceOrderCommandHandler(IRpcClient rpc, IssuedOrderWindow issuedOrders) : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var payload = new OrdersCreateRequestPayload(
            command.IdempotencyKey,
            command.RetailerCode,
            command.CompanyCode,
            command.Currency,
            command.Lines.Select(l => new OrdersCreateRequestLine(l.ProductCode, l.Quantity, l.UnitPrice, l.LineDiscount)).ToList(),
            command.OrderDiscount,
            command.Notes);

        // Neither the order nor its id is known yet — a fresh gateway
        // request id stands in for x-correlation-id.
        var requestId = Guid.NewGuid();
        var reply = await rpc.CallAsync<OrdersCreateRequestPayload, OrdersCreateReplyPayload>(
            GatewaySubjects.OrdersCreate, payload, new RpcCallMeta(requestId, requestId), cancellationToken).ConfigureAwait(false);

        // F3 (#7 review finding, ported) — R55's "an id the caller has just
        // been given" is, by construction, an id THIS gateway just handed
        // out: record it so GetOrderQueryHandler can answer 202 (never a
        // false 404) for it right up until the projection catches up or
        // the window's TTL expires.
        issuedOrders.Record(reply.OrderId);

        return new PlaceOrderResult(
            reply.OrderId,
            reply.OrderReference,
            "placed",
            reply.Currency,
            reply.InitialAmount,
            reply.InitialDiscount,
            reply.TotalAmount,
            reply.OrderDate,
            ProjectionPending: true);
    }
}
