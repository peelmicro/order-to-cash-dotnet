using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BI15` SQL half — every filter, paging and `Page.Total`, `issuedBeforeMinutes` against a SUPPLIED `now`, `paidAt` present/absent, and a re-read afterwards proving no row changed.</summary>
[Collection(MsSqlCollection.Name)]
public sealed class InvoiceReadRepositoryTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task BI15_TranslatesEveryFilterAndPagesTheResult_ReadingTheCurrentTimeFromTheSuppliedNowRatherThanTheAmbientClock()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_read_bi15_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var fixedNow = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var old = fixedNow.AddDays(-2).UtcDateTime;
        var recent = fixedNow.AddMinutes(-5).UtcDateTime;

        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810001", "ORD-000501", "RetailerRead", "CompanyOld", 1_000, 0, 1_000, "issued", null, invoiceDate: old);
        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810002", "ORD-000502", "RetailerRead", "CompanyRecent", 2_000, 0, 2_000, "issued", null, invoiceDate: recent);
        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810003", "ORD-000503", "RetailerRead", "CompanyPaid", 3_000, 500, 2_500, "paid", fixedNow.UtcDateTime, invoiceDate: old);

        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreInvoiceReadRepository(db);

        // now is SUPPLIED and fixed — a real ambient clock would not
        // reproduce this deterministically.
        var olderThanOneDay = await repo.ListAsync(new InvoiceListRequestPayload(1, 25, RetailerCode: "RetailerRead", IssuedBeforeMinutes: 60 * 24), fixedNow, CancellationToken.None);
        Assert.Equal(2, olderThanOneDay.Page.Total);
        Assert.DoesNotContain(olderThanOneDay.Items, i => i.CompanyCode == "CompanyRecent");

        var byStatus = await repo.ListAsync(new InvoiceListRequestPayload(1, 25, Status: "paid", RetailerCode: "RetailerRead"), fixedNow, CancellationToken.None);
        var paidView = Assert.Single(byStatus.Items);
        Assert.NotNull(paidView.PaidAt);
        Assert.Equal(2_500, paidView.TotalAmount);
        Assert.Equal(500, paidView.Discount);

        var issuedView = (await repo.ListAsync(new InvoiceListRequestPayload(1, 25, Status: "issued", RetailerCode: "RetailerRead"), fixedNow, CancellationToken.None)).Items;
        Assert.All(issuedView, i => Assert.Null(i.PaidAt));

        var byOrderReference = await repo.ListAsync(new InvoiceListRequestPayload(1, 25, OrderReference: "ORD-000502"), fixedNow, CancellationToken.None);
        Assert.Equal(1, byOrderReference.Page.Total);

        var page1 = await repo.ListAsync(new InvoiceListRequestPayload(1, 1, RetailerCode: "RetailerRead"), fixedNow, CancellationToken.None);
        Assert.Equal(3, page1.Page.Total);
        Assert.Single(page1.Items);

        // No lock, no mutation — a re-read afterwards proves nothing changed.
        await using var assertDb = mssql.CreateDbContext(connectionString);
        var rowAfter = await assertDb.Invoices.AsNoTracking().SingleAsync(i => i.OrderReference == "ORD-000501");
        Assert.Equal("issued", rowAfter.Status);
    }

    /// <summary>ARM: make the adapter read <see cref="DateTimeOffset.UtcNow"/> instead of the supplied `now` and confirm this fails.</summary>
    [Fact]
    public async Task IssuedBeforeMinutes_UsesTheSuppliedNowRatherThanAnAmbientClock_SoAFarFutureNowIncludesEverything()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_invoice_read_supplied_now_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedInvoiceAsync(mssql, connectionString, "INV-810010", "ORD-000510", "RetailerReadNow", "CompanyA", 1_000, 0, 1_000, "issued", null, invoiceDate: DateTime.UtcNow);

        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreInvoiceReadRepository(db);

        // A "now" far in the FUTURE makes issuedBeforeMinutes:5 INCLUDE a
        // row written moments ago in real wall-clock time — provable only
        // because the adapter reads the SUPPLIED now, never
        // DateTimeOffset.UtcNow directly. (A far-PAST supplied now cannot
        // distinguish the two: a row inserted moments ago is excluded by
        // both the supplied and the real clock alike.)
        var farFutureNow = new DateTimeOffset(3000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await repo.ListAsync(new InvoiceListRequestPayload(1, 25, RetailerCode: "RetailerReadNow", IssuedBeforeMinutes: 5), farFutureNow, CancellationToken.None);

        Assert.Equal(1, result.Page.Total);
    }
}
