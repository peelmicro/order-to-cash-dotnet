using OrderToCash.Billing.Infrastructure.Messaging;
using OrderToCash.Billing.Infrastructure.Outbox;

namespace OrderToCash.Billing.Infrastructure;

/// <summary>`BC21`/design.md §4.1 — the responder's own concurrency bound. Default 32, chosen against ADO.NET's default <c>Max Pool Size</c> of 100, the same reasoning <c>fulfillment_stock/design.md</c> §6.2 gives. Renamed from <c>CreditResponderOptions</c> — the responder it bounds is no longer credit-scoped (`BI31`, design.md §4.1).</summary>
public sealed class BillingResponderOptions
{
    public int MaxConcurrentRequests { get; set; } = 32;
}

/// <summary>The configuration <c>BillingServiceCollectionExtensions</c> needs (design.md §14.1).</summary>
public sealed class BillingOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NatsOptions Nats { get; } = new();

    public KafkaOptions Kafka { get; } = new() { ClientId = "otc-billing" };

    public OutboxRelayOptions Relay { get; } = new();

    public BillingResponderOptions Responder { get; } = new();

    /// <summary>
    /// R43 — the proportion of fitting hold requests
    /// <c>SimulatorCreditDecision</c> refuses with <c>simulated_failure_rate</c>,
    /// in the closed interval <c>[0, 1]</c>. Defaults to <c>0</c> (R43's own
    /// default) so a caller who never touches this property gets
    /// deterministic behaviour; populate it via
    /// <see cref="CreditDecisions.CreditSimulatorOptionsLoader.Load"/>
    /// from <c>CREDIT_FAILURE_RATE</c>, never with an unvalidated raw value.
    /// </summary>
    public double CreditFailureRate { get; set; }
}
