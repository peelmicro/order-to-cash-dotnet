using OrderToCash.Orders.Infrastructure;

namespace OrderToCash.Orders;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — the shape
/// <c>BillingProgramConfiguration</c> establishes, applied here: the Orders
/// host's three environment-reading configure delegates
/// (<c>OrdersHost.CreateBuilder</c> takes one per port group — outbox,
/// acceptance, saga), extracted out of <c>Program.cs</c>'s three inline
/// lambdas so <c>OrdersProgramConfigurationTests</c> can call the EXACT
/// methods <c>Program.cs</c> calls.
/// </summary>
public static class OrdersProgramConfiguration
{
    public static void ConfigureOutbox(OrdersOutboxOptions options)
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
    }

    public static void ConfigureAcceptance(OrdersAcceptanceOptions options)
    {
        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";
    }

    public static void ConfigureSaga(OrdersSagaOptions options)
    {
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
    }

    // Mirrors OrdersDbContextFactory's own reading of .env's variable names
    // (MSSQL_HOST/MSSQL_HOST_PORT/MSSQL_DB_ORDERS/MSSQL_APP_USER/MSSQL_APP_PASSWORD)
    // — the runtime host and the design-time migration tooling read the exact
    // same environment, deliberately.
    public static string BuildMsSqlConnectionString()
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
}
