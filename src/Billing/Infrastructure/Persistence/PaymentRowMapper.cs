using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;
using RowPayment = OrderToCash.Billing.Infrastructure.Persistence.Entities.Payment;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// Row &lt;-&gt; <see cref="PaymentSnapshot"/> — feature 22's sibling of
/// <see cref="InvoiceRowMapper"/>/<see cref="BuyerCreditRowMapper"/>. There
/// is no aggregate on this side (<see cref="PaymentSnapshot"/>'s own
/// remark), so this mapper is a plain projection, never a
/// <c>Reconstitute</c>.
/// </summary>
public static class PaymentRowMapper
{
    /// <summary>Instants OUT: <c>value.UtcDateTime</c> — no offset survives a <c>datetime2(3)</c> column, so none should be carried past this point (the same convention <c>InvoiceRowMapper.ToSnapshot</c> states).</summary>
    public static PaymentSnapshot ToSnapshot(RowPayment row) => new(
        UniqueId.From(row.Id),
        row.PaymentReference,
        UniqueId.From(row.InvoiceId),
        new Money(row.Amount, row.CurrencyCode),
        new DateTimeOffset(DateTime.SpecifyKind(row.ValueDate, DateTimeKind.Utc)),
        row.Source);

    /// <summary>Builds a brand-new `payments` row from <see cref="MarkPaidInput"/> plus the invoice id it was resolved against — the ONE INSERT this subject makes, appended alongside the invoice row's UPDATE inside the same transaction.</summary>
    public static RowPayment ToNewRow(MarkPaidInput payment, Guid invoiceId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        PaymentReference = payment.PaymentReference,
        InvoiceId = invoiceId,
        Amount = payment.Amount.MinorUnits,
        CurrencyCode = payment.Amount.Currency,
        ValueDate = payment.ValueDate.UtcDateTime,
        Source = payment.Source,
        CreatedAt = now,
    };
}
