using Microsoft.Extensions.Hosting;
using OrderToCash.Billing;
using OrderToCash.Billing.Infrastructure;

// The Billing service host — the FIRST runnable Billing host (design.md
// §14.3). The actual composition (AddBilling, AddDispatcher, the
// ValidateOnBuild/ValidateScopes forcing) lives in BillingHost.CreateBuilder,
// factored out the same way FulfillmentHost/OrdersHost are.
var builder = BillingHost.CreateBuilder(
    args,
    configure: (BillingOptions options) =>
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.ClientId = Environment.GetEnvironmentVariable("BILLING_KAFKA_CLIENT_ID") ?? "otc-billing";
        options.Responder.MaxConcurrentRequests = int.TryParse(Environment.GetEnvironmentVariable("BILLING_MAX_CONCURRENT_REQUESTS"), out var max) ? max : 32;
    });

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);

// Mirrors BillingDbContextFactory's own reading of .env's variable names —
// the runtime host and the design-time migration tooling read the exact
// same environment, deliberately (same shape as FulfillmentHost's Program.cs).
static string BuildMsSqlConnectionString()
{
    var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
    var database = Environment.GetEnvironmentVariable("MSSQL_DB_BILLING") ?? "otc_billing";
    var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
    var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
        ?? throw new InvalidOperationException(
            "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the Billing host, " +
            "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_BILLING|HOST_PORT)=' .env | xargs)");

    return $"Server={host},{port};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
}
