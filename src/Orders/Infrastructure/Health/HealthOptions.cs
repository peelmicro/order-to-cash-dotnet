namespace OrderToCash.Orders.Infrastructure.Health;

/// <summary>
/// design.md §8.1/§9.2 — everything <see cref="HealthProbeService"/> and its
/// checks need. <see cref="Port"/> is read from <c>ORDERS_HEALTH_PORT</c>
/// (default <c>3002</c>) in production; integration tests bind <c>0</c> (an
/// OS-assigned ephemeral port) instead, so many parallel test runs never
/// collide on one hard-coded number — <see cref="HealthProbeService.BoundPort"/>
/// reports which port was actually bound.
/// </summary>
public sealed class HealthOptions
{
    public int Port { get; set; }

    /// <summary>The write-model probe's own connection string — read independently by <c>OrdersProgramConfiguration.ConfigureHealth</c>, the same value <c>ConfigureOutbox</c> builds, never shared state between the two delegates.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The fact-stream probe's own bootstrap servers.</summary>
    public string KafkaBootstrapServers { get; set; } = string.Empty;
}
