using Microsoft.Data.SqlClient;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature outbox_and_idempotency, design.md §9.2 — the fixture change has
/// no meaning without an assertion pinned on it: a database this fixture
/// creates must carry the same isolation configuration the deployed stack
/// does (<c>infra/mssql/init/01-create-databases.sql</c>), or every
/// concurrency test in this repository proves something about a system
/// nobody runs.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class MsSqlContainerFixtureTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Fixture_CreatesDatabasesWithRowVersioningEnabledExactlyAsTheDeployedStackDoes()
    {
        var databaseName = $"otc_orders_rcsi_{Guid.NewGuid():N}";
        var connectionString = await fixture.CreateFreshDatabaseAsync(databaseName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // sys.databases.is_read_committed_snapshot_on, keyed by DB_NAME(),
        // rather than DATABASEPROPERTYEX (design.md §9.2's own wording) —
        // observed to return DBNull here even for a database the current
        // connection is inside, which the catalog view does not.
        command.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();";
        var isReadCommittedSnapshotOn = (bool)(await command.ExecuteScalarAsync())!;

        Assert.True(isReadCommittedSnapshotOn);
    }
}
