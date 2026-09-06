using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Round-trip reconstitute/save over the REAL repository and REAL MS-SQL,
/// no NATS. `BC10` transaction half, `BC24`'s instant round trip under a
/// non-UTC host time zone, and §3.1's honest check: `availableCredit`
/// recomputed from every row of `credit_items` equals the fact's
/// `availableCreditAfter` after each committed operation.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class BuyerCreditRepositoryTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task RoundTrips_ReconstituteThenSave_ThroughTheRealRepository()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_repo_roundtrip_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var orderReference = OrderNumber.Parse("ORD-000001");

        await RunAsync(connectionString, async (repo, service) =>
        {
            var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, CancellationToken.None);
            Assert.NotNull(credit);
            Assert.Equal(500_000, credit!.AvailableCredit.MinorUnits);

            var request = new HoldRequest(orderReference, new Money(1_000, "EUR"), UniqueId.New());
            Assert.IsType<HoldEvaluation.Fits>(credit.EvaluateHold(request));
            credit.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);

            await repo.SaveChangesAsync(credit, CancellationToken.None);
        });

        var reloaded = await LockAsync(connectionString, "CarrefourEs", "IBERFOODS", orderReference);
        Assert.NotNull(reloaded);
        Assert.Equal(499_000, reloaded!.AvailableCredit.MinorUnits);
        Assert.IsType<HoldEvaluation.AlreadyHeld>(reloaded.EvaluateHold(new HoldRequest(orderReference, new Money(1_000, "EUR"), UniqueId.New())));

        var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, orderReference.Value);
        var entry = Assert.Single(ledger);
        Assert.Equal("hold", entry.Type);
        Assert.Equal(1_000, entry.Amount);
        Assert.Equal(creditId, entry.CreditId);
    }

    /// <summary>`BC10` transaction half — the hold entry and the `credit.approved.v1` outbox record are written IN ONE TRANSACTION; a forced rollback leaves NEITHER.</summary>
    [Fact]
    public async Task BC10_WritesTheHoldEntryAndTheCreditApprovedOutboxRecordInOneTransaction_AndAForcedRollbackLeavesNeither()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_repo_bc10_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var orderReference = OrderNumber.Parse("ORD-000002");

        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new CreditFactPayloadMapper()), new FixedClock());
        var uow = new EfCoreUnitOfWork(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(
            async ct =>
            {
                var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, ct);
                var request = new HoldRequest(orderReference, new Money(2_000, "EUR"), UniqueId.New());
                credit!.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                await repo.SaveChangesAsync(credit, ct);
                throw new InvalidOperationException("simulated post-save failure — must roll back everything.");
            },
            CancellationToken.None));

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(0, await assertDb.CreditItems.CountAsync());
        Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
    }

    /// <summary>
    /// `BC24`, ledger `L11` — an instant round-trips unchanged, independently
    /// of the HOST's local time zone. <c>datetime2(3)</c> carries no offset
    /// and EF Core returns <see cref="DateTimeKind.Unspecified"/>; without
    /// the explicit <c>TimeSpan.Zero</c> conversion,
    /// <c>new DateTimeOffset(unspecified)</c> would apply the MACHINE's
    /// local offset.
    /// </summary>
    [Fact]
    public async Task BC24_RoundTripsALedgerEntrysInstantUnchanged_UnderANonUtcHostTimeZone()
    {
        var originalTz = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", "America/New_York");
            TimeZoneInfo.ClearCachedData();
            Assert.NotEqual(TimeSpan.Zero, TimeZoneInfo.Local.BaseUtcOffset);

            var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_repo_bc24_{Guid.NewGuid():N}");
            await using (var migrate = mssql.CreateDbContext(connectionString))
            {
                await migrate.Database.MigrateAsync();
            }

            await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
            var orderReference = OrderNumber.Parse("ORD-000003");
            var knownInstant = new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(knownInstant), new CreditFactPayloadMapper()), new FixedClock(knownInstant));
                var uow = new EfCoreUnitOfWork(db);

                await uow.ExecuteAsync(async ct =>
                {
                    var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, ct);
                    var request = new HoldRequest(orderReference, new Money(1_500, "EUR"), UniqueId.New());
                    credit!.Approve(request, new CreditContext(knownInstant, UniqueId.New()), UniqueId.New);
                    await repo.SaveChangesAsync(credit, ct);
                }, CancellationToken.None);
            }

            var reloaded = await LockAsync(connectionString, "CarrefourEs", "IBERFOODS", orderReference);
            var reloadedEntry = reloaded!.Summary.ByOrder.Single();
            Assert.Equal(1_500, reloadedEntry.Exposure);

            // The property BC24 exists to guard is the PRODUCTION read
            // path's own conversion: LockForOrderAsync ->
            // BuyerCreditRowMapper.ToDomain -> CreditLedgerEntry.EntryDate.
            // Going through ToSnapshot() here reads back exactly the
            // instant CreditLedgerEntry.Reconstitute was built with from
            // the mapper's snapshot — not a re-implementation of the
            // conversion in the test.
            var mappedEntry = Assert.Single(reloaded.ToSnapshot().Entries);
            Assert.Equal(knownInstant, mappedEntry.EntryDate);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalTz);
            TimeZoneInfo.ClearCachedData();
        }
    }

    /// <summary>§3.1's honest check — availableCredit recomputed from EVERY row of credit_items equals the fact's availableCreditAfter, after each committed operation (approve, then release).</summary>
    [Fact]
    public async Task AvailableCreditRecomputedFromEveryLedgerRow_EqualsTheFactsAvailableCreditAfter_AfterEachCommittedOperation()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_repo_honest_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        var orderReference = OrderNumber.Parse("ORD-000004");

        long approvedAvailableCreditAfter = 0;
        await RunAsync(connectionString, async (repo, service) =>
        {
            var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, CancellationToken.None);
            var request = new HoldRequest(orderReference, new Money(4_000, "EUR"), UniqueId.New());
            credit!.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
            var fact = (CreditApproved)credit.DomainEvents.Single();
            approvedAvailableCreditAfter = fact.AvailableCreditAfter.MinorUnits;
            await repo.SaveChangesAsync(credit, CancellationToken.None);
        });

        await AssertRecomputedAvailableCreditEqualsAsync(connectionString, creditId, approvedAvailableCreditAfter);

        long releasedAvailableCreditAfter = 0;
        await RunAsync(connectionString, async (repo, service) =>
        {
            var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, CancellationToken.None);
            var released = credit!.Release(orderReference, CreditReleaseReason.OrderCancelled, UniqueId.New(), new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
            Assert.NotNull(released);
            var fact = (CreditReleased)credit.DomainEvents.Single();
            releasedAvailableCreditAfter = fact.AvailableCreditAfter.MinorUnits;
            await repo.SaveChangesAsync(credit, CancellationToken.None);
        });

        await AssertRecomputedAvailableCreditEqualsAsync(connectionString, creditId, releasedAvailableCreditAfter);
    }

    private async Task AssertRecomputedAvailableCreditEqualsAsync(string connectionString, Guid creditId, long expectedAvailableCredit)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var creditRow = await db.Credits.AsNoTracking().SingleAsync(c => c.Id == creditId);
        var committedExposure = await BillingHostFixture.CommittedExposureOfAsync(mssql, connectionString, creditId);
        Assert.Equal(expectedAvailableCredit, creditRow.CreditLimit - committedExposure);
    }

    private async Task<BuyerCredit?> LockAsync(string connectionString, string retailerCode, string companyCode, OrderNumber orderReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new CreditFactPayloadMapper()), new FixedClock());
        return await repo.LockForOrderAsync(retailerCode, companyCode, orderReference, CancellationToken.None);
    }

    private async Task RunAsync(string connectionString, Func<EfCoreBuyerCreditRepository, EfCoreUnitOfWork, Task> work)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new CreditFactPayloadMapper()), new FixedClock());
        var uow = new EfCoreUnitOfWork(db);

        await uow.ExecuteAsync(async ct => await work(repo, uow), CancellationToken.None);
    }

    private sealed class FixedClock(DateTimeOffset? at = null) : IClock
    {
        public DateTimeOffset UtcNow => at ?? new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
