using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.Persistence;

// COPY OF — src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchNumberAllocator.cs
// (DES- -> INV-, despatches -> invoices, despatch_reference -> invoice_reference,
// despatch_number_sequences -> invoice_number_sequences). Copied rather than
// re-derived because the ORIGINAL rendering of this idiom (check-then-act,
// `IF NOT EXISTS (SELECT ...) INSERT`) was a real, fixed defect — found for
// `ORD-` in feature 45 and fixed in `EfCoreDespatchNumberAllocator`, whose own
// doc comment already names this as the pattern every later counter table
// must follow rather than re-derive. This is the THIRD instance of the idiom
// (`ORD-`, `DES-`, `INV-`) and it is copied, not re-derived (design.md §6.4,
// `BI29`).
/// <summary>
/// Allocates <c>INV-######</c> under <c>WITH (UPDLOCK, ROWLOCK)</c> on
/// <c>dbo.invoice_number_sequences</c>' single row (<c>id = 1</c>). The seed
/// statement and its own existence test are ONE statement — never
/// check-then-act — so N concurrent allocations against a fresh database
/// with no counter row never duplicate-key and never lose an allocation
/// (`BI29`). The self-initialising <c>MAX(CAST(SUBSTRING(...)))</c> is what
/// makes the first live allocation continue past the seed's highest
/// reference rather than colliding with it (`BI12`).
/// </summary>
public sealed class EfCoreInvoiceNumberAllocator(BillingDbContext db) : IInvoiceNumberAllocator
{
    private const string Prefix = "INV-";
    private const int MinimumSequenceDigits = 6;

    public async Task<string> AllocateNextAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO dbo.invoice_number_sequences (id, next_value)
             SELECT 1, seed.next_value
             FROM (
                 SELECT ISNULL(MAX(CAST(SUBSTRING(invoice_reference, {Prefix.Length + 1}, LEN(invoice_reference) - {Prefix.Length}) AS int)), 0) + 1 AS next_value
                 FROM dbo.invoices
             ) AS seed
             WHERE NOT EXISTS (
                 SELECT 1 FROM dbo.invoice_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
             )
             """,
            cancellationToken).ConfigureAwait(false);

        var sequenceRow = await db.InvoiceNumberSequences
            .FromSqlRaw("SELECT * FROM dbo.invoice_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var allocated = sequenceRow.NextValue;
        sequenceRow.NextValue = allocated + 1;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Prefix + allocated.ToString(new string('0', MinimumSequenceDigits), CultureInfo.InvariantCulture);
    }
}
