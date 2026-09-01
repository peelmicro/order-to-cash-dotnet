using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` / `dotnet ef database
/// update` can build a <see cref="FulfillmentDbContext"/> without a running
/// host — the Fulfillment service has no `Program.cs` yet (that lands with
/// the service's own feature; this feature is schema-only). The connection
/// string mirrors the variable names `.env` already declares
/// (`MSSQL_APP_USER`/`MSSQL_APP_PASSWORD`/`MSSQL_DB_FULFILLMENT`/
/// `MSSQL_HOST_PORT`), read from the environment against the compose stack
/// running on `localhost:1433`. This factory is never invoked outside
/// migration tooling; the runtime host will register <see
/// cref="FulfillmentDbContext"/> through DI when the service is built.
/// </summary>
public sealed class FulfillmentDbContextFactory : IDesignTimeDbContextFactory<FulfillmentDbContext>
{
    public FulfillmentDbContext CreateDbContext(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
        var database = Environment.GetEnvironmentVariable("MSSQL_DB_FULFILLMENT") ?? "otc_fulfillment";
        var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";

        // Deliberately NO fallback here (feature db_orders review D6): a
        // hardcoded dev password would restate `.env`'s value in source,
        // where it can drift silently. This factory is design-time tooling
        // only — run by a developer with a shell, never by a deployed
        // process — so failing loudly with a clear message is strictly
        // better than silently pointing at a password that may no longer
        // match.
        var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
            ?? throw new InvalidOperationException(
                "MSSQL_APP_PASSWORD is not set. Export the value from .env before running " +
                "'dotnet ef' against FulfillmentDbContext, e.g.: " +
                "export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_FULFILLMENT|HOST_PORT)=' .env | xargs)");

        var connectionString =
            $"Server={host},{port};Database={database};User Id={user};Password={password};" +
            "TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<FulfillmentDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new FulfillmentDbContext(optionsBuilder.Options);
    }
}
