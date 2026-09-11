namespace OrderToCash.Projector.Infrastructure.Health;

/// <summary>design.md §8.1/§9.2 — everything <see cref="HealthProbeService"/> and its checks need.</summary>
public sealed class HealthOptions
{
    public int Port { get; set; }

    public string KafkaBootstrapServers { get; set; } = string.Empty;

    /// <summary>The read-model probe's own database name — the singleton <c>IMongoClient</c> is shared, but the database it points the <c>ping</c> command at is this feature's own configuration.</summary>
    public string MongoDatabase { get; set; } = string.Empty;
}
