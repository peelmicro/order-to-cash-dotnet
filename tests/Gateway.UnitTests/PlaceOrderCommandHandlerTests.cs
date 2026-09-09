using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Domain.Orders;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class PlaceOrderCommandHandlerTests
{
    private static PlaceOrderCommand Command(Guid? idempotencyKey = null) => new(
        "CarrefourEs",
        "IBERFOODS",
        "EUR",
        [new PlaceOrderCommandLine("PRD-0001", 5, null, null)],
        OrderDiscount: null,
        Notes: "demo",
        IdempotencyKey: idempotencyKey);

    [Fact]
    public async Task HandleAsync_SendsTheRequestOnTheOrdersCreateSubject_CarryingTheIdempotencyKeyAsRequestId()
    {
        var rpc = new FakeRpcClient();
        var idempotencyKey = Guid.NewGuid();
        var replyOrderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCreate, new OrdersCreateReplyPayload(replyOrderId, "ORD-000042", "placed", "EUR", 124250, 0, 124250, DateTimeOffset.UtcNow));
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 100);
        var handler = new PlaceOrderCommandHandler(rpc, window);

        await handler.HandleAsync(Command(idempotencyKey), CancellationToken.None);

        var call = Assert.Single(rpc.Calls);
        Assert.Equal(GatewaySubjects.OrdersCreate, call.Subject);
        var request = Assert.IsType<OrdersCreateRequestPayload>(call.Payload);
        Assert.Equal(idempotencyKey, request.RequestId);
    }

    /// <summary>F3 (#7 review finding, ported) — the reply's OWN orderId, not the request's, is what must be recorded: this is what lets GET /orders/{id} answer 202 rather than a false 404 for this order right after placement.</summary>
    [Fact]
    public async Task HandleAsync_RecordsTheReplysOrderId_InTheIssuedOrderWindow()
    {
        var rpc = new FakeRpcClient();
        var replyOrderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCreate, new OrdersCreateReplyPayload(replyOrderId, "ORD-000042", "placed", "EUR", 124250, 0, 124250, DateTimeOffset.UtcNow));
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 100);
        var handler = new PlaceOrderCommandHandler(rpc, window);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(replyOrderId, result.OrderId);
        Assert.True(window.IsRecentlyIssued(replyOrderId));
        Assert.False(window.IsRecentlyIssued(Guid.NewGuid()));
    }

    [Fact]
    public async Task HandleAsync_AlwaysAnswersProjectionPendingTrue_RegardlessOfHowQuicklyTheReplyArrived()
    {
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.OrdersCreate, new OrdersCreateReplyPayload(Guid.NewGuid(), "ORD-000042", "placed", "EUR", 124250, 0, 124250, DateTimeOffset.UtcNow));
        var window = new IssuedOrderWindow(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5), 100);
        var handler = new PlaceOrderCommandHandler(rpc, window);

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.ProjectionPending);
        Assert.Equal("placed", result.Status);
    }
}
