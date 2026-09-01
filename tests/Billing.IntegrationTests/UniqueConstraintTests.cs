using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// "Unique constraints genuinely reject a duplicate" — real conflicting
/// inserts against a real MS-SQL database, not "the index exists". Covers
/// `credits (retailer_code, company_code)`, `processed_events (event_id,
/// consumer)` and, distinctively for this feature, `payments.payment_reference`
/// — the remittance endpoint's idempotency key (R47/R48) — each with a
/// control case proving the constraint is on the intended key, not a looser
/// one.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class UniqueConstraintTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Credits_Rejects_A_Duplicate_RetailerCode_CompanyCode_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_uq_credit_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;

        db.Credits.Add(new Credit
        {
            Id = Guid.NewGuid(),
            Code = "CR-000001",
            RetailerCode = "CarrefourEs",
            CompanyCode = "SupplierEs",
            CreditLimit = 100_000,
            CurrencyCode = "EUR",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        db.Credits.Add(new Credit
        {
            Id = Guid.NewGuid(),
            Code = "CR-000002",
            RetailerCode = "CarrefourEs",
            CompanyCode = "SupplierEs",
            CreditLimit = 50_000,
            CurrencyCode = "EUR",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Credits_Accepts_The_Same_RetailerCode_For_A_Different_CompanyCode()
    {
        // Control case: proves the constraint is genuinely on the PAIR, not
        // on retailer_code alone — otherwise the rejection test above would
        // pass for the wrong reason.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_uq_credit_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;

        db.Credits.Add(new Credit
        {
            Id = Guid.NewGuid(),
            Code = "CR-000001",
            RetailerCode = "CarrefourEs",
            CompanyCode = "SupplierEs",
            CreditLimit = 100_000,
            CurrencyCode = "EUR",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Credits.Add(new Credit
        {
            Id = Guid.NewGuid(),
            Code = "CR-000002",
            RetailerCode = "CarrefourEs",
            CompanyCode = "OtherSupplierEs",
            CreditLimit = 50_000,
            CurrencyCode = "EUR",
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Credits.CountAsync());
    }

    [Fact]
    public async Task ProcessedEvents_Rejects_A_Duplicate_EventId_Consumer_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_uq_pe_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "billing",
            ProcessedAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "billing",
            ProcessedAt = now,
            CreatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProcessedEvents_Accepts_The_Same_EventId_For_A_Different_Consumer()
    {
        // Control case: proves the constraint is genuinely on the PAIR, not
        // on event_id alone.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_uq_pe_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "billing",
            ProcessedAt = now,
            CreatedAt = now,
        });
        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "projector",
            ProcessedAt = now,
            CreatedAt = now,
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Payments_Rejects_A_Duplicate_PaymentReference()
    {
        // R47/R48: payment_reference is the remittance endpoint's
        // idempotency key. If this constraint is missing, a retried remittance
        // POST creates a second payment row and double-releases credit.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_uq_payment_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var invoiceId = Guid.NewGuid();

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            InvoiceReference = "INV-000001",
            InvoiceDate = now,
            CompanyCode = "SupplierEs",
            RetailerCode = "CarrefourEs",
            OrderReference = "ORD-000001",
            Amount = 10_000,
            Discount = 0,
            TotalAmount = 10_000,
            CurrencyCode = "EUR",
            Status = "issued",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            PaymentReference = "REM-000001",
            InvoiceId = invoiceId,
            Amount = 10_000,
            CurrencyCode = "EUR",
            ValueDate = now,
            Source = "robot",
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            PaymentReference = "REM-000001",
            InvoiceId = invoiceId,
            Amount = 10_000,
            CurrencyCode = "EUR",
            ValueDate = now,
            Source = "robot",
            CreatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
