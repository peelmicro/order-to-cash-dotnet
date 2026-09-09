using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;

namespace OrderToCash.Gateway.Application.Commands;

public sealed record CancelOrderCommand(Guid OrderId, string? Note) : ICommand<CancelOrderResult>;

public sealed record CancelOrderResult(Guid OrderId, string OrderReference, string Status, string? CancellationReason, IReadOnlyList<string> CompensationPlanned);

/// <summary>
/// <c>POST /orders/{id}/cancel</c> → NATS RPC <c>orders.cancel</c> (R8,
/// R27/R28). The order id IS known here, so it — not a fresh gateway
/// request id — is the RPC <c>x-correlation-id</c>, matching every fact
/// this order's saga has produced or will produce.
/// </summary>
public sealed class CancelOrderCommandHandler(IRpcClient rpc) : ICommandHandler<CancelOrderCommand, CancelOrderResult>
{
    private const string OperatorCancelled = "operator_cancelled";

    public async Task<CancelOrderResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var payload = new OrdersCancelRequestPayload(command.OrderId, OrderReference: null, OperatorCancelled, command.Note);

        var reply = await rpc.CallAsync<OrdersCancelRequestPayload, OrdersCancelReplyPayload>(
            GatewaySubjects.OrdersCancel,
            payload,
            new RpcCallMeta(command.OrderId, Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);

        // The RESPONDER's own CancellationReason, not a hard-coded
        // "operator_cancelled" — Orders' OrdersCreateResponder.ToReplyPayload
        // populates it only once the order has actually reached
        // `cancelled` (openapi.yaml CancelOrderResponse's own wording:
        // "present only once the order has actually reached cancelled";
        // absent while compensation is still pending). Passing through
        // what the responder said is more faithful than always echoing the
        // request's own reason back.
        return new CancelOrderResult(reply.OrderId, reply.OrderReference, reply.Status, reply.CancellationReason, reply.CompensationPlanned ?? []);
    }
}
