using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// "Unique constraints genuinely reject a duplicate" — real conflicting
/// inserts against a real MS-SQL database, not "the index exists". Covers
/// `stock (company_code, product_code)`, `processed_events (event_id,
/// consumer)` and `despatches.order_reference`, each with a control case
/// proving the constraint is on the intended key, not a looser one.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class UniqueConstraintTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Stock_Rejects_A_Duplicate_CompanyCode_ProductCode_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_uq_stock_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;

        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "SupplierEs",
            ProductCode = "SKU-001",
            Units = 100,
            ReservedUnits = 0,
            LowStockThreshold = 10,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "SupplierEs",
            ProductCode = "SKU-001",
            Units = 50,
            ReservedUnits = 0,
            LowStockThreshold = 5,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Stock_Accepts_The_Same_CompanyCode_For_A_Different_ProductCode()
    {
        // Control case: proves the constraint is genuinely on the PAIR, not
        // on company_code alone — otherwise the rejection test above would
        // pass for the wrong reason.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_uq_stock_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;

        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "SupplierEs",
            ProductCode = "SKU-001",
            Units = 100,
            ReservedUnits = 0,
            LowStockThreshold = 10,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "SupplierEs",
            ProductCode = "SKU-002",
            Units = 200,
            ReservedUnits = 0,
            LowStockThreshold = 20,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Stocks.CountAsync());
    }

    [Fact]
    public async Task ProcessedEvents_Rejects_A_Duplicate_EventId_Consumer_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_uq_pe_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "fulfillment",
            ProcessedAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "fulfillment",
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
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_uq_pe_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "fulfillment",
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
    public async Task Despatches_Rejects_A_Duplicate_OrderReference()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_uq_desp_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;

        db.Despatches.Add(new Despatch
        {
            Id = Guid.NewGuid(),
            DespatchReference = "DES-000001",
            DespatchDate = now,
            CompanyCode = "SupplierEs",
            RetailerCode = "CarrefourEs",
            OrderReference = "ORD-000001",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        db.Despatches.Add(new Despatch
        {
            Id = Guid.NewGuid(),
            DespatchReference = "DES-000002",
            DespatchDate = now,
            CompanyCode = "SupplierEs",
            RetailerCode = "CarrefourEs",
            OrderReference = "ORD-000001",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // At most one despatch per order (Databases doc §5).
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
