namespace OrderToCash.Billing.Infrastructure.Outbox;

/// <summary>
/// The one topic every row in this outbox belongs to, by construction —
/// design.md §8.5. Guarded by
/// <c>tests/Billing.UnitTests/BillingFactTopicTests.cs</c>, which reads
/// <c>specs/shared/asyncapi.yaml</c> as text and extracts the
/// <c>billingFacts</c> channel's <c>bindings.kafka.topic</c> — the same
/// "derive the topic from the spec, never retype it" discipline
/// <c>OrdersFactTopic</c>/<c>FulfillmentFactTopic</c> already follow.
/// </summary>
public static class BillingFactTopic
{
    public const string Name = "otc.billing.facts.v1";
}
