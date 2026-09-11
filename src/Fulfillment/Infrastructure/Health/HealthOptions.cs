namespace OrderToCash.Fulfillment.Infrastructure.Health;

/// <summary>design.md §8.1/§9.2 — everything <see cref="HealthProbeService"/> and its checks need.</summary>
public sealed class HealthOptions
{
    public int Port { get; set; }

    /// <summary>The write-model probe's own connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
