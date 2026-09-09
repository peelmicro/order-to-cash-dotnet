using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature <c>orders_catalog_responder</c>'s list-shaped half of
/// <c>IOrderReferenceCatalog</c>, against a REAL MS-SQL database — the same
/// EF Core join-on-currency shape <c>EfCoreOrderReferenceCatalogTests</c>
/// (the find-shaped half, feature <c>orders_acceptance</c>) already proves,
/// extended to the list methods this feature adds.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class EfCoreOrderReferenceCatalogListTests(MsSqlContainerFixture mssql)
{
    private async Task<(OrdersDbContext Db, EfCoreOrderReferenceCatalog Catalog, Guid CurrencyId)> SeedAsync()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_catalog_list_{Guid.NewGuid():N}");
        var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();
        db.Currencies.Add(new Currency { Id = currencyId, Code = "EUR", IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });

        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "PROD-001", Ean = "1000000000017", Name = "Product One", Description = "First product", Price = 1_000, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "PROD-002", Ean = "1000000000024", Name = "Product Two", Description = "Second product", Price = 500, CurrencyId = currencyId, DisabledAt = now, CreatedAt = now, UpdatedAt = now });

        db.Retailers.Add(new Retailer { Id = Guid.NewGuid(), Code = "RETAILER-01", Name = "Test Retailer", Country = "FR", Vat = "FR00000000000", Gln = "4006381333931", CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Retailers.Add(new Retailer { Id = Guid.NewGuid(), Code = "RETAILER-02", Name = "Disabled Retailer", Country = "FR", Vat = "FR00000000002", Gln = "4006382333930", CurrencyId = currencyId, DisabledAt = now, CreatedAt = now, UpdatedAt = now });

        db.Companies.Add(new Company { Id = Guid.NewGuid(), Code = "COMPANY-01", Name = "Test Company", Country = "FR", Vat = "FR00000000001", Gln = "5001234567890", CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Companies.Add(new Company { Id = Guid.NewGuid(), Code = "COMPANY-02", Name = "Disabled Company", Country = "FR", Vat = "FR00000000003", Gln = "5001235567899", CurrencyId = currencyId, DisabledAt = now, CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();

        return (db, new EfCoreOrderReferenceCatalog(db), currencyId);
    }

    [Fact]
    public async Task ListProductsAsync_IncludeDisabledFalse_ExcludesTheDisabledRowAndCarriesEveryFieldFromTheEnabledOne()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListProductsAsync(includeDisabled: false, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("PROD-001", row.Code);
        Assert.Equal("1000000000017", row.Ean);
        Assert.Equal("Product One", row.Name);
        Assert.Equal("First product", row.Description);
        Assert.Equal(1_000, row.Price.MinorUnits);
        Assert.Equal("EUR", row.Price.Currency);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task ListProductsAsync_IncludeDisabledTrue_IncludesTheDisabledRowWithEnabledFalse()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListProductsAsync(includeDisabled: true, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var disabled = Assert.Single(rows, r => r.Code == "PROD-002");
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public async Task ListRetailersAsync_IncludeDisabledFalse_ExcludesTheDisabledRowAndCarriesEveryFieldFromTheEnabledOne()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListRetailersAsync(includeDisabled: false, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("RETAILER-01", row.Code);
        Assert.Equal("Test Retailer", row.Name);
        Assert.Equal("FR", row.Country);
        Assert.Equal("FR00000000000", row.Vat);
        Assert.Equal("4006381333931", row.Gln.Value);
        Assert.Equal("EUR", row.Currency);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task ListRetailersAsync_IncludeDisabledTrue_IncludesTheDisabledRowWithEnabledFalse()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListRetailersAsync(includeDisabled: true, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var disabled = Assert.Single(rows, r => r.Code == "RETAILER-02");
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public async Task ListCompaniesAsync_IncludeDisabledFalse_ExcludesTheDisabledRowAndCarriesEveryFieldFromTheEnabledOne()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListCompaniesAsync(includeDisabled: false, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("COMPANY-01", row.Code);
        Assert.Equal("Test Company", row.Name);
        Assert.Equal("FR", row.Country);
        Assert.Equal("FR00000000001", row.Vat);
        Assert.Equal("5001234567890", row.Gln.Value);
        Assert.Equal("EUR", row.Currency);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task ListCompaniesAsync_IncludeDisabledTrue_IncludesTheDisabledRowWithEnabledFalse()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListCompaniesAsync(includeDisabled: true, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var disabled = Assert.Single(rows, r => r.Code == "COMPANY-02");
        Assert.False(disabled.Enabled);
    }

    [Fact]
    public async Task ListCurrenciesAsync_ReturnsEveryCurrencyRowFieldForField()
    {
        var (_, catalog, _) = await SeedAsync();

        var rows = await catalog.ListCurrenciesAsync(CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("EUR", row.Code);
        Assert.Equal("978", row.IsoNumber);
        Assert.Equal("€", row.Symbol);
        Assert.Equal(2, row.DecimalPoints);
    }

    /// <summary>
    /// Review advisory A2: the adapter's <c>.OrderBy(row =&gt; row.Product.Code)</c>
    /// (<c>EfCoreOrderReferenceCatalog.cs:58</c>) was previously claimed in
    /// <c>progress/impl_orders_catalog_responder.md</c> but asserted by
    /// nothing. Three codes seeded in a deliberately NON-alphabetical
    /// insertion order (PROD-C, PROD-A, PROD-B) so a passing assertion
    /// cannot be an accident of insertion order matching code order.
    /// </summary>
    [Fact]
    public async Task ListProductsAsync_ThreeRowsSeededOutOfOrder_ReturnsThemOrderedByCode()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_catalog_list_order_{Guid.NewGuid():N}");
        var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();
        db.Currencies.Add(new Currency { Id = currencyId, Code = "EUR", IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "PROD-C", Ean = "1000000000048", Name = "Product C", Description = "Third", Price = 300, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "PROD-A", Ean = "1000000000055", Name = "Product A", Description = "First", Price = 100, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = "PROD-B", Ean = "1000000000062", Name = "Product B", Description = "Second", Price = 200, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        var catalog = new EfCoreOrderReferenceCatalog(db);
        var rows = await catalog.ListProductsAsync(includeDisabled: false, CancellationToken.None);

        Assert.Equal(["PROD-A", "PROD-B", "PROD-C"], rows.Select(r => r.Code));
    }
}
