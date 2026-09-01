using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// "Unique constraints genuinely reject a duplicate" — real conflicting
/// inserts against a real MS-SQL database, not "the index exists". This is
/// the guarantee the whole database exists for: a duplicate `(event_id,
/// consumer)` pair must be rejected, or a duplicate email send becomes
/// possible under a Kafka redelivery or a consumer-group replay (Databases
/// doc §7).
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class UniqueConstraintTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task ProcessedEvents_Rejects_A_Duplicate_EventId_Consumer_Pair()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_uq_pe_{Guid.NewGuid():N}");
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
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_uq_pe_ctrl_{Guid.NewGuid():N}");
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
            Consumer = "billing-mirror",
            ProcessedAt = now,
            CreatedAt = now,
        });

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.ProcessedEvents.CountAsync());
    }
}
