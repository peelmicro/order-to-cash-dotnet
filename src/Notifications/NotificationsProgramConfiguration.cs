using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Health;
using OrderToCash.Notifications.Infrastructure.Observability;

namespace OrderToCash.Notifications;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — the shape
/// <c>BillingProgramConfiguration</c> establishes, applied here: the
/// Notifications host's environment-reading half of composition, extracted
/// out of <c>Program.cs</c>'s inline <c>configure</c> delegate so
/// <c>NotificationsProgramConfigurationTests</c> can call the EXACT method
/// <c>Program.cs</c> calls — including the <c>SenderKind = Smtp</c> switch
/// that makes this the one caller in the whole repository that sends real
/// mail.
/// </summary>
public static class NotificationsProgramConfiguration
{
    public static void Configure(NotificationsOptions options)
    {
        options.ConnectionString = BuildMsSqlConnectionString();
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.BootstrapServers = bootstrapServers;

        // OR1 — the two-variable retry policy family (design.md §3.5). Each
        // read is bound to its OWN variable name.
        options.FactRetry.MaxAttempts = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS"), out var maxAttempts)
            ? maxAttempts
            : 3;
        options.FactRetry.BackoffMs = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_BACKOFF_MS"), out var backoffMs)
            ? backoffMs
            : 500;

        options.DeadLetter.BootstrapServers = bootstrapServers;

        // The real host always sends real mail — Mailpit locally, per the
        // brief's "MailKit → Mailpit" instruction. Every automated test
        // leaves NotificationsOptions.SenderKind at its Console default
        // instead of calling NotificationsHost.CreateBuilder with this
        // configure delegate.
        options.SenderKind = NotificationSenderKind.Smtp;
        options.Smtp.Host = Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_HOST") ?? "localhost";
        options.Smtp.Port = int.TryParse(Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_PORT"), out var smtpPort)
            ? smtpPort
            : int.TryParse(Environment.GetEnvironmentVariable("MAILPIT_SMTP_HOST_PORT"), out var mailpitPort) ? mailpitPort : 1025;
        options.Smtp.FromAddress = Environment.GetEnvironmentVariable("NOTIFICATIONS_SMTP_FROM_ADDRESS") ?? "no-reply@order-to-cash.example";
    }

    // design.md §9.2 — OTEL_EXPORTER_OTLP_ENDPOINT, read on its own key
    // exactly as every other env read in this class is.
    public static void ConfigureTelemetry(TelemetryOptions options)
    {
        options.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    }

    // design.md §8.1/§9.2 — group A4. NOTIFICATIONS_HEALTH_PORT is bound to
    // its OWN key (tasks.md A4b's substitution arming target).
    public static void ConfigureHealth(HealthOptions options)
    {
        options.Port = int.TryParse(Environment.GetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT"), out var port) ? port : 3005;
        options.ConnectionString = BuildMsSqlConnectionString();
        options.KafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
    }

    // Mirrors NotificationsDbContextFactory's own reading of .env's variable
    // names — the runtime host and the design-time migration tooling read the
    // exact same environment, deliberately.
    public static string BuildMsSqlConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
        var database = Environment.GetEnvironmentVariable("MSSQL_DB_NOTIFICATIONS") ?? "otc_notifications";
        var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
        var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
            ?? throw new InvalidOperationException(
                "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the Notifications host, " +
                "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_NOTIFICATIONS|HOST_PORT)=' .env | xargs)");

        return $"Server={host},{port};Database={database};User Id={user};Password={password};TrustServerCertificate=True;";
    }
}
