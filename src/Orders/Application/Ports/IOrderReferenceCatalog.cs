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
}
