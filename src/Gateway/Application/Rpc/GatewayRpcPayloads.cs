using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Rpc;

namespace OrderToCash.Gateway.Application.Rpc;

// The request/reply payload records of the Gateway's own subjects that
// src/Contracts/Rpc does NOT already declare, transcribed from
// specs/shared/asyncapi.yaml.
//
// Backlog id 84 (gateway_keeps_a_third_copy_of_the_now_canonical_rpc_payloads)
// — feature 76 moved the fulfillment.stock.*, billing.credit.*,
// billing.invoice.* and billing.payment.register payload records into
// src/Contracts/Rpc and unified the caller-side and responder-side copies
// there, precisely so that two copies of one RPC contract cannot drift
// apart. That argument does not stop at the Gateway boundary, so the
// seventeen records this file used to declare for those subjects are gone:
// the Gateway now CALLS them through the one canonical copy in
// OrderToCash.Contracts.Rpc (and OrderToCash.Contracts.Facts.InvoiceLine),
// exactly as Orders' own saga-side caller does. The enumeration that
// classified every record here, one line each, is in
// progress/impl_batch_d1_gateway_payload_dedup_and_key_set_guards.md.
//
// What remains is exactly two groups, and both are deliberate:
//
// 1. orders.create / orders.cancel / catalog.reference.list. Contracts/Rpc
//    declares NO counterpart for any of these — the responder-side records
//    live in src/Orders/Presentation/Rpc/, under the older repository rule
//    that "RPC payloads live in the service that speaks them"
//    (Orders design.md §4.3/§6.3), which feature 76 superseded only for the
//    six subjects it actually moved. Unifying these would mean moving
//    Orders' own Presentation records into Contracts, which is a different
//    change against a different feature's design and is NOT part of id 84
//    (its first acceptance bullet matches against src/Contracts/Rpc only).
//
// 2. GatewayInvoiceViewPayload / GatewayInvoiceListReplyPayload. These are
//    the one genuine FIELD DIFFERENCE the id 84 enumeration found, so per
//    its second acceptance bullet they are NOT unified: they are recorded
//    here with their reason and left alone. Contracts' InvoiceViewPayload
//    carries exactly asyncapi.yaml's InvoiceView (twelve properties); the
//    Gateway's carries a thirteenth, the optional `lines` that
//    openapi.yaml's own `Invoice` schema declares and Billing's responder
//    never sends (review D8 of gateway_rest_auth, "a comment, not a
//    change"). Adding `lines` to the Contracts copy would break Billing's
//    BC23 exact-match case, and dropping it from the Gateway's would
//    reverse a ruling taken in another feature's review — so the two copies
//    stay distinct, and the `Gateway` prefix says so at every use site
//    rather than leaving two same-named types in two namespaces for the
//    compiler to disambiguate. GatewayInvoiceListReplyPayload follows it
//    only because it is the wrapper whose `Items` are those views; its own
//    two properties are identical to the Contracts copy's.
//
// Property names, not JSON attributes, are what make every record below
// wire-compatible with the responder that answers it: every service in this
// repository serialises through the SAME
// OrderToCash.Contracts.Wire.JsonWire options (camelCase, nulls omitted),
// so a PascalCase C# property name here reaches the wire as the exact
// camelCase key the real responder's own record produces.

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

// -- billing.invoice.list, the DELIBERATELY separate pair (group 2 above) ----

/// <summary>
/// <c>asyncapi.yaml</c>'s <c>InvoiceView</c> PLUS the optional <c>lines</c>
/// member <c>openapi.yaml</c>'s own <c>Invoice</c> schema declares — the one
/// Gateway payload record that is NOT
/// <see cref="OrderToCash.Contracts.Rpc.InvoiceViewPayload"/>, kept separate
/// deliberately (backlog id 84, acceptance bullet 2 and 5; the reason is in
/// this file's header). Billing's responder never sends <c>lines</c>, so the
/// member round-trips to <see langword="null"/> and is omitted from the wire.
/// </summary>
public sealed record GatewayInvoiceViewPayload(
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
    IReadOnlyList<InvoiceLine>? Lines);

/// <summary>
/// <c>asyncapi.yaml</c>'s <c>InvoiceListReplyPayload</c>. Structurally
/// identical to <see cref="OrderToCash.Contracts.Rpc.InvoiceListReplyPayload"/>
/// property-for-property, and kept separate for ONE reason only: its
/// <c>Items</c> are <see cref="GatewayInvoiceViewPayload"/>, the deliberately
/// divergent view above. Unifying this wrapper alone would silently drop the
/// <c>lines</c> member from the Gateway's deserialisation target.
/// </summary>
public sealed record GatewayInvoiceListReplyPayload(IReadOnlyList<GatewayInvoiceViewPayload> Items, InvoicePageInfo Page);
