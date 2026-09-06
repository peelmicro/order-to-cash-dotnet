using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;
using RowInvoice = OrderToCash.Billing.Infrastructure.Persistence.Entities.Invoice;
using RowInvoiceItem = OrderToCash.Billing.Infrastructure.Persistence.Entities.InvoiceItem;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// The ONE place `status`/`paid_at` meet <see cref="InvoiceState"/> —
/// design.md §6.3. This is where the store-side half of `BI10` is closed:
/// <see cref="ToSnapshot"/> parses the two independent columns into one
/// <see cref="InvoiceState"/> value (raising <c>UnknownInvoiceStatusError</c>
/// on an unrecognised token), and <see cref="Invoice.Reconstitute"/> is what
/// refuses a `paid`-with-null / `issued`-with-a-date disagreement — that
/// refusal is an aggregate invariant, not a mapping concern, so it does not
/// live here.
/// </summary>
public static class InvoiceRowMapper
{
    /// <summary>Instants OUT: <c>value.UtcDateTime</c> — no offset survives a <c>datetime2(3)</c> column, so none should be carried past this point.</summary>
    public static InvoiceSnapshot ToSnapshot(RowInvoice row, IReadOnlyList<RowInvoiceItem> items)
    {
        // Instants IN: datetime2(3) carries no offset and EF Core hands back
        // DateTimeKind.Unspecified; new DateTimeOffset(unspecified) would
        // apply the MACHINE's local offset, so SpecifyKind(..., Utc) is
        // applied explicitly on every read — including inside the nullable
        // branch for paid_at (`BI24`).
        var invoiceDate = new DateTimeOffset(DateTime.SpecifyKind(row.InvoiceDate, DateTimeKind.Utc));
        DateTimeOffset? paidAt = row.PaidAt is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(row.PaidAt.Value, DateTimeKind.Utc));

        var state = InvoiceStatuses.Parse(row.Status, paidAt);

        var lines = items
            .Select(item => new InvoiceLineSnapshot(
                UniqueId.From(item.Id),
                item.ProductCode,
                new Quantity(item.Units),
                new Money(item.Price, row.CurrencyCode)))
            .ToList();

        return new InvoiceSnapshot(
            UniqueId.From(row.Id),
            row.InvoiceReference,
            invoiceDate,
            OrderNumber.Parse(row.OrderReference),
            row.RetailerCode,
            row.CompanyCode,
            row.CurrencyCode,
            new Money(row.Amount, row.CurrencyCode),
            new Money(row.Discount, row.CurrencyCode),
            new Money(row.TotalAmount, row.CurrencyCode),
            state,
            lines);
    }

    /// <summary>Builds a brand-new `invoices` row — both `status` and `paid_at` derived from <see cref="Invoice.ToSnapshot"/>'s <see cref="InvoiceState"/>, so they cannot disagree on the way out. Amounts: `long` minor units both ways, no cast, no `decimal`.</summary>
    public static RowInvoice ToNewRow(Invoice invoice, DateTime now)
    {
        var snapshot = invoice.ToSnapshot();

        return new RowInvoice
        {
            Id = snapshot.Id.Value,
            InvoiceReference = snapshot.InvoiceReference,
            InvoiceDate = snapshot.InvoiceDate.UtcDateTime,
            CompanyCode = snapshot.CompanyCode,
            RetailerCode = snapshot.RetailerCode,
            OrderReference = snapshot.OrderReference.Value,
            Amount = snapshot.Amount.MinorUnits,
            Discount = snapshot.Discount.MinorUnits,
            TotalAmount = snapshot.TotalAmount.MinorUnits,
            CurrencyCode = snapshot.Currency,
            Status = InvoiceStatuses.ToToken(snapshot.State),
            PaidAt = snapshot.State.PaidAtOrNull?.UtcDateTime,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static RowInvoiceItem ToNewRow(InvoiceLineSnapshot line, Guid invoiceId, DateTime now) => new()
    {
        Id = line.Id.Value,
        InvoiceId = invoiceId,
        ProductCode = line.ProductCode,
        Units = line.Units.Value,
        Price = line.UnitPrice.MinorUnits,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
