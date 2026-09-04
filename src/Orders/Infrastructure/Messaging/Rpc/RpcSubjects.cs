namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

/// <summary>
/// The RPC subjects this and the saga feature speak —
/// <c>specs/shared/asyncapi.yaml</c> <c>channels.ordersCreate.address</c>,
/// <c>channels.stockCheck.address</c> and, since <c>order_saga_orchestrator</c>
/// (design.md §6.1), the five saga command channels' own <c>address</c>.
/// Guarded by <c>tests/Orders.UnitTests/RpcSubjectsTests.cs</c> and
/// <c>SagaRpcSubjectsTests.cs</c>, which read <c>asyncapi.yaml</c> as text
/// rather than retyping the subjects — the same discipline
/// <c>OrdersFactTopic</c> already follows for the Kafka topic.
/// </summary>
public static class RpcSubjects
{
    public const string OrdersCreate = "orders.create";

    public const string StockCheck = "fulfillment.stock.check";

    // The five saga command subjects (design.md §6.1) — extended here rather
    // than restructured, per tasks.md A3.
    public const string StockReserve = "fulfillment.stock.reserve";

    public const string StockRelease = "fulfillment.stock.release";

    public const string DespatchCreate = "fulfillment.despatch.create";

    public const string CreditHold = "billing.credit.hold";

    public const string InvoiceIssue = "billing.invoice.issue";
}
