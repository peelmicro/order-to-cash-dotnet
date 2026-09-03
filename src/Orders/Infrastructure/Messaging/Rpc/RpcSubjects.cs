namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

/// <summary>
/// The two RPC subjects this feature speaks — <c>specs/shared/asyncapi.yaml</c>
/// <c>channels.ordersCreate.address</c> and <c>channels.stockCheck.address</c>.
/// Guarded by <c>tests/Orders.UnitTests/RpcSubjectsTests.cs</c>, which reads
/// <c>asyncapi.yaml</c> as text rather than retyping the subjects — the same
/// discipline <c>OrdersFactTopic</c> already follows for the Kafka topic.
/// </summary>
public static class RpcSubjects
{
    public const string OrdersCreate = "orders.create";

    public const string StockCheck = "fulfillment.stock.check";
}
