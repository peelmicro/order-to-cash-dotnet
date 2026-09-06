using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// Three plain queries, no transaction, no lock hint (design.md §7.3) —
/// under RCSI these are versioned reads that block nobody: `R31`'s
/// equivalent property for this service is that a read blocks nobody (§15
/// ledger <c>L9</c>). The page of <c>credits</c>, the <c>CountAsync</c>, and
/// one read of the page's <c>credit_items</c> — folded through the SAME
/// <see cref="CreditExposure.Summarise"/> the write side uses, so the
/// <c>CreditView</c>'s three amounts can never tell a different story than
/// <c>BC5</c>'s two-term sum.
/// </summary>
public sealed class EfCoreCreditReadRepository(BillingDbContext db) : ICreditReadPort
{
    public async Task<CreditListReplyPayload> ListAsync(CreditListRequestPayload query, CancellationToken cancellationToken)
    {
        var page = query.Page is > 0 ? query.Page.Value : 1;
        var pageSize = query.PageSize is > 0 and <= 200 ? query.PageSize.Value : 25;

        var baseQuery = db.Credits.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(query.RetailerCode))
        {
            baseQuery = baseQuery.Where(c => c.RetailerCode == query.RetailerCode);
        }

        if (!string.IsNullOrEmpty(query.CompanyCode))
        {
            baseQuery = baseQuery.Where(c => c.CompanyCode == query.CompanyCode);
        }

        var total = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var creditRows = await baseQuery
            .OrderBy(c => c.RetailerCode).ThenBy(c => c.CompanyCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var creditIds = creditRows.Select(c => c.Id).ToArray();

        var itemRows = await db.CreditItems
            .AsNoTracking()
            .Where(i => creditIds.Contains(i.CreditId))
            .Select(i => new { i.CreditId, i.OrderReference, i.Amount, i.Type })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var itemsByCreditId = itemRows
            .GroupBy(i => i.CreditId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<CreditViewPayload>(creditRows.Count);
        foreach (var creditRow in creditRows)
        {
            var entries = itemsByCreditId.TryGetValue(creditRow.Id, out var rows)
                ? rows.Select(r => new CreditLedgerEntrySnapshot(
                    SharedKernel.UniqueId.New(), // read-only projection: the entry's own id plays no part in Summarise
                    r.OrderReference,
                    new SharedKernel.Money(r.Amount, creditRow.CurrencyCode),
                    CreditEntryTypes.Parse(r.Type),
                    DateTimeOffset.UtcNow)).ToList()
                : [];

            var summary = CreditExposure.Summarise(entries);
            var availableCredit = creditRow.CreditLimit - summary.CommittedExposure;

            items.Add(new CreditViewPayload(
                creditRow.Code,
                creditRow.RetailerCode,
                creditRow.CompanyCode,
                creditRow.CurrencyCode,
                creditRow.CreditLimit,
                summary.ActiveHolds,
                summary.OpenExposure,
                availableCredit));
        }

        return new CreditListReplyPayload(items, new CreditPageInfo(page, pageSize, total));
    }
}
