using OrderToCash.Projector.Infrastructure;
using OrderToCash.Projector.Infrastructure.Health;
using OrderToCash.Projector.Infrastructure.Observability;

namespace OrderToCash.Projector;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — the shape
/// <c>BillingProgramConfiguration</c> establishes, applied here: the
/// Projector host's environment-reading half of composition, extracted out
/// of <c>Program.cs</c>'s inline <c>configure</c> delegate so
/// <c>ProjectorProgramConfigurationTests</c> can call the EXACT method
/// <c>Program.cs</c> calls.
/// </summary>
public static class ProjectorProgramConfiguration
{
    public static void Configure(ProjectorOptions options)
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.Kafka.BootstrapServers = bootstrapServers;

        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";

        options.Mongo = ProjectorMongoOptions.FromEnvironment();

        // OR1 — the two-variable retry policy family (design.md §3.5). Each
        // read is bound to its OWN variable name.
        options.FactRetry.MaxAttempts = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS"), out var maxAttempts)
            ? maxAttempts
            : 3;
        options.FactRetry.BackoffMs = int.TryParse(Environment.GetEnvironmentVariable("FACT_RETRY_BACKOFF_MS"), out var backoffMs)
            ? backoffMs
            : 500;

        options.DeadLetter.BootstrapServers = bootstrapServers;
    }

    // design.md §9.2 — OTEL_EXPORTER_OTLP_ENDPOINT, read on its own key
    // exactly as every other env read in this class is.
    public static void ConfigureTelemetry(TelemetryOptions options)
    {
        options.OtlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
    }

    // design.md §8.1/§9.2 — group A4. PROJECTOR_HEALTH_PORT is bound to its
    // OWN key (tasks.md A4b's substitution arming target).
    public static void ConfigureHealth(HealthOptions options)
    {
        options.Port = int.TryParse(Environment.GetEnvironmentVariable("PROJECTOR_HEALTH_PORT"), out var port) ? port : 3006;
        options.KafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";
        options.MongoDatabase = ProjectorMongoOptions.FromEnvironment().Database;
    }
}
