using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature db_orders: "the unique constraints genuinely reject a
/// duplicate: insert a conflicting (event_id, consumer) and a conflicting
/// (order_id, command) and assert each fails." Reading EF's model and
/// comparing it to EF's model proves nothing about the database — these
/// tests insert real conflicting rows against a real MS-SQL database and
/// assert the second insert is rejected.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class UniqueConstraintTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task ProcessedEvents_Rejects_A_Duplicate_EventId_Consumer_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_uq_pe_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "notifications",
            ProcessedAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "notifications",
            ProcessedAt = now,
            CreatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ProcessedEvents_Accepts_The_Same_EventId_For_A_Different_Consumer()
    {
        // Control case: proves the constraint is genuinely on the PAIR, not
        // on event_id alone — otherwise the rejection test above would pass
        // for the wrong reason.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_uq_pe_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = "notifications",
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
    public async Task SagaCommands_Rejects_A_Duplicate_OrderId_Command_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_uq_sc_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.SagaCommands.Add(new SagaCommand
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OrderReference = "ORD-000001",
            Command = "stock.reserve",
            Payload = "{}",
            TriggeringEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        db.SagaCommands.Add(new SagaCommand
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OrderReference = "ORD-000001",
            Command = "stock.reserve",
            Payload = "{}",
            TriggeringEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SagaCommands_Accepts_The_Same_OrderId_For_A_Different_Command()
    {
        // Control case: proves the constraint is on the PAIR, not on
        // order_id alone.
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_uq_sc_ctrl_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.SagaCommands.Add(new SagaCommand
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OrderReference = "ORD-000001",
            Command = "stock.reserve",
            Payload = "{}",
            TriggeringEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.SagaCommands.Add(new SagaCommand
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OrderReference = "ORD-000001",
            Command = "credit.hold",
            Payload = "{}",
            TriggeringEventId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.SagaCommands.CountAsync());
    }
}
