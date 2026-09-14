namespace OrderToCash.Contracts.Rpc;

// The catalog.reference.list request/reply payload records (and their
// closed kinds enum), transcribed from specs/shared/asyncapi.yaml. Backlog
// id 93 (orders_and_catalog_rpc_payloads_are_still_duplicated_between_
// orders_and_the_gateway) moved these here from
// src/Orders/Presentation/Rpc/CatalogReferenceListPayloads.cs AND unified
// them with the Gateway's own caller-side copy
// (src/Gateway/Application/Rpc/GatewayRpcPayloads.cs's
// catalog.reference.list section, which declared structurally IDENTICAL
// types for the same subject) — see OrdersRpcPayloads.cs's own header for
// the full reasoning and the ported-idiom ledger row. CatalogReferenceKinds
// is a strict merge, not a straight identical-copy: Orders' own copy also
// declared <see cref="CatalogReferenceKinds.All"/> (used by
// <c>CatalogReferenceListRequestValidator</c> and
// <c>OrdersCreateResponder</c> to resolve an omitted <c>kinds</c> to "all
// four"), which the Gateway's copy never declared because the Gateway
// always requests one kind at a time
// (<c>ListCatalogQuery</c>/<c>CatalogEndpoints</c>). Carrying <c>All</c>
// here is harmless to the Gateway — an unused static member — and is the
// superset both sides can now share.

/// <summary>
/// The closed set of collections <c>catalog.reference.list</c> can return —
/// <c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds.items.enum</c>.
/// Guarded by <c>CatalogReferenceListPayloadTests.BC23_...</c>, the same
/// discipline <c>OrdersCreateErrorMapper._contractRpcErrorCodes</c> already
/// follows for its own closed enum.
/// </summary>
public static class CatalogReferenceKinds
{
    public const string Products = "products";
    public const string Retailers = "retailers";
    public const string Companies = "companies";
    public const string Currencies = "currencies";

    /// <summary>What an omitted or empty <c>kinds</c> means — "Omit for all of them" (<c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds</c>'s own description). Read only by Orders' responder/validator — the Gateway never omits <c>kinds</c>, it always names exactly one.</summary>
    public static readonly IReadOnlyList<string> All = [Products, Retailers, Companies, Currencies];
}

/// <summary><c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload</c> — the <c>catalog.reference.list</c> request body. Neither field is required by the schema.</summary>
public sealed record CatalogReferenceListRequestPayload(IReadOnlyList<string>? Kinds, bool? IncludeDisabled);

/// <summary><c>asyncapi.yaml</c> <c>components.schemas.Product</c>.</summary>
public sealed record ProductPayload(string Code, string? Ean, string Name, string? Description, long Price, string Currency, bool Enabled);

/// <summary><c>asyncapi.yaml</c> <c>components.schemas.Party</c> — used for both <c>retailers</c> and <c>companies</c> (the schema's own description: "A retailer or a company as the place-order form sees it").</summary>
public sealed record PartyPayload(string Code, string Name, string Country, string? Vat, string Gln, string Currency, bool Enabled);

/// <summary><c>asyncapi.yaml</c> <c>components.schemas.CurrencyView</c>.</summary>
public sealed record CurrencyViewPayload(string Code, string? IsoNumber, string? Symbol, int DecimalPoints);

/// <summary>
/// <c>asyncapi.yaml</c> <c>CatalogReferenceListReplyPayload</c> — "Only the
/// requested collections are present" (the schema's own description), so
/// every field here is nullable and the shared <c>JsonWire.Options</c>
/// (nulls omitted) is what turns an un-requested collection into an ABSENT
/// key on the wire, never a present-but-null one.
/// </summary>
public sealed record CatalogReferenceListReplyPayload(
    IReadOnlyList<ProductPayload>? Products,
    IReadOnlyList<PartyPayload>? Retailers,
    IReadOnlyList<PartyPayload>? Companies,
    IReadOnlyList<CurrencyViewPayload>? Currencies);
