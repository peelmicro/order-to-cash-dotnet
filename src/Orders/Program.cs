using Microsoft.Extensions.Hosting;
using OrderToCash.Orders;

// The Orders service host — the FIRST Program.cs this repository builds
// (outbox_and_idempotency design.md §2.3: "there is no Program.cs yet, and
// this feature does not write one" — feature orders_acceptance is that
// feature). The actual composition (AddOrdersOutbox, AddOrdersAcceptance,
// AddDispatcher, and the ValidateOnBuild/ValidateScopes forcing — review
// D3/D6) lives in OrdersHost.CreateBuilder, factored out so
// OrdersDispatcherRegistrationTests can drive the SAME method this file
// calls rather than reconstructing its own copy of the wiring.
var builder = OrdersHost.CreateBuilder(
    args,
    configureOutbox: options =>
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
    },
    configureAcceptance: options =>
    {
        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";
    });

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

// Mirrors OrdersDbContextFactory's own reading of .env's variable names
// (MSSQL_HOST/MSSQL_HOST_PORT/MSSQL_DB_ORDERS/MSSQL_APP_USER/MSSQL_APP_PASSWORD)
// — the runtime host and the design-time migration tooling read the exact
// same environment, deliberately.
static string BuildMsSqlConnectionString()
{
    var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
    var database = Environment.GetEnvironmentVariable("MSSQL_DB_ORDERS") ?? "otc_orders";
    var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
    var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
        ?? throw new InvalidOperationException(
            "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the Orders host, " +
            "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_ORDERS|HOST_PORT)=' .env | xargs)");

    return $"Server={host},{port};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
}
