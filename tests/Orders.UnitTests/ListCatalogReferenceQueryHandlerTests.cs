using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Queries;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>ListCatalogReferenceQueryHandler</c> — feature
/// <c>orders_catalog_responder</c>'s acceptance bullet 3, "reuses the same
/// reference-data lookup <c>PlaceOrderHandler</c> already calls, exposed as
/// a query rather than duplicated": exercised here against the SAME
/// <see cref="FakeOrderReferenceCatalog"/> class
/// <c>PlaceOrderCommandHandlerTests</c> already uses.
/// </summary>
public sealed class ListCatalogReferenceQueryHandlerTests
{
    private static readonly ProductCatalogEntry _enabledProduct = new("PROD-001", "1000000000017", "Product One", "First product", new Money(1_000, "EUR"), Enabled: true);
    private static readonly ProductCatalogEntry _disabledProduct = new("PROD-002", "1000000000024", "Product Two", "Second product", new Money(500, "EUR"), Enabled: false);
    private static readonly PartyCatalogEntry _enabledRetailer = new("RETAILER-01", "Test Retailer", "FR", "FR00000000000", new GLN("4006381333931"), "EUR", Enabled: true);
    private static readonly PartyCatalogEntry _enabledCompany = new("COMPANY-01", "Test Company", "FR", "FR00000000001", new GLN("5001234567890"), "EUR", Enabled: true);
    private static readonly CurrencyCatalogEntry _currency = new("EUR", "978", "€", 2);

    private static FakeOrderReferenceCatalog SeededCatalog()
    {
        var catalog = new FakeOrderReferenceCatalog();
        catalog.ProductEntries.AddRange([_enabledProduct, _disabledProduct]);
        catalog.RetailerEntries.Add(_enabledRetailer);
        catalog.CompanyEntries.Add(_enabledCompany);
        catalog.CurrencyEntries.Add(_currency);
        return catalog;
    }

    [Fact]
    public async Task HandleAsync_KindsCarriesOnlyProducts_ReturnsProductsAndLeavesEveryOtherCollectionNull()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        var result = await handler.HandleAsync(new ListCatalogReferenceQuery(["products"], IncludeDisabled: false), CancellationToken.None);

        Assert.NotNull(result.Products);
        Assert.Null(result.Retailers);
        Assert.Null(result.Companies);
        Assert.Null(result.Currencies);

        // Only the requested port method was ever called — the OTHER three
        // fake methods below were never invoked at all, proving the handler
        // does not fetch a collection nobody asked for.
        Assert.Empty(catalog.ListRetailersIncludeDisabledCalls);
        Assert.Empty(catalog.ListCompaniesIncludeDisabledCalls);
        Assert.Equal(0, catalog.ListCurrenciesCallCount);
    }

    [Fact]
    public async Task HandleAsync_AllFourKinds_ReturnsAllFourCollections()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        var result = await handler.HandleAsync(
            new ListCatalogReferenceQuery(["products", "retailers", "companies", "currencies"], IncludeDisabled: false),
            CancellationToken.None);

        Assert.NotNull(result.Products);
        Assert.NotNull(result.Retailers);
        Assert.NotNull(result.Companies);
        Assert.NotNull(result.Currencies);
    }

    /// <summary>
    /// Corruption probe — the handler must carry every field of a returned
    /// row through UNCHANGED, not merely return a non-empty collection.
    /// Field-by-field against the exact entry the fake was seeded with.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ReturnsTheCatalogsProductRowsFieldForFieldUnchanged()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        var result = await handler.HandleAsync(new ListCatalogReferenceQuery(["products"], IncludeDisabled: true), CancellationToken.None);

        var row = Assert.Single(result.Products!, p => p.Code == "PROD-001");
        Assert.Equal(_enabledProduct.Ean, row.Ean);
        Assert.Equal(_enabledProduct.Name, row.Name);
        Assert.Equal(_enabledProduct.Description, row.Description);
        Assert.Equal(_enabledProduct.Price, row.Price);
        Assert.Equal(_enabledProduct.Enabled, row.Enabled);
    }

    [Fact]
    public async Task HandleAsync_IncludeDisabledFalse_PassesFalseThroughToEveryRequestedListMethod()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        await handler.HandleAsync(new ListCatalogReferenceQuery(["products", "retailers", "companies"], IncludeDisabled: false), CancellationToken.None);

        Assert.Equal([false], catalog.ListProductsIncludeDisabledCalls);
        Assert.Equal([false], catalog.ListRetailersIncludeDisabledCalls);
        Assert.Equal([false], catalog.ListCompaniesIncludeDisabledCalls);
    }

    [Fact]
    public async Task HandleAsync_IncludeDisabledTrue_PassesTrueThroughToEveryRequestedListMethod()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        await handler.HandleAsync(new ListCatalogReferenceQuery(["products", "retailers", "companies"], IncludeDisabled: true), CancellationToken.None);

        Assert.Equal([true], catalog.ListProductsIncludeDisabledCalls);
        Assert.Equal([true], catalog.ListRetailersIncludeDisabledCalls);
        Assert.Equal([true], catalog.ListCompaniesIncludeDisabledCalls);
    }

    [Fact]
    public async Task HandleAsync_IncludeDisabledFalse_ExcludesTheDisabledProductRow()
    {
        var catalog = SeededCatalog();
        var handler = new ListCatalogReferenceQueryHandler(catalog);

        var result = await handler.HandleAsync(new ListCatalogReferenceQuery(["products"], IncludeDisabled: false), CancellationToken.None);

        Assert.DoesNotContain(result.Products!, p => p.Code == "PROD-002");
    }
}
