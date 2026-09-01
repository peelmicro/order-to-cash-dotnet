using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Xunit;

namespace OrderToCash.Seed.IntegrationTests;

/// <summary>
/// One real MS-SQL container AND one real MongoDB container (Testcontainers,
/// never a mock — CLAUDE.md testing conventions), shared by every test class
/// in the <see cref="SeedContainersCollection"/> so their ~20-30s startup
/// cost is paid once per test run. Each test asks for its own,
/// uniquely-named MS-SQL databases (mirroring
/// <c>Orders.IntegrationTests.MsSqlContainerFixture</c>) and its own
/// uniquely-named Mongo database, so tests never interfere with each
/// other's state.
/// </summary>
public sealed class SeedContainersFixture : IAsyncLifetime
{
    // Same image tag as infra/docker-compose.infra.yml's `mssql` service.
    private readonly MsSqlContainer _mssql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();

    // Same major version as infra/docker-compose.infra.yml's `mongodb` service (mongo:8.3.8).
    private readonly MongoDbContainer _mongo =
        new MongoDbBuilder("mongo:8.3.8").Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mssql.StartAsync(), _mongo.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_mssql.DisposeAsync().AsTask(), _mongo.DisposeAsync().AsTask());
    }

    public async Task<string> CreateFreshDatabaseAsync(string databaseName)
    {
        await using var masterConnection = new SqlConnection(_mssql.GetConnectionString());
        await masterConnection.OpenAsync();

        await using (var create = masterConnection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}];";
            await create.ExecuteNonQueryAsync();
        }

        return BuildConnectionString(databaseName);
    }

    public string BuildConnectionString(string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(_mssql.GetConnectionString())
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    public OrdersDbContext CreateOrdersDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(connectionString).Options);

    public FulfillmentDbContext CreateFulfillmentDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<FulfillmentDbContext>().UseSqlServer(connectionString).Options);

    public BillingDbContext CreateBillingDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(connectionString).Options);

    public IMongoDatabase CreateMongoDatabase(string databaseName)
    {
        var client = new MongoClient(_mongo.GetConnectionString());
        return client.GetDatabase(databaseName);
    }
}

[CollectionDefinition(Name)]
public sealed class SeedContainersCollection : ICollectionFixture<SeedContainersFixture>
{
    public const string Name = "SeedContainers";
}
