namespace OrderToCash.Notifications.Infrastructure.Health;

/// <summary>design.md §8.1/§9.2 — everything <see cref="HealthProbeService"/> and its checks need.</summary>
public sealed class HealthOptions
{
    public int Port { get; set; }

    public string ConnectionString { get; set; } = string.Empty;

    public string KafkaBootstrapServers { get; set; } = string.Empty;
}
