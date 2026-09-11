namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>
/// OR1's retry policy for the fact-consumer retry-then-dead-letter wrapper —
/// <c>FACT_RETRY_MAX_ATTEMPTS</c> (default 3) and <c>FACT_RETRY_BACKOFF_MS</c>
/// (default 500 ms, doubling between attempts). Read the same way in every
/// fact-consuming service's own <c>*ProgramConfiguration</c> (design.md
/// §3.5) — not part of <see cref="FactRetryDispatcher"/>'s own byte-identity
/// parity guard (OR2 guards the dispatcher file itself, not this options
/// class), but present at the identical path in every copy so the canonical
/// file needs no <c>using</c> to reach it: it lives in this same namespace
/// in every service.
/// </summary>
public sealed class FactRetryOptions
{
    public int MaxAttempts { get; set; } = 3;

    public int BackoffMs { get; set; } = 500;
}
