using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// One real MS-SQL container (Testcontainers.MsSql, never a mock and never
/// SQLite-in-memory — CLAUDE.md testing conventions), shared by every test
/// class in the <c>MsSql</c> collection so its ~20-30s startup cost is paid
/// once per test run. Each test asks for its own, uniquely-named database on
/// that one server (<see cref="CreateFreshDatabaseAsync"/>), so tests never
/// interfere with each other's schema state.
/// </summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    // Same image tag as infra/docker-compose.infra.yml's `mssql` service, so
    // the integration tests validate against the same engine build the
    // compose stack runs, not whatever tag Testcontainers' obsolete
    // parameterless constructor used to default to.
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a brand-new, empty database on the shared container and
    /// returns a connection string to it — the starting point every
    /// migration test needs ("applies against an empty database").
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

        // feature outbox_and_idempotency (design.md §9.2): every deployed
        // database is created WITH READ_COMMITTED_SNAPSHOT ON
        // (infra/mssql/init/01-create-databases.sql) — the closest MS-SQL
        // gets to the non-blocking-read semantics the shared spec was
        // written against. Before this feature, this fixture created
        // databases WITHOUT it, so every concurrency test to date proved
        // behaviour under an isolation configuration the running stack does
        // not use. Same statement shape as the init script, ROLLBACK
        // IMMEDIATE included even though a fresh database has nothing to
        // roll back, so the two never drift.
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
    /// depending on artifacts a first run left behind.
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

    public OrdersDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new OrdersDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class MsSqlCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "MsSql";
}
