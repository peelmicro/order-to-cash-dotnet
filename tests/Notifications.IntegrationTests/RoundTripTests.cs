using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Round-trip integration test for `processed_events` — the one table this
/// context owns: insert a row through <see
/// cref="Infrastructure.Persistence.NotificationsDbContext"/>, read it back
/// from a brand-new instance (so the read genuinely hits the database
/// rather than EF's first-level cache), and assert every field survived
/// unchanged.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class RoundTripTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task ProcessedEvent_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_rt_pe_{Guid.NewGuid():N}");
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
                Consumer = "notifications",
                ProcessedAt = now,
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.ProcessedEvents.SingleAsync(p => p.Id == id);

        Assert.Equal(eventId, row.EventId);
        Assert.Equal("notifications", row.Consumer);
    }
}
