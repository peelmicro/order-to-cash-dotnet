// COPY OF — tests/Fulfillment.IntegrationTests/MsSqlContainerFixture.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>One real MS-SQL container — needed only for the one true end-to-end proof against Fulfillment's REAL <c>StockRpcResponder</c>.</summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateFreshDatabaseAsync(string databaseName)
    {
        await using var masterConnection = new SqlConnection(_container.GetConnectionString());
        await masterConnection.OpenAsync();

        await using (var create = masterConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}];";
            await create.ExecuteNonQueryAsync();
        }

        await using (var snapshot = masterConnection.CreateCommand())
        {
            snapshot.CommandText = $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
            await snapshot.ExecuteNonQueryAsync();
        }

        return BuildConnectionString(databaseName);
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
