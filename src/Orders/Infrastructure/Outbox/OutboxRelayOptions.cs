namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>The relay's own tunables — design.md §8, #7's numbers kept so the benchmark compares like with like.</summary>
public sealed class OutboxRelayOptions
{
    /// <summary>Exists so a scaled-out deployment runs exactly one relay per write model (design.md §5.2's ordering caveat).</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalMs { get; set; } = 250;

    /// <summary>Bounds how long the claim transaction stays open and how many locks it holds.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>The acknowledgement budget, enforced (OI14).</summary>
    public int PublishTimeoutMs { get; set; } = 5000;
}
