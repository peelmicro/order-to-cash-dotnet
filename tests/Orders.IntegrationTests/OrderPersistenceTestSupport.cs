using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using CurrencyEntity = OrderToCash.Orders.Infrastructure.Persistence.Entities.Currency;
using DomainOrder = OrderToCash.Orders.Domain.Order;
using DomainOrderLineRequest = OrderToCash.Orders.Domain.OrderLineRequest;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Minimal reference-catalogue rows (one currency, one retailer, one
/// company, two products) so an integration test can drive a real
/// <c>Order.Place(...)</c> through the real repository without depending on
/// feature <c>seed_job</c>'s full dataset. Codes and GLNs mirror
/// <c>Orders.UnitTests.OrderTestData</c> so the two suites read the same
/// way.
/// </summary>
internal static class OrderPersistenceTestSupport
{
    public const string Currency = "EUR";
    public const string RetailerCode = "RETAILER-01";
    public const string CompanyCode = "COMPANY-01";
    public const string BuyerGlnValue = "4006381333931";
    public const string SupplierGlnValue = "5001234567890";
    public const string ProductCode1 = "PROD-001";
    public const string ProductCode2 = "PROD-002";

    public static readonly GLN BuyerGln = new(BuyerGlnValue);
    public static readonly GLN SupplierGln = new(SupplierGlnValue);

    public static async Task SeedReferenceDataAsync(OrdersDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();

        db.Currencies.Add(new CurrencyEntity { Id = currencyId, Code = Currency, IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });
        db.Retailers.Add(new Retailer { Id = Guid.NewGuid(), Code = RetailerCode, Name = "Test Retailer", Country = "FR", Vat = "FR00000000000", Gln = BuyerGlnValue, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Companies.Add(new Company { Id = Guid.NewGuid(), Code = CompanyCode, Name = "Test Company", Country = "FR", Vat = "FR00000000001", Gln = SupplierGlnValue, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = ProductCode1, Ean = "1000000000017", Name = "Product One", Description = "First product", Price = 1_000, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = ProductCode2, Ean = "1000000000024", Name = "Product Two", Description = "Second product", Price = 500, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static IReadOnlyList<DomainOrderLineRequest> TwoLines() =>
    [
        new DomainOrderLineRequest(ProductCode1, "First product", new Quantity(2), new Money(1_000, Currency), new Money(50, Currency)),
        new DomainOrderLineRequest(ProductCode2, "Second product", new Quantity(1), new Money(500, Currency), Money.Zero(Currency)),
    ];

    public static DomainOrder Place(OrderNumber reference, DateTimeOffset occurredAt, UniqueId causationId, IReadOnlyList<DomainOrderLineRequest>? lines = null, string? notes = null) =>
        DomainOrder.Place(
            orderReference: reference,
            orderDate: occurredAt,
            retailerCode: RetailerCode,
            buyerGln: BuyerGln,
            companyCode: CompanyCode,
            supplierGln: SupplierGln,
            currency: Currency,
            lines: lines ?? TwoLines(),
            notes: notes,
            occurredAt: occurredAt,
            causationId: causationId);
}
