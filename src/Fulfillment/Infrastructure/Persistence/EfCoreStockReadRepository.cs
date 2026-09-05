using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// Check + paged list, plain <c>AsNoTracking</c> queries (design.md §7.3).
/// Under RCSI these are versioned reads that block nobody — never a lock
/// hint, never a transaction (`R31`).
/// </summary>
public sealed class EfCoreStockReadRepository(FulfillmentDbContext db) : IStockReadPort
{
    public async Task<StockCheckReplyPayload> AvailabilityAsync(string companyCode, IReadOnlyList<StockCheckRequestLine> lines, CancellationToken cancellationToken)
    {
        var productCodes = lines.Select(l => l.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var rows = await db.Stocks
            .AsNoTracking()
            .Where(s => s.CompanyCode == companyCode && productCodes.Contains(s.ProductCode))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var rowsByProduct = rows.ToDictionary(r => r.ProductCode, StringComparer.OrdinalIgnoreCase);

        var replyLines = new List<StockCheckReplyLine>(lines.Count);
        foreach (var line in lines)
        {
            if (rowsByProduct.TryGetValue(line.ProductCode, out var row))
            {
                var available = row.Units - row.ReservedUnits;
                replyLines.Add(new StockCheckReplyLine(line.ProductCode, line.Quantity, available, line.Quantity <= available));
            }
            else
            {
                // FS22 / R31: an unknown product answers available: 0,
                // sufficient: false — NEVER an RpcError. This is the only
                // reply shape stock.check can produce for a well-formed
                // request.
                replyLines.Add(new StockCheckReplyLine(line.ProductCode, line.Quantity, 0, false));
            }
        }

        return new StockCheckReplyPayload(replyLines.All(l => l.Sufficient), replyLines);
    }

    public async Task<StockListReplyPayload> ListAsync(StockListRequestPayload query, CancellationToken cancellationToken)
    {
        var page = query.Page is > 0 ? query.Page.Value : 1;
        var pageSize = query.PageSize is > 0 and <= 200 ? query.PageSize.Value : 25;

        var baseQuery = db.Stocks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(query.CompanyCode))
        {
            baseQuery = baseQuery.Where(s => s.CompanyCode == query.CompanyCode);
        }

        if (!string.IsNullOrEmpty(query.ProductCode))
        {
            baseQuery = baseQuery.Where(s => s.ProductCode == query.ProductCode);
        }

        if (query.BelowThreshold == true)
        {
            // Expressed in SQL, not pulled into memory first.
            baseQuery = baseQuery.Where(s => s.Units - s.ReservedUnits < s.LowStockThreshold);
        }

        var total = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await baseQuery
            .OrderBy(s => s.CompanyCode).ThenBy(s => s.ProductCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows
            .Select(r => new StockViewPayload(r.CompanyCode, r.ProductCode, r.Units, r.ReservedUnits, r.Units - r.ReservedUnits, r.LowStockThreshold))
            .ToList();

        return new StockListReplyPayload(items, new StockPageInfo(page, pageSize, total));
    }
}
