using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// Resolves <c>retailerCode</c>/<c>companyCode</c>/<c>currency</c>/<c>productCode</c>
/// against <c>otc_orders</c>' own reference catalogue — read-only,
/// <c>AsNoTracking()</c>, no join across a context boundary (orders_aggregate
/// design.md §8.3: all four reference tables live in this service's own
/// database).
/// </summary>
public sealed class EfCoreOrderReferenceCatalog(OrdersDbContext db) : IOrderReferenceCatalog
{
    public async Task<PartyReference?> FindRetailerAsync(string retailerCode, CancellationToken cancellationToken)
    {
        var row = await db.Retailers.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Code == retailerCode, cancellationToken).ConfigureAwait(false);

        return row is null ? null : new PartyReference(row.Code, new GLN(row.Gln));
    }

    public async Task<PartyReference?> FindCompanyAsync(string companyCode, CancellationToken cancellationToken)
    {
        var row = await db.Companies.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == companyCode, cancellationToken).ConfigureAwait(false);

        return row is null ? null : new PartyReference(row.Code, new GLN(row.Gln));
    }

    public Task<bool> CurrencyExistsAsync(string currencyCode, CancellationToken cancellationToken) =>
        db.Currencies.AsNoTracking().AnyAsync(c => c.Code == currencyCode, cancellationToken);

    public async Task<IReadOnlyDictionary<string, ProductReference>> FindProductsAsync(IReadOnlyCollection<string> productCodes, CancellationToken cancellationToken)
    {
        var rows = await db.Products.AsNoTracking()
            .Where(p => productCodes.Contains(p.Code))
            .Join(db.Currencies.AsNoTracking(), p => p.CurrencyId, c => c.Id, (p, c) => new { Product = p, CurrencyCode = c.Code })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.ToDictionary(
            row => row.Product.Code,
            row => new ProductReference(row.Product.Code, row.Product.Description, new Money(row.Product.Price, row.CurrencyCode)),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<ProductCatalogEntry>> ListProductsAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking()
            .Join(db.Currencies.AsNoTracking(), p => p.CurrencyId, c => c.Id, (p, c) => new { Product = p, CurrencyCode = c.Code });

        if (!includeDisabled)
        {
            query = query.Where(row => row.Product.DisabledAt == null);
        }

        var rows = await query.OrderBy(row => row.Product.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new ProductCatalogEntry(
                row.Product.Code,
                row.Product.Ean,
                row.Product.Name,
                row.Product.Description,
                new Money(row.Product.Price, row.CurrencyCode),
                row.Product.DisabledAt is null))
            .ToList();
    }

    public async Task<IReadOnlyList<PartyCatalogEntry>> ListRetailersAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var query = db.Retailers.AsNoTracking()
            .Join(db.Currencies.AsNoTracking(), r => r.CurrencyId, c => c.Id, (r, c) => new { Party = r, CurrencyCode = c.Code });

        if (!includeDisabled)
        {
            query = query.Where(row => row.Party.DisabledAt == null);
        }

        var rows = await query.OrderBy(row => row.Party.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new PartyCatalogEntry(row.Party.Code, row.Party.Name, row.Party.Country, row.Party.Vat, new GLN(row.Party.Gln), row.CurrencyCode, row.Party.DisabledAt is null))
            .ToList();
    }

    public async Task<IReadOnlyList<PartyCatalogEntry>> ListCompaniesAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var query = db.Companies.AsNoTracking()
            .Join(db.Currencies.AsNoTracking(), co => co.CurrencyId, c => c.Id, (co, c) => new { Party = co, CurrencyCode = c.Code });

        if (!includeDisabled)
        {
            query = query.Where(row => row.Party.DisabledAt == null);
        }

        var rows = await query.OrderBy(row => row.Party.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new PartyCatalogEntry(row.Party.Code, row.Party.Name, row.Party.Country, row.Party.Vat, new GLN(row.Party.Gln), row.CurrencyCode, row.Party.DisabledAt is null))
            .ToList();
    }

    public async Task<IReadOnlyList<CurrencyCatalogEntry>> ListCurrenciesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Currencies.AsNoTracking().OrderBy(c => c.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => new CurrencyCatalogEntry(row.Code, row.IsoNumber, row.Symbol, row.DecimalPoints))
            .ToList();
    }
}
