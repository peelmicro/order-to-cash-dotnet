namespace OrderToCash.Gateway.Application.Rpc;

/// <summary>
/// The eight RPC subjects the Gateway calls — <c>specs/shared/asyncapi.yaml</c>
/// channels' own <c>address</c>, transcribed here rather than referenced
/// from any other service's <c>RpcSubjects</c>/<c>StockSubjects</c>/
/// <c>CreditSubjects</c>/<c>InvoiceSubjects</c> class (cross-service
/// project references are not permitted — "database per service", and by
/// extension no service-internal type reaches across the boundary).
/// </summary>
public static class GatewaySubjects
{
    public const string OrdersCreate = "orders.create";
    public const string OrdersCancel = "orders.cancel";
    public const string CatalogReferenceList = "catalog.reference.list";
    public const string StockList = "fulfillment.stock.list";
    public const string StockReplenish = "fulfillment.stock.replenish";
    public const string CreditList = "billing.credit.list";
    public const string InvoiceList = "billing.invoice.list";
    public const string PaymentRegister = "billing.payment.register";
}
