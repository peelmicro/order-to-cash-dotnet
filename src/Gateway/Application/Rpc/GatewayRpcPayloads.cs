namespace OrderToCash.Gateway.Application.Rpc;

// The request/reply payload records of every subject the Gateway calls,
// transcribed from specs/shared/asyncapi.yaml — the Gateway's OWN copy,
// never a reference to Orders'/Fulfillment's/Billing's own Infrastructure
// payload files, per the established repository rule that "RPC payloads
// live in the service that speaks them" (design.md §4.3/§6.3 in those
// services). Every property name below was cross-checked against the
// PRODUCTION payload record it must be wire-compatible with:
// src/Orders/Presentation/Rpc/OrdersCreatePayloads.cs,
// src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs,
// src/Orders/Presentation/Rpc/CatalogReferenceListPayloads.cs,
// src/Fulfillment/Infrastructure/Messaging/Rpc/StockRpcPayloads.cs,
// src/Billing/Infrastructure/Messaging/Rpc/CreditRpcPayloads.cs,
// src/Billing/Infrastructure/Messaging/Rpc/InvoiceRpcPayloads.cs. Property
// names, not JSON attributes, are what make this wire-compatible: every
// service in this repository serialises through the SAME
// OrderToCash.Contracts.Wire.JsonWire options (camelCase, nulls omitted),
// so a PascalCase C# property name here reaches the wire as the exact
// camelCase key the real responder's own record produces from its own,
// separately-declared property of the same name.

// -- orders.create ------------------------------------------------------

public sealed record OrdersCreateRequestLine(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

public sealed record OrdersCreateRequestPayload(
    Guid? RequestId,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<OrdersCreateRequestLine> Lines,
    long? OrderDiscount,
    string? Notes);

public sealed record OrdersCreateReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    DateTimeOffset OrderDate);

// -- orders.cancel --------------------------------------------------------

public sealed record OrdersCancelRequestPayload(Guid? OrderId, string? OrderReference, string? Reason, string? Note);

public sealed record OrdersCancelReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    IReadOnlyList<string> CompensationPlanned,
    string? CancellationReason);

// -- catalog.reference.list ------------------------------------------------

public static class CatalogReferenceKinds
{
    public const string Products = "products";
    public const string Retailers = "retailers";
    public const string Companies = "companies";
    public const string Currencies = "currencies";
}

public sealed record CatalogReferenceListRequestPayload(IReadOnlyList<string>? Kinds, bool? IncludeDisabled);

public sealed record ProductPayload(string Code, string? Ean, string Name, string? Description, long Price, string Currency, bool Enabled);

public sealed record PartyPayload(string Code, string Name, string Country, string? Vat, string Gln, string Currency, bool Enabled);

public sealed record CurrencyViewPayload(string Code, string? IsoNumber, string? Symbol, int DecimalPoints);

public sealed record CatalogReferenceListReplyPayload(
    IReadOnlyList<ProductPayload>? Products,
    IReadOnlyList<PartyPayload>? Retailers,
    IReadOnlyList<PartyPayload>? Companies,
    IReadOnlyList<CurrencyViewPayload>? Currencies);

// -- fulfillment.stock.list -------------------------------------------------

public sealed record StockPageInfo(int Page, int PageSize, int Total);

public sealed record StockListRequestPayload(int? Page, int? PageSize, string? CompanyCode, string? ProductCode, bool? BelowThreshold);

public sealed record StockViewPayload(string CompanyCode, string ProductCode, int Units, int ReservedUnits, int AvailableUnits, int LowStockThreshold);

public sealed record StockListReplyPayload(IReadOnlyList<StockViewPayload> Items, StockPageInfo Page);

// -- fulfillment.stock.replenish ---------------------------------------------

public sealed record StockReplenishRequestLine(string ProductCode, int Units);

public sealed record StockReplenishRequestPayload(string CompanyCode, IReadOnlyList<StockReplenishRequestLine> Lines);

public sealed record StockReplenishReplyPayload(IReadOnlyList<StockViewPayload> Items);

// -- billing.credit.list -----------------------------------------------------

public sealed record CreditPageInfo(int Page, int PageSize, int Total);

public sealed record CreditListRequestPayload(int? Page, int? PageSize, string? RetailerCode, string? CompanyCode);

public sealed record CreditViewPayload(
    string CreditCode,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long CreditLimit,
    long ActiveHolds,
    long OpenExposure,
    long AvailableCredit);

public sealed record CreditListReplyPayload(IReadOnlyList<CreditViewPayload> Items, CreditPageInfo Page);

// -- billing.invoice.list -----------------------------------------------------

public sealed record InvoicePageInfo(int Page, int PageSize, int Total);

public sealed record InvoiceListRequestPayload(
    int? Page,
    int? PageSize,
    string? Status,
    string? RetailerCode,
    string? CompanyCode,
    string? OrderReference,
    int? IssuedBeforeMinutes);

public sealed record InvoiceLinePayload(string ProductCode, int Units, long UnitPrice);

public sealed record InvoiceViewPayload(
    Guid InvoiceId,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long Amount,
    long Discount,
    long TotalAmount,
    string Status,
    DateTimeOffset? PaidAt,
    IReadOnlyList<InvoiceLinePayload>? Lines);

public sealed record InvoiceListReplyPayload(IReadOnlyList<InvoiceViewPayload> Items, InvoicePageInfo Page);

// -- billing.payment.register --------------------------------------------

/// <summary>The nested <c>{ amount, currency }</c> object asyncapi.yaml's <c>Money</c> schema declares.</summary>
public sealed record GatewayRpcMoney(long Amount, string Currency);

public sealed record PaymentRegisterRequestPayload(
    string PaymentReference,
    GatewayRpcMoney Amount,
    DateTimeOffset ValueDate,
    string Source,
    Guid? InvoiceId,
    string? InvoiceReference);

public sealed record PaymentRegisterReplyPayload(
    string Outcome,
    string PaymentReference,
    string InvoiceReference,
    string OrderReference,
    string InvoiceStatus,
    DateTimeOffset? PaidAt);
