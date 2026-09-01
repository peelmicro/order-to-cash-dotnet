using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// "`outbox.seq` really is an IDENTITY and really increments" — both the
/// column property and the runtime behaviour, on the real database. Mirrors
/// feature db_fulfillment's `OutboxSeqIdentityTests`.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class OutboxSeqIdentityTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Outbox_Seq_Is_An_Identity_Column()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_seqid_{Guid.NewGuid():N}");
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMNPROPERTY(OBJECT_ID('dbo.outbox'), 'seq', 'IsIdentity');";
        var isIdentity = (int)(await command.ExecuteScalarAsync())!;

        Assert.Equal(1, isIdentity);
    }

    [Fact]
    public async Task Outbox_Seq_Really_Increments_Across_Inserted_Rows()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_seqinc_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var first = NewOutboxMessage(now);
        var second = NewOutboxMessage(now);

        db.OutboxMessages.Add(first);
        await db.SaveChangesAsync();

        db.OutboxMessages.Add(second);
        await db.SaveChangesAsync();

        Assert.True(first.Seq > 0, "the first inserted row's seq must be assigned by the database, not left at 0");
        Assert.True(second.Seq > first.Seq, "seq must strictly increase across inserts");
    }

    private static OutboxMessage NewOutboxMessage(DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        EventType = "invoice.issued.v1",
        AggregateId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        CausationId = Guid.NewGuid(),
        Payload = "{}",
        OccurredAt = now,
        CreatedAt = now,
    };
}
