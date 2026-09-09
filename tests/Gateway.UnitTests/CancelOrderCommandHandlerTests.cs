using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class CancelOrderCommandHandlerTests
{
    /// <summary>
    /// The order id IS known here (unlike placeOrder), so it — not a
    /// fresh gateway request id — must be the RPC correlationId, matching
    /// every fact this order's saga has produced or will produce
    /// (asyncapi.yaml <c>rpcCorrelationId</c>: "the order id when the
    /// request concerns a known order"). A wrong correlationId here is
    /// exactly the class of wire-mismatch bug this feature's own known-risk
    /// section names.
    /// </summary>
    [Fact]
    public async Task HandleAsync_UsesTheOrderIdAsTheRpcCorrelationId_NeverAFreshRequestId()
    {
        var rpc = new FakeRpcClient();
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCancel, new OrdersCancelReplyPayload(orderId, "ORD-000042", "placed", [], null));
        var handler = new CancelOrderCommandHandler(rpc);

        await handler.HandleAsync(new CancelOrderCommand(orderId, "operator note"), CancellationToken.None);

        var call = Assert.Single(rpc.Calls);
        Assert.Equal(orderId, call.Meta.CorrelationId);
    }

    [Fact]
    public async Task HandleAsync_SendsTheConstReasonAndTheOperatorsNote()
    {
        var rpc = new FakeRpcClient();
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCancel, new OrdersCancelReplyPayload(orderId, "ORD-000042", "placed", [], null));
        var handler = new CancelOrderCommandHandler(rpc);

        await handler.HandleAsync(new CancelOrderCommand(orderId, "please stop"), CancellationToken.None);

        var request = Assert.IsType<OrdersCancelRequestPayload>(Assert.Single(rpc.Calls).Payload);
        Assert.Equal("operator_cancelled", request.Reason);
        Assert.Equal("please stop", request.Note);
        Assert.Equal(orderId, request.OrderId);
    }

    /// <summary>The RESPONDER's own CancellationReason must pass through — never a hard-coded echo of the request's own reason, since the wire contract says it is present only once the order has actually reached `cancelled` (openapi.yaml CancelOrderResponse).</summary>
    [Fact]
    public async Task HandleAsync_PassesThroughTheRespondersOwnCancellationReason_NeverHardCodingIt()
    {
        var rpc = new FakeRpcClient();
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCancel, new OrdersCancelReplyPayload(orderId, "ORD-000042", "stock_reserved", ["credit_release"], CancellationReason: null));
        var handler = new CancelOrderCommandHandler(rpc);

        var result = await handler.HandleAsync(new CancelOrderCommand(orderId, null), CancellationToken.None);

        // Compensation still pending — the responder's own reply says the
        // reason is not populated yet, and the handler must not invent one.
        Assert.Null(result.CancellationReason);
        Assert.Equal(["credit_release"], result.CompensationPlanned);
    }

    [Fact]
    public async Task HandleAsync_ReturnsAnEmptyCompensationPlannedList_WhenTheResponderSendsNone()
    {
        var rpc = new FakeRpcClient();
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCancel, new OrdersCancelReplyPayload(orderId, "ORD-000042", "cancelled", CompensationPlanned: null!, "operator_cancelled"));
        var handler = new CancelOrderCommandHandler(rpc);

        var result = await handler.HandleAsync(new CancelOrderCommand(orderId, null), CancellationToken.None);

        Assert.Empty(result.CompensationPlanned);
    }
}
