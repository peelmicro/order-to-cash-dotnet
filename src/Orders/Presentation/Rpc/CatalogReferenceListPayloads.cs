namespace OrderToCash.Orders.Presentation.Rpc;

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

    /// <summary>What an omitted or empty <c>kinds</c> means — "Omit for all of them" (<c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds</c>'s own description).</summary>
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
/// every field here is nullable and <see cref="Infrastructure.Messaging.Rpc.RpcJson"/>'s
/// shared <c>JsonWire.Options</c> (nulls omitted) is what turns an
/// un-requested collection into an ABSENT key on the wire, never a
/// present-but-null one.
/// </summary>
public sealed record CatalogReferenceListReplyPayload(
    IReadOnlyList<ProductPayload>? Products,
    IReadOnlyList<PartyPayload>? Retailers,
    IReadOnlyList<PartyPayload>? Companies,
    IReadOnlyList<CurrencyViewPayload>? Currencies);
