using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application.Ports;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// Allocates <c>DES-######</c> under <c>WITH (UPDLOCK, ROWLOCK)</c> on
/// <c>dbo.despatch_number_sequences</c>' single row (<c>id = 1</c>) — the
/// sibling of <c>src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator</c>,
/// substituting table/column names only. Copied rather than reinvented
/// because the ORIGINAL rendering of this idiom (check-then-act,
/// <c>IF NOT EXISTS (SELECT ...) INSERT</c>) was a real, fixed defect
/// (feature 45 / feature db_orders review D2) — the self-initialising
/// <c>INSERT ... SELECT ... WHERE NOT EXISTS (... WITH (UPDLOCK, HOLDLOCK) ...)</c>
/// is the FIXED idiom, and <c>DespatchNumberSequence.NextValue</c>'s own doc
/// comment already names this table as one that must follow the fixed
/// pattern, not the original one (ledger L5's sibling — see
/// <c>DespatchCreationService</c>'s remark on why no upsert is needed for
/// the despatch rows themselves; the counter table is a DIFFERENT idiom that
/// genuinely does need this atomic seed).
/// </summary>
public sealed class EfCoreDespatchNumberAllocator(FulfillmentDbContext db) : IDespatchNumberAllocator
{
    private const string Prefix = "DES-";
    private const int MinimumSequenceDigits = 6;

    public async Task<string> AllocateNextAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO dbo.despatch_number_sequences (id, next_value)
             SELECT 1, seed.next_value
             FROM (
                 SELECT ISNULL(MAX(CAST(SUBSTRING(despatch_reference, {Prefix.Length + 1}, LEN(despatch_reference) - {Prefix.Length}) AS int)), 0) + 1 AS next_value
                 FROM dbo.despatches
             ) AS seed
             WHERE NOT EXISTS (
                 SELECT 1 FROM dbo.despatch_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
             )
             """,
            cancellationToken).ConfigureAwait(false);

        var sequenceRow = await db.DespatchNumberSequences
            .FromSqlRaw("SELECT * FROM dbo.despatch_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var allocated = sequenceRow.NextValue;
        sequenceRow.NextValue = allocated + 1;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Prefix + allocated.ToString(new string('0', MinimumSequenceDigits), CultureInfo.InvariantCulture);
    }
}
