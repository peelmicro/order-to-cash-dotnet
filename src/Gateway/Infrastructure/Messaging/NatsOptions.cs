namespace OrderToCash.Gateway.Infrastructure.Messaging;

public sealed class NatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>Default per-call RPC deadline — every list/command endpoint uses this unless a subject-specific override is supplied.</summary>
    public int DefaultTimeoutMs { get; set; } = 5_000;
}
