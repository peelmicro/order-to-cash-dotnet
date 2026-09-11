using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using OrderToCash.Billing.Infrastructure.Health;
using OrderToCash.Billing.Infrastructure.Observability;

namespace OrderToCash.Billing;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — the Billing
/// host's environment-reading half of composition, extracted out of
/// <c>Program.cs</c>'s inline <c>configure</c> delegate. Top-level
/// statements (what <c>Program.cs</c> is) compile into a hidden,
/// inaccessible <c>Program.&lt;Main&gt;$</c> method, so a lambda written
/// inline there can never be called by a test — deleting an env read from
/// it left the whole suite green (the defect this feature exists to fix).
/// <c>Program.cs</c> now reads only
/// <c>BillingHost.CreateBuilder(args, configure: BillingProgramConfiguration.Configure)</c>,
/// so <c>BillingProgramConfigurationTests</c> drives the EXACT method
/// <c>Program.cs</c> calls — not a re-implementation of it.
/// </summary>
public static class BillingProgramConfiguration
{
    public static void Configure(BillingOptions options)
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.ClientId = Environment.GetEnvironmentVariable("BILLING_KAFKA_CLIENT_ID") ?? "otc-billing";
        options.Responder.MaxConcurrentRequests = int.TryParse(Environment.GetEnvironmentVariable("BILLING_MAX_CONCURRENT_REQUESTS"), out var max) ? max : 32;

        // R43 — validated eagerly, HERE, so an out-of-range or non-numeric
        // CREDIT_FAILURE_RATE throws before BillingHost.CreateBuilder ever
        // returns and the process never reaches host.RunAsync().
        options.CreditFailureRate = CreditSimulatorOptionsLoader.Load(Environment.GetEnvironmentVariable("CREDIT_FAILURE_RATE"));
    }

    // design.md §9.2 — OTEL_EXPORTER_OTLP_ENDPOINT, read on its own key
    // exactly as every other env read in this class is.
    public static void ConfigureTelemetry(TelemetryOptions options)
    {
        options.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    }

    // design.md §8.1/§9.2 — group A4. BILLING_HEALTH_PORT is bound to its
    // OWN key (tasks.md A4b's substitution arming target).
    public static void ConfigureHealth(HealthOptions options)
    {
        options.Port = int.TryParse(Environment.GetEnvironmentVariable("BILLING_HEALTH_PORT"), out var port) ? port : 3004;
        options.ConnectionString = BuildMsSqlConnectionString();
    }

    // Mirrors BillingDbContextFactory's own reading of .env's variable names —
    // the runtime host and the design-time migration tooling read the exact
    // same environment, deliberately (same shape as FulfillmentHost's Program.cs).
    public static string BuildMsSqlConnectionString()
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
}
