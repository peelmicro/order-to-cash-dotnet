using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Application.Queries;

/// <summary>
/// The <c>catalog.reference.list</c> query. <see cref="Kinds"/> is ALREADY
/// resolved by the responder before this query is built — "omit for all of
/// them" (<c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds</c>'s
/// own description) is a wire-shape concern the responder's own
/// <c>CatalogReferenceKinds.All</c> resolves, so this type carries no
/// optionality the handler would have to re-interpret.
/// </summary>
public sealed record ListCatalogReferenceQuery(IReadOnlyList<string> Kinds, bool IncludeDisabled) : IQuery<CatalogReferenceListResult>;

/// <summary>
/// The query's result — one nullable list per collection. "Only the
/// requested collections are present" (<c>asyncapi.yaml</c>
/// <c>CatalogReferenceListReplyPayload</c>'s own description); the
/// responder maps this to the wire's <c>CatalogReferenceListReplyPayload</c>.
/// </summary>
public sealed record CatalogReferenceListResult(
    IReadOnlyList<ProductCatalogEntry>? Products,
    IReadOnlyList<PartyCatalogEntry>? Retailers,
    IReadOnlyList<PartyCatalogEntry>? Companies,
    IReadOnlyList<CurrencyCatalogEntry>? Currencies);

/// <summary>
/// Feature <c>orders_catalog_responder</c>'s design decision, made explicit
/// (the ported-idiom ledger, <c>progress/impl_orders_catalog_responder.md</c>
/// — corrected after review round 1's D2): #7 satisfied its acceptance
/// bullet "reuses the same reference-data lookup <c>PlaceOrderHandler</c>
/// already calls, exposed as a query rather than duplicated" NOT for free —
/// via an explicit <c>useExisting</c> DI alias between TWO distinct tokens,
/// <c>apps/orders/src/app.module.ts:123-137</c>, carrying its own comment
/// explaining why it is an alias and not a second <c>useFactory</c>. #7 also
/// kept TWO SEPARATE ports on that one adapter
/// (<c>catalog-reference-list.port.ts:1-21</c>), deliberately rejecting a
/// widened single port because it would force
/// <c>place-order.handler.spec.ts</c>'s fake to grow methods it never uses.
/// #8 diverges on THAT point: ONE widened <see cref="IOrderReferenceCatalog"/>
/// port, so there is no second token to alias and nothing to mis-alias —
/// <c>PlaceOrderCommandHandler</c> and THIS handler resolve the identical
/// registration
/// (<c>OrdersAcceptanceServiceCollectionExtensions.AddOrdersAcceptance</c>:
/// exactly one
/// <c>AddScoped&lt;IOrderReferenceCatalog, EfCoreOrderReferenceCatalog&gt;</c>
/// line) — never a second, parallel <c>OrdersDbContext</c> query written
/// beside it. The accepted trade-off: <c>FakeOrderReferenceCatalog</c>
/// (<c>tests/Orders.UnitTests/PlaceOrderTestDoubles.cs</c>) carries four
/// list methods <c>PlaceOrderCommandHandler</c> never calls — exactly what
/// #7's port header predicted widening would cost. Guarded by
/// <c>CatalogReferenceListPortReuseTests.AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation</c>.
/// </summary>
public sealed class ListCatalogReferenceQueryHandler(IOrderReferenceCatalog referenceCatalog)
    : IQueryHandler<ListCatalogReferenceQuery, CatalogReferenceListResult>
{
    public async Task<CatalogReferenceListResult> HandleAsync(ListCatalogReferenceQuery query, CancellationToken cancellationToken)
    {
        var products = query.Kinds.Contains("products", StringComparer.Ordinal)
            ? await referenceCatalog.ListProductsAsync(query.IncludeDisabled, cancellationToken).ConfigureAwait(false)
            : null;

        var retailers = query.Kinds.Contains("retailers", StringComparer.Ordinal)
            ? await referenceCatalog.ListRetailersAsync(query.IncludeDisabled, cancellationToken).ConfigureAwait(false)
            : null;

        var companies = query.Kinds.Contains("companies", StringComparer.Ordinal)
            ? await referenceCatalog.ListCompaniesAsync(query.IncludeDisabled, cancellationToken).ConfigureAwait(false)
            : null;

        var currencies = query.Kinds.Contains("currencies", StringComparer.Ordinal)
            ? await referenceCatalog.ListCurrenciesAsync(cancellationToken).ConfigureAwait(false)
            : null;

        return new CatalogReferenceListResult(products, retailers, companies, currencies);
    }
}
