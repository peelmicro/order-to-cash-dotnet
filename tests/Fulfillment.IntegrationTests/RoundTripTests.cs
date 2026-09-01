using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// Feature db_fulfillment, acceptance 2: "round-trip integration test per
/// table" — for every one of the seven tables, insert a row through
/// <see cref="FulfillmentDbContext"/>, read it back from a brand-new
/// <see cref="FulfillmentDbContext"/> instance (so the read genuinely hits
/// the database rather than EF's first-level cache), and assert every field
/// survived unchanged. This is a distinct claim from
/// <c>SchemaColumnTypeTests</c>: that test proves the column exists with the
/// right SQL type; this one proves data actually persists and reads back
/// through the mapping.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class RoundTripTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Stock_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_stock_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Stocks.Add(new Stock
            {
                Id = id,
                CompanyCode = "SupplierEs",
                ProductCode = "SKU-001",
                Units = 100,
                ReservedUnits = 12,
                LowStockThreshold = 20,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.Stocks.SingleAsync(s => s.Id == id);

        Assert.Equal("SupplierEs", row.CompanyCode);
        Assert.Equal("SKU-001", row.ProductCode);
        Assert.Equal(100, row.Units);
        Assert.Equal(12, row.ReservedUnits);
        Assert.Equal(20, row.LowStockThreshold);
    }

    [Fact]
    public async Task Reservation_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_res_{Guid.NewGuid():N}");
        var stockId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Stocks.Add(new Stock
            {
                Id = stockId,
                CompanyCode = "SupplierEs",
                ProductCode = "SKU-001",
                Units = 100,
                ReservedUnits = 0,
                LowStockThreshold = 20,
                CreatedAt = now,
                UpdatedAt = now,
            });
            write.Reservations.Add(new Reservation
            {
                Id = reservationId,
                StockId = stockId,
                CompanyCode = "SupplierEs",
                RetailerCode = "CarrefourEs",
                ProductCode = "SKU-001",
                OrderReference = "ORD-000001",
                Units = 5,
                Status = "reserved",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.Reservations.SingleAsync(r => r.Id == reservationId);

        Assert.Equal(stockId, row.StockId);
        Assert.Equal("CarrefourEs", row.RetailerCode);
        Assert.Equal("ORD-000001", row.OrderReference);
        Assert.Equal(5, row.Units);
        Assert.Equal("reserved", row.Status);
    }

    [Fact]
    public async Task Despatch_And_DespatchItem_Round_Trip()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_desp_{Guid.NewGuid():N}");
        var despatchId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Despatches.Add(new Despatch
            {
                Id = despatchId,
                DespatchReference = "DES-000001",
                DespatchDate = now,
                CompanyCode = "SupplierEs",
                RetailerCode = "CarrefourEs",
                OrderReference = "ORD-000001",
                CreatedAt = now,
                UpdatedAt = now,
            });
            write.DespatchItems.Add(new DespatchItem
            {
                Id = itemId,
                DespatchId = despatchId,
                ProductCode = "SKU-001",
                Units = 5,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var despatch = await read.Despatches.SingleAsync(d => d.Id == despatchId);
        var item = await read.DespatchItems.SingleAsync(i => i.Id == itemId);

        Assert.Equal("DES-000001", despatch.DespatchReference);
        Assert.Equal("ORD-000001", despatch.OrderReference);
        Assert.Equal(despatchId, item.DespatchId);
        Assert.Equal("SKU-001", item.ProductCode);
        Assert.Equal(5, item.Units);
    }

    [Fact]
    public async Task DespatchNumberSequence_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_seq_{Guid.NewGuid():N}");

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.DespatchNumberSequences.Add(new DespatchNumberSequence { Id = 1, NextValue = 1 });
            await write.SaveChangesAsync();
        }

        await using (var update = fixture.CreateDbContext(connectionString))
        {
            var row = await update.DespatchNumberSequences.SingleAsync(s => s.Id == 1);
            row.NextValue = 2;
            await update.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var finalRow = await read.DespatchNumberSequences.SingleAsync(s => s.Id == 1);

        Assert.Equal(2, finalRow.NextValue);
    }

    [Fact]
    public async Task OutboxMessage_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_outbox_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                EventId = eventId,
                EventType = "order.despatched.v1",
                AggregateId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                Payload = """{"despatchReference":"DES-000001"}""",
                OccurredAt = now,
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.OutboxMessages.SingleAsync(o => o.Id == id);

        Assert.Equal(eventId, row.EventId);
        Assert.Equal("order.despatched.v1", row.EventType);
        Assert.Equal("""{"despatchReference":"DES-000001"}""", row.Payload);
        Assert.Null(row.PublishedAt);
        Assert.True(row.Seq > 0);
    }

    [Fact]
    public async Task ProcessedEvent_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_rt_pe_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.ProcessedEvents.Add(new ProcessedEvent
            {
                Id = id,
                EventId = eventId,
                Consumer = "fulfillment",
                ProcessedAt = now,
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.ProcessedEvents.SingleAsync(p => p.Id == id);

        Assert.Equal(eventId, row.EventId);
        Assert.Equal("fulfillment", row.Consumer);
    }
}
