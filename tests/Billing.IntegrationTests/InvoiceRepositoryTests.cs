using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BI7` — the two-aggregate transaction, honestly (design.md §7.4). `BI10` store half. `BI24` — the `paid_at` instant round trip under a non-UTC host time zone.</summary>
[Collection(MsSqlCollection.Name)]
public sealed class InvoiceRepositoryTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task BI7_CommitsTheInvoiceItsLinesTheConsumeLedgerEntryAndExactlyOneOutboxRow_AndLeavesNoneOfThemBehindWhenTheTransactionRollsBack()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_repo_bi7_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var orderReference = OrderNumber.Parse("ORD-000601");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference.Value, 6_000, "hold");

        var outboxCountBefore = 0;

        // Happy path — both aggregates, one transaction.
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            outboxCountBefore = await db.OutboxMessages.CountAsync();

            var invoiceRepo = new EfCoreInvoiceRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var creditRepo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            await uow.ExecuteAsync(async ct =>
            {
                var credit = await creditRepo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, ct);
                var invoice = Invoice.Issue(
                    new IssueInvoiceInput(UniqueId.New(), "INV-810101", orderReference, "CarrefourEs", "IBERFOODS",
                        [new InvoiceLineInput("SKU-1", new Quantity(6), new Money(1_000, "EUR"))], Money.Zero("EUR"), UniqueId.New()),
                    new InvoiceContext(DateTimeOffset.UtcNow, UniqueId.New()),
                    UniqueId.New);

                credit!.Consume(orderReference, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);

                await invoiceRepo.SaveAsync(invoice, ct);
                await creditRepo.SaveChangesAsync(credit, ct);
            }, CancellationToken.None);
        }

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            Assert.Single(await assertDb.Invoices.Where(i => i.OrderReference == orderReference.Value).ToListAsync());
            var invoiceRow = await assertDb.Invoices.SingleAsync(i => i.OrderReference == orderReference.Value);
            Assert.Single(await assertDb.InvoiceItems.Where(l => l.InvoiceId == invoiceRow.Id).ToListAsync());
            var consumeRows = await assertDb.CreditItems.Where(c => c.OrderReference == orderReference.Value && c.Type == "consume").ToListAsync();
            var consumeRow = Assert.Single(consumeRows);
            Assert.Equal(6_000, consumeRow.Amount);
            Assert.Equal(outboxCountBefore + 1, await assertDb.OutboxMessages.CountAsync());
        }

        // Forced rollback — a second order, second transaction.
        var secondOrderReference = OrderNumber.Parse("ORD-000602");
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, secondOrderReference.Value, 2_000, "hold");
        var creditUpdatedAtBefore = (await BillingHostFixture.FindCreditAsync(mssql, connectionString, "CarrefourEs", "IBERFOODS"))!.UpdatedAt;
        var outboxCountBeforeRollback = 0;

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            outboxCountBeforeRollback = await db.OutboxMessages.CountAsync();
            var invoiceRepo = new EfCoreInvoiceRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var creditRepo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(async ct =>
            {
                var credit = await creditRepo.LockForOrderAsync("CarrefourEs", "IBERFOODS", secondOrderReference, ct);
                var invoice = Invoice.Issue(
                    new IssueInvoiceInput(UniqueId.New(), "INV-810102", secondOrderReference, "CarrefourEs", "IBERFOODS",
                        [new InvoiceLineInput("SKU-1", new Quantity(2), new Money(1_000, "EUR"))], Money.Zero("EUR"), UniqueId.New()),
                    new InvoiceContext(DateTimeOffset.UtcNow, UniqueId.New()),
                    UniqueId.New);

                credit!.Consume(secondOrderReference, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);

                await invoiceRepo.SaveAsync(invoice, ct);
                await creditRepo.SaveChangesAsync(credit, ct);

                throw new InvalidOperationException("simulated post-save failure — must roll back everything.");
            }, CancellationToken.None));
        }

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            Assert.Empty(await assertDb.Invoices.Where(i => i.OrderReference == secondOrderReference.Value).ToListAsync());
            Assert.Empty(await assertDb.CreditItems.Where(c => c.OrderReference == secondOrderReference.Value && c.Type == "consume").ToListAsync());
            Assert.Equal(outboxCountBeforeRollback, await assertDb.OutboxMessages.CountAsync());

            var creditAfter = await BillingHostFixture.FindCreditAsync(mssql, connectionString, "CarrefourEs", "IBERFOODS");
            Assert.Equal(creditUpdatedAtBefore, creditAfter!.UpdatedAt);
        }
    }

    /// <summary>`BI10` store half — a hand-crafted row with `status='paid'` and `paid_at NULL` reads back as `InvalidInvoiceSnapshotError`.</summary>
    [Fact]
    public async Task BI10_RefusesToReadBackAHandWrittenRowWhoseStatusAndPaidAtDisagree()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_repo_bi10_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810103", "ORD-000603", "CarrefourEs", "IBERFOODS", 1_000, 0, 1_000, "paid", paidAt: null);

        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreInvoiceRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());

        var snapshot = await repo.FindByOrderReferenceAsync(OrderNumber.Parse("ORD-000603"), CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Throws<InvalidInvoiceSnapshotError>(() => Invoice.Reconstitute(snapshot!));
    }

    /// <summary>`BI24` — an instant round-trips unchanged, independently of the HOST's local time zone, for both a non-null and a NULL `paid_at`.</summary>
    [Fact]
    public async Task BI24_RoundTripsInvoiceDateAndANonNullPaidAtUnchangedUnderANonUtcHostTimeZone_AndDistinguishesANullPaidAt()
    {
        var originalTz = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", "America/New_York");
            TimeZoneInfo.ClearCachedData();
            Assert.NotEqual(TimeSpan.Zero, TimeZoneInfo.Local.BaseUtcOffset);

            var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_repo_bi24_{Guid.NewGuid():N}");
            await using (var migrate = mssql.CreateDbContext(connectionString))
            {
                await migrate.Database.MigrateAsync();
            }

            var invoiceDate = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);
            var paidAt = new DateTimeOffset(2026, 6, 20, 14, 0, 0, TimeSpan.Zero);

            await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810104", "ORD-000604", "CarrefourEs", "IBERFOODS", 1_000, 0, 1_000, "paid", paidAt.UtcDateTime, invoiceDate: invoiceDate.UtcDateTime);
            await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810105", "ORD-000605", "CarrefourEs", "IBERFOODS", 2_000, 0, 2_000, "issued", null, invoiceDate: invoiceDate.UtcDateTime);

            await using var db = mssql.CreateDbContext(connectionString);
            var repo = new EfCoreInvoiceRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());

            var paidSnapshot = await repo.FindByOrderReferenceAsync(OrderNumber.Parse("ORD-000604"), CancellationToken.None);
            Assert.Equal(invoiceDate, paidSnapshot!.InvoiceDate);
            Assert.Equal(paidAt, paidSnapshot.State.PaidAtOrNull);

            var issuedSnapshot = await repo.FindByOrderReferenceAsync(OrderNumber.Parse("ORD-000605"), CancellationToken.None);
            Assert.Equal(invoiceDate, issuedSnapshot!.InvoiceDate);
            Assert.Null(issuedSnapshot.State.PaidAtOrNull);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalTz);
            TimeZoneInfo.ClearCachedData();
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
