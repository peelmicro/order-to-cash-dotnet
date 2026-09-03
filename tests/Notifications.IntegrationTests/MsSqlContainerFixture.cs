using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// One real MS-SQL container (Testcontainers.MsSql, never a mock and never
/// SQLite-in-memory — CLAUDE.md testing conventions), shared by every test
/// class in the <c>MsSql</c> collection so its ~20-30s startup cost is paid
/// once per test run. Each test asks for its own, uniquely-named database on
/// that one server (<see cref="CreateFreshDatabaseAsync"/>), so tests never
/// interfere with each other's schema state. Mirrors
/// `Billing.IntegrationTests.MsSqlContainerFixture` (feature db_billing)
/// exactly, retargeted at <see cref="NotificationsDbContext"/>.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    // Same image tag as infra/docker-compose.infra.yml's `mssql` service, so
    // the integration tests validate against the same engine build the
    // compose stack runs.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a brand-new, empty database on the shared container and
    /// returns a connection string to it — the starting point every
    /// migration test needs ("migrations run from empty").
    /// </summary>
    public async Task<string> CreateFreshDatabaseAsync(string databaseName)
    {
        await using var masterConnection = new SqlConnection(_container.GetConnectionString());
        await masterConnection.OpenAsync();

        await using (var create = masterConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}];";
            await create.ExecuteNonQueryAsync();
        }

        // feature outbox_and_idempotency (design.md §9.2, task B4): the same
        // one-line change Orders.IntegrationTests' fixture carries, applied
        // here only after confirming this project's suite stays green
        // unchanged under it — infra/mssql/init/01-create-databases.sql sets
        // READ_COMMITTED_SNAPSHOT ON on every deployed database, and this
        // fixture did not.
        await using (var snapshot = masterConnection.CreateCommand())
        {
            snapshot.CommandText = $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
            await snapshot.ExecuteNonQueryAsync();
        }

        return BuildConnectionString(databaseName);
    }

    /// <summary>
    /// Drops <paramref name="databaseName"/> back to nothing — used by the
    /// re-apply-from-empty test to prove the migration is not silently
    /// depending on an artifact a first run left behind.
    /// </summary>
    public async Task DropDatabaseAsync(string databaseName)
    {
        SqlConnection.ClearAllPools();

        await using var masterConnection = new SqlConnection(_container.GetConnectionString());
        await masterConnection.OpenAsync();

        await using var drop = masterConnection.CreateCommand();
        drop.CommandText =
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{databaseName}];";
        await drop.ExecuteNonQueryAsync();
    }

    public string BuildConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    public NotificationsDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new NotificationsDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MsSql";
}
