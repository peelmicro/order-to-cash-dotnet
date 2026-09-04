namespace OrderToCash.Orders.Infrastructure;

/// <summary>Kafka consumer settings for the saga fact stream (design.md §3.2).</summary>
public sealed class OrdersSagaKafkaOptions
{
    /// <summary>Reuses the same bootstrap-servers value the outbox relay's producer already reads — one broker, one setting (design.md §9).</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Bounded <c>Consume(TimeSpan)</c> poll — returns <see langword="null"/> when nothing arrived, so the cancellation token is observed every cycle (design.md §3.1).</summary>
    public int PollTimeoutMs { get; set; } = 1_000;
}

/// <summary>
/// The in-line retry policy for issuing a saga command (SO4, design.md §6.2).
/// Worst-case in-line occupation: <c>MaxAttempts × TimeoutMs + Σ(BackoffMs × 2^n)</c>
/// = 3 × 5 000 + 500 + 1 000 = 16 500 ms — comfortably under
/// <c>max.poll.interval.ms</c>'s 300 000 ms (design.md §3.2), and in any
/// case never on the consume loop (SO10, design.md §5.5).
/// </summary>
public sealed class OrdersSagaCommandOptions
{
    /// <summary>Per-attempt NATS request budget — the same value <c>NatsOptions.StockCheckTimeoutMs</c> already uses (design.md §6.2).</summary>
    public int TimeoutMs { get; set; } = 5_000;

    /// <summary>In-line attempts before parking (SO4/SO5).</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay, doubling between attempts (500 ms, then 1 000 ms).</summary>
    public int BackoffMs { get; set; } = 500;

    /// <summary>How long a claimed row is invisible to a concurrent claim (SO11) — comfortably above the ~16.5 s worst case.</summary>
    public int LeaseMs { get; set; } = 60_000;
}

/// <summary>The sweeper's schedule and batch (SO5, design.md §6.4) — the durability backstop, structurally identical to <see cref="Outbox.OutboxRelayOptions"/>.</summary>
public sealed class OrdersSagaSweeperOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalMs { get; set; } = 30_000;

    /// <summary>The SO3 crash window and the drop-tolerance of the in-process signal (design.md §5.5).</summary>
    public int PendingGraceMs { get; set; } = 10_000;

    /// <summary>15 minutes — park backoff is capped and indefinite, never given up (design.md §6.4).</summary>
    public int ParkRetryCapMs { get; set; } = 900_000;

    public int BatchSize { get; set; } = 20;
}

/// <summary>The three nested settings groups <see cref="OrdersSagaServiceCollectionExtensions.AddOrdersSaga"/> needs (design.md §9).</summary>
public sealed class OrdersSagaOptions
{
    public OrdersSagaKafkaOptions Kafka { get; } = new();

    public OrdersSagaCommandOptions Command { get; } = new();

    public OrdersSagaSweeperOptions Sweeper { get; } = new();
}
