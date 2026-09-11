using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Health;
using OrderToCash.Orders.Infrastructure.Observability;

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
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.BootstrapServers = bootstrapServers;

        // OR1 — the two-variable retry policy family (design.md §3.5). Each
        // read is bound to its OWN variable name; FactRetryDispatcherTests'
        // substitution arming (tasks.md A1a) proves neither read has been
        // repointed at the other's key.
        options.FactRetry.MaxAttempts = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS"), out var maxAttempts)
            ? maxAttempts
            : 3;
        options.FactRetry.BackoffMs = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_BACKOFF_MS"), out var backoffMs)
            ? backoffMs
            : 500;

        options.DeadLetter.BootstrapServers = bootstrapServers;
    }

    // design.md §9.2 — the single new variable this feature's telemetry
    // half reads (FACT_RETRY_MAX_ATTEMPTS/FACT_RETRY_BACKOFF_MS above
    // already establish the "each read bound to its own key" discipline
    // this one follows too).
    public static void ConfigureTelemetry(TelemetryOptions options)
    {
        options.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    }

    // design.md §8.1/§9.2 — group A4. ORDERS_HEALTH_PORT is bound to its OWN
    // key (tasks.md A4b's substitution arming target: the five *_HEALTH_PORT
    // variables are a sibling family, and this read must never be
    // repointable at another service's key without a test failing and
    // naming it). ConnectionString/KafkaBootstrapServers are read
    // INDEPENDENTLY here rather than shared from ConfigureOutbox/ConfigureSaga
    // — the same "each configure delegate reads its own environment" shape
    // every method in this class already follows.
    public static void ConfigureHealth(HealthOptions options)
    {
        options.Port = int.TryParse(Environment.GetEnvironmentVariable("ORDERS_HEALTH_PORT"), out var port) ? port : 3002;
        options.ConnectionString = BuildMsSqlConnectionString();
        options.KafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
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
