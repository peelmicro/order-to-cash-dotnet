using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;
using RowCredit = OrderToCash.Billing.Infrastructure.Persistence.Entities.Credit;
using RowCreditItem = OrderToCash.Billing.Infrastructure.Persistence.Entities.CreditItem;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// Rows &lt;-&gt; <see cref="BuyerCreditSnapshot"/>/<see cref="CreditLedgerEntrySnapshot"/>.
/// Instants per the convention <c>OrderRowMapper.cs</c> fixed and documents
/// in the same words: <c>value.UtcDateTime</c> to write,
/// <c>new DateTimeOffset(value, TimeSpan.Zero)</c> to read.
/// <c>datetime2(3)</c> carries no offset and EF Core hands back
/// <see cref="DateTimeKind.Unspecified"/>; <c>new DateTimeOffset(unspecified)</c>
/// applies the MACHINE's local offset, so a CI runner in a non-UTC zone
/// would read back an instant hours away from what it wrote.
/// <see cref="CreditLedgerEntry.EntryDate"/> is the first #8 child entity
/// carrying a date across this boundary (design.md §7.4, §15 ledger
/// <c>L11</c>; <c>BC24</c>).
/// </summary>
public static class BuyerCreditRowMapper
{
    /// <summary>Builds the snapshot the aggregate reconstitutes from — the <c>credits</c> row, the whole line's committed-exposure scalar, and the loaded <c>credit_items</c> rows for the one order in scope.</summary>
    public static BuyerCreditSnapshot ToDomain(RowCredit creditRow, long committedExposureMinorUnits, IReadOnlyList<RowCreditItem> orderEntryRows) =>
        new(
            UniqueId.From(creditRow.Id),
            creditRow.Code,
            creditRow.RetailerCode,
            creditRow.CompanyCode,
            new Money(creditRow.CreditLimit, creditRow.CurrencyCode),
            committedExposureMinorUnits,
            [.. orderEntryRows.Select(row => ToEntrySnapshot(row, creditRow.CurrencyCode))]);

    private static CreditLedgerEntrySnapshot ToEntrySnapshot(RowCreditItem row, string currencyCode) =>
        new(
            UniqueId.From(row.Id),
            row.OrderReference,
            new Money(row.Amount, currencyCode),
            CreditEntryTypes.Parse(row.Type),
            new DateTimeOffset(row.CreditDate, TimeSpan.Zero));

    /// <summary>Builds a brand-new <c>credit_items</c> row from an appended <see cref="CreditLedgerEntry"/> — never called for a loaded entry (`B2`).</summary>
    public static RowCreditItem ToNewRow(CreditLedgerEntry entry, Guid creditId, DateTime createdAt) => new()
    {
        Id = entry.Id.Value,
        CreditId = creditId,
        OrderReference = entry.OrderReference.Value,
        Amount = entry.Amount.MinorUnits,
        Type = CreditEntryTypes.ToToken(entry.Type),
        CreditDate = entry.EntryDate.UtcDateTime,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };
}
