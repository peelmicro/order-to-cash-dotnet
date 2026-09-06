using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// `BI12` — never yields the same reference to concurrent allocations, and
/// continues past the highest seeded reference. `BI29` — the counter row is
/// seeded ATOMICALLY under concurrency from a fresh database, never
/// check-then-act.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class InvoiceNumberAllocatorTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task BI12_NeverYieldsTheSameReferenceToConcurrentAllocations_AndContinuesPastTheHighestSeededReference()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_allocator_bi12_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await SeedInvoiceRowWithReferenceAsync(connectionString, "INV-000005");

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var allocator = new EfCoreInvoiceNumberAllocator(db);
            var first = await allocator.AllocateNextAsync(CancellationToken.None);
            Assert.Equal("INV-000006", first);
        }

        const int concurrency = 16;
        using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dbs = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            var db = mssql.CreateDbContext(connectionString);
            await db.Database.OpenConnectionAsync(openTimeout.Token);
            return db;
        }));

        try
        {
            var tasks = dbs.Select(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                var allocator = new EfCoreInvoiceNumberAllocator(db);
                var allocated = await allocator.AllocateNextAsync(CancellationToken.None);
                await transaction.CommitAsync();
                return allocated;
            });

            var results = await Task.WhenAll(tasks);
            var sequenceNumbers = results.Select(r => int.Parse(r["INV-".Length..])).OrderBy(n => n).ToList();

            Assert.Equal(concurrency, sequenceNumbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(7, concurrency), sequenceNumbers); // continues right after INV-000006.
        }
        finally
        {
            foreach (var db in dbs)
            {
                await db.DisposeAsync();
            }
        }
    }

    /// <summary>`BI29` — N concurrent allocations against a database with NO `invoice_number_sequences` row: N distinct references, no duplicate-key failure, one counter row.</summary>
    [Fact]
    public async Task BI29_SeedsTheCounterRowAtomicallyUnderConcurrencyFromAFreshDatabase_WithNoDuplicateKeyFailureAndNoLostAllocation()
    {
        const int concurrency = 16;
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_allocator_bi29_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using (var assertNoRow = mssql.CreateDbContext(connectionString))
        {
            Assert.Empty(await assertNoRow.InvoiceNumberSequences.ToListAsync());
        }

        using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dbs = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            var db = mssql.CreateDbContext(connectionString);
            await db.Database.OpenConnectionAsync(openTimeout.Token);
            return db;
        }));

        try
        {
            var tasks = dbs.Select(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                var allocator = new EfCoreInvoiceNumberAllocator(db);
                var allocated = await allocator.AllocateNextAsync(CancellationToken.None);
                await transaction.CommitAsync();
                return allocated;
            });

            var results = await Task.WhenAll(tasks);
            var sequenceNumbers = results.Select(r => int.Parse(r["INV-".Length..])).OrderBy(n => n).ToList();

            Assert.Equal(concurrency, sequenceNumbers.Distinct().Count());
            Assert.Equal(Enumerable.Range(1, concurrency), sequenceNumbers);
        }
        finally
        {
            foreach (var db in dbs)
            {
                await db.DisposeAsync();
            }
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Single(await assertDb.InvoiceNumberSequences.ToListAsync());
    }

    private async Task SeedInvoiceRowWithReferenceAsync(string connectionString, string invoiceReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceReference = invoiceReference,
            InvoiceDate = now,
            CompanyCode = "IBERFOODS",
            RetailerCode = "CarrefourEs",
            OrderReference = $"ORD-SEED-{invoiceReference}",
            Amount = 1_000,
            Discount = 0,
            TotalAmount = 1_000,
            CurrencyCode = "EUR",
            Status = "issued",
            PaidAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
