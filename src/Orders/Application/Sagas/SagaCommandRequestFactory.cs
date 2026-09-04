using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// Builds the full typed RPC request for an owed <see cref="SagaCommandKind"/>
/// from the loaded, already-transitioned aggregate (design.md §6.3: "the
/// full typed request, serialised through RpcJson at enqueue time from the
/// loaded aggregate"), and serialises it — the one place a saga command's
/// wire body is assembled, so <see cref="SagaFactHandler"/> stays a plain
/// orchestration of ports.
/// </summary>
public static class SagaCommandRequestFactory
{
    public static string BuildJson(SagaCommandKind command, Domain.Order order) => command switch
    {
        SagaCommandKind.StockReserve => RpcJsonString(BuildStockReserve(order)),
        SagaCommandKind.StockRelease => RpcJsonString(BuildStockRelease(order)),
        SagaCommandKind.DespatchCreate => RpcJsonString(new DespatchCreateRequestPayload(order.OrderReference.Value)),
        SagaCommandKind.CreditHold => RpcJsonString(BuildCreditHold(order)),
        SagaCommandKind.InvoiceIssue => RpcJsonString(BuildInvoiceIssue(order)),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unrecognised SagaCommandKind member."),
    };

    private static StockReserveRequestPayload BuildStockReserve(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        [.. order.Lines.Select(line => new StockReserveRequestLine(line.ProductCode, line.Quantity.Value))]);

    /// <summary>
    /// The ONLY producer of <see cref="SagaCommandKind.StockRelease"/> in
    /// this feature is the <c>credit.rejected.v1</c> step (R27), so
    /// <c>reason</c> is fixed to <c>credit_rejected</c> here. Feature 25's
    /// operator-cancellation flow will need its OWN enqueue path with
    /// <c>reason: order_cancelled</c> when it lands — this factory is not
    /// where that decision would be made, because this feature's step table
    /// never triggers it.
    /// </summary>
    private static StockReleaseRequestPayload BuildStockRelease(Domain.Order order) =>
        new(order.OrderReference.Value, "credit_rejected");

    private static CreditHoldRequestPayload BuildCreditHold(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        new SagaMoney(order.TotalAmount.MinorUnits, order.Currency));

    private static InvoiceIssueRequestPayload BuildInvoiceIssue(Domain.Order order) => new(
        order.OrderReference.Value,
        order.RetailerCode,
        order.CompanyCode,
        order.Currency,
        [.. order.Lines.Select(line => new Contracts.Facts.InvoiceLine(line.ProductCode, line.Quantity.Value, line.UnitPrice.MinorUnits))],
        order.InitialDiscount.MinorUnits);

    private static string RpcJsonString<T>(T payload) => System.Text.Encoding.UTF8.GetString(RpcJson.Serialize(payload));
}
