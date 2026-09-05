// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelayOptions.cs
namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>The relay's own tunables — #7's numbers kept so the benchmark compares like with like.</summary>
public sealed class OutboxRelayOptions
{
    /// <summary>Exists so a scaled-out deployment runs exactly one relay per write model.</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalMs { get; set; } = 250;

    /// <summary>Bounds how long the claim transaction stays open and how many locks it holds.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>The acknowledgement budget, enforced.</summary>
    public int PublishTimeoutMs { get; set; } = 5000;
}
