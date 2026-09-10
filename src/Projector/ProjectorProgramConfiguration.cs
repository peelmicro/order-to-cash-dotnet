using OrderToCash.Projector.Infrastructure;

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
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";

        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";

        options.Mongo = ProjectorMongoOptions.FromEnvironment();
    }
}
