using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// One real MS-SQL container (Testcontainers.MsSql, never a mock and never
/// SQLite-in-memory — CLAUDE.md testing conventions), shared by every test
/// class in the <c>MsSql</c> collection so its ~20-30s startup cost is paid
/// once per test run. Each test asks for its own, uniquely-named database on
/// that one server (<see cref="CreateFreshDatabaseAsync"/>), so tests never
/// interfere with each other's schema state. Mirrors
/// `Orders.IntegrationTests.MsSqlContainerFixture` (feature db_orders)
/// exactly, retargeted at <see cref="FulfillmentDbContext"/>.
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

    public FulfillmentDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new FulfillmentDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MsSql";
}
