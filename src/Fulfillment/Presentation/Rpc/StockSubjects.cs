namespace OrderToCash.Fulfillment.Presentation.Rpc;

/// <summary>
/// The five subjects this responder speaks — <c>specs/shared/asyncapi.yaml</c>
/// channels' own <c>address</c>. Guarded by
/// <c>tests/Fulfillment.UnitTests/StockSubjectsTests.cs</c>, which reads the
/// spec as text rather than retyping the subjects — the discipline
/// <c>OrdersFactTopic</c>/<c>RpcSubjects</c> already establish.
/// </summary>
public static class StockSubjects
{
    public const string StockCheck = "fulfillment.stock.check";

    public const string StockReserve = "fulfillment.stock.reserve";

    public const string StockRelease = "fulfillment.stock.release";

    public const string StockList = "fulfillment.stock.list";

    public const string StockReplenish = "fulfillment.stock.replenish";
}
