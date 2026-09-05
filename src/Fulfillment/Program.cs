using Microsoft.Extensions.Hosting;
using OrderToCash.Fulfillment;

// The Fulfillment service host — the FIRST runnable Fulfillment host (feature
// fulfillment_stock). The actual composition (AddFulfillment, AddDispatcher,
// the ValidateOnBuild/ValidateScopes forcing) lives in
// FulfillmentHost.CreateBuilder, factored out the same way OrdersHost is.
var builder = FulfillmentHost.CreateBuilder(
    args,
    configure: options =>
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.ClientId = Environment.GetEnvironmentVariable("FULFILLMENT_KAFKA_CLIENT_ID") ?? "otc-fulfillment";
        options.Responder.MaxConcurrentRequests = int.TryParse(Environment.GetEnvironmentVariable("FULFILLMENT_MAX_CONCURRENT_REQUESTS"), out var max) ? max : 32;
    });

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

// Mirrors FulfillmentDbContextFactory's own reading of .env's variable names
// — the runtime host and the design-time migration tooling read the exact
// same environment, deliberately (same shape as OrdersHost's Program.cs).
static string BuildMsSqlConnectionString()
{
    var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
    var database = Environment.GetEnvironmentVariable("MSSQL_DB_FULFILLMENT") ?? "otc_fulfillment";
    var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
    var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
        ?? throw new InvalidOperationException(
            "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the Fulfillment host, " +
            "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_FULFILLMENT|HOST_PORT)=' .env | xargs)");

    return $"Server={host},{port};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
}
