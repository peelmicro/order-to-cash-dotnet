using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Contracts.Rpc;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// Two plain queries, no transaction, no lock hint (design.md §6.5) — under
/// RCSI these are versioned reads that block nobody (ledger `L12`). Lines
/// are NOT joined — <c>InvoiceView</c> does not carry them and the demo bank
/// robot pages over hundreds of rows.
/// </summary>
public sealed class EfCoreInvoiceReadRepository(BillingDbContext db) : IInvoiceReadPort
{
    public async Task<InvoiceListReplyPayload> ListAsync(InvoiceListRequestPayload query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var page = query.Page is > 0 ? query.Page.Value : 1;
        var pageSize = query.PageSize is > 0 and <= 200 ? query.PageSize.Value : 25;

        var baseQuery = db.Invoices.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(query.Status))
        {
            baseQuery = baseQuery.Where(i => i.Status == query.Status);
        }

        if (!string.IsNullOrEmpty(query.RetailerCode))
        {
            baseQuery = baseQuery.Where(i => i.RetailerCode == query.RetailerCode);
        }

        if (!string.IsNullOrEmpty(query.CompanyCode))
        {
            baseQuery = baseQuery.Where(i => i.CompanyCode == query.CompanyCode);
        }

        if (!string.IsNullOrEmpty(query.OrderReference))
        {
            baseQuery = baseQuery.Where(i => i.OrderReference == query.OrderReference);
        }

        if (query.IssuedBeforeMinutes is { } issuedBeforeMinutes)
        {
            // now.UtcDateTime reaches the parameter — a datetime2(3) column
            // is never compared against a datetimeoffset (ledger L28).
            var cutoff = now.AddMinutes(-issuedBeforeMinutes).UtcDateTime;
            baseQuery = baseQuery.Where(i => i.InvoiceDate <= cutoff);
        }

        var total = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await baseQuery
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InvoiceReference)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(ToViewPayload).ToList();

        return new InvoiceListReplyPayload(items, new InvoicePageInfo(page, pageSize, total));
    }

    private static InvoiceViewPayload ToViewPayload(Entities.Invoice row)
    {
        var invoiceDate = new DateTimeOffset(DateTime.SpecifyKind(row.InvoiceDate, DateTimeKind.Utc));
        var state = InvoiceStatuses.Parse(row.Status, row.PaidAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(row.PaidAt.Value, DateTimeKind.Utc)));

        return new InvoiceViewPayload(
            row.Id,
            row.InvoiceReference,
            invoiceDate,
            row.OrderReference,
            row.RetailerCode,
            row.CompanyCode,
            row.CurrencyCode,
            row.Amount,
            row.Discount,
            row.TotalAmount,
            row.Status,
            state.PaidAtOrNull);
    }
}
