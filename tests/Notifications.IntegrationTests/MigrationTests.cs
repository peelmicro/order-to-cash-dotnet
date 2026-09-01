using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Feature db_billing, acceptance 1: "both migrations apply and re-apply
/// cleanly" (the `otc_notifications` half) — against a real MS-SQL
/// container (Testcontainers.MsSql), never a mock, and re-applies cleanly
/// after being dropped back to nothing.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class MigrationTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Migration_Applies_Against_An_Empty_Database()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_notifications_migrate_{Guid.NewGuid():N}");

        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.Contains("InitialCreate", StringComparison.Ordinal));

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice()
    {
        var databaseName = $"otc_notifications_reapply_{Guid.NewGuid():N}";
        var connectionString = await fixture.CreateFreshDatabaseAsync(databaseName);

        // First application, from empty.
        await using (var firstRun = fixture.CreateDbContext(connectionString))
        {
            await firstRun.Database.MigrateAsync();
            Assert.True(await firstRun.Database.CanConnectAsync());
        }

        // Wipe the database entirely back to empty — not just its tables —
        // so the second run starts from the exact same state as the first,
        // proving the migration script is not silently depending on an
        // artifact (e.g. a stray __EFMigrationsHistory row) a first run left
        // behind.
        await fixture.DropDatabaseAsync(databaseName);
        await fixture.CreateFreshDatabaseAsync(databaseName);

        await using var secondRun = fixture.CreateDbContext(connectionString);
        await secondRun.Database.MigrateAsync();

        var applied = await secondRun.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.Contains("InitialCreate", StringComparison.Ordinal));

        var pending = await secondRun.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }
}
