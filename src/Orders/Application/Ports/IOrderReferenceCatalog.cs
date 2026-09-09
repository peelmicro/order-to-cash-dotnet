using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// A retailer or a company row, resolved by its business code — the two
/// fields <c>PlaceOrderCommandHandler</c> needs before it can call
/// <see cref="Domain.Order.Place"/>, which cannot resolve either itself
/// (domain purity forbids it a database — orders_aggregate design.md §8.3).
/// </summary>
public sealed record PartyReference(string Code, GLN Gln);

/// <summary>
/// A product row, resolved by its business code. <see cref="Price"/> is the
/// CURRENT catalogue price, in the product's own currency — used only when
/// a request line omits <c>unitPrice</c> (<c>asyncapi.yaml</c>
/// <c>OrdersCreateRequestPayload.unitPrice</c>: "When omitted the responder
/// snapshots the catalogue price").
/// </summary>
public sealed record ProductReference(string ProductCode, string? Description, Money Price);

/// <summary>
/// One row of <c>catalog.reference.list</c>'s <c>products</c> collection
/// (<c>asyncapi.yaml</c> <c>components.schemas.Product</c>) — richer than
/// <see cref="ProductReference"/> (which carries only what
/// <c>PlaceOrderCommandHandler</c> needs to price a line) because the
/// listing reply's wire schema requires <c>ean</c>, <c>name</c> and
/// <c>enabled</c> too.
/// </summary>
public sealed record ProductCatalogEntry(string Code, string Ean, string Name, string Description, Money Price, bool Enabled);

/// <summary>
/// One row of <c>catalog.reference.list</c>'s <c>retailers</c>/<c>companies</c>
/// collection (<c>asyncapi.yaml</c> <c>components.schemas.Party</c>) —
/// richer than <see cref="PartyReference"/> for the same reason
/// <see cref="ProductCatalogEntry"/> is richer than <see cref="ProductReference"/>.
/// </summary>
public sealed record PartyCatalogEntry(string Code, string Name, string Country, string Vat, GLN Gln, string Currency, bool Enabled);

/// <summary>One row of <c>catalog.reference.list</c>'s <c>currencies</c> collection (<c>asyncapi.yaml</c> <c>components.schemas.CurrencyView</c>). The <c>otc_orders.currencies</c> table carries no <c>disabled_at</c> column (Databases doc §4.1), so there is no <c>includeDisabled</c> toggle for this collection.</summary>
public sealed record CurrencyCatalogEntry(string Code, string IsoNumber, string Symbol, int DecimalPoints);

/// <summary>
/// Resolves the business codes an <c>orders.create</c> request carries
/// against the Orders context's own reference catalogue (§8.3: "the
/// reference catalogue ... used to compose an order" lives in this
/// context's own database, so this is not a cross-context join) — BEFORE
/// <c>Order.Place</c> runs, and deliberately outside the placing
/// transaction: a reference row disabled or removed between this read and
/// the commit still fails loudly there (the repository re-resolves by code
/// as its own authoritative check), it just fails as a generic write error
/// rather than a clean <c>NOT_FOUND</c> RPC reply — accepted as out of
/// scope for this feature, matching #7's own accepted gap
/// (<c>apps/orders/src/application/ports/order-reference-data.port.ts</c>).
/// </summary>
public interface IOrderReferenceCatalog
{
    Task<PartyReference?> FindRetailerAsync(string retailerCode, CancellationToken cancellationToken);

    Task<PartyReference?> FindCompanyAsync(string companyCode, CancellationToken cancellationToken);

    Task<bool> CurrencyExistsAsync(string currencyCode, CancellationToken cancellationToken);

    /// <summary>Keyed by <c>productCode</c>. A code absent from the returned dictionary was not found in the catalogue.</summary>
    Task<IReadOnlyDictionary<string, ProductReference>> FindProductsAsync(IReadOnlyCollection<string> productCodes, CancellationToken cancellationToken);

    /// <summary>
    /// Feature <c>orders_catalog_responder</c>: the full products
    /// collection for <c>catalog.reference.list</c>. THE SAME registered
    /// implementation <see cref="FindProductsAsync"/> above runs on —
    /// deliberately one port, one adapter, not a second query path that
    /// could drift from the one <c>PlaceOrderCommandHandler</c> already
    /// calls (#7's own design note, aliasing the identical repository
    /// instance for its own catalogue query).
    /// </summary>
    Task<IReadOnlyList<ProductCatalogEntry>> ListProductsAsync(bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>Feature <c>orders_catalog_responder</c>: the full retailers collection for <c>catalog.reference.list</c>.</summary>
    Task<IReadOnlyList<PartyCatalogEntry>> ListRetailersAsync(bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>Feature <c>orders_catalog_responder</c>: the full companies collection for <c>catalog.reference.list</c>.</summary>
    Task<IReadOnlyList<PartyCatalogEntry>> ListCompaniesAsync(bool includeDisabled, CancellationToken cancellationToken);

    /// <summary>Feature <c>orders_catalog_responder</c>: the full currencies collection for <c>catalog.reference.list</c>. No <c>includeDisabled</c> parameter — see <see cref="CurrencyCatalogEntry"/>'s remarks.</summary>
    Task<IReadOnlyList<CurrencyCatalogEntry>> ListCurrenciesAsync(CancellationToken cancellationToken);
}
