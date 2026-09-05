namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// The one topic every row in this outbox belongs to, by construction.
/// Guarded by <c>tests/Fulfillment.UnitTests/FulfillmentFactTopicTests.cs</c>,
/// which reads <c>specs/shared/asyncapi.yaml</c> as text and extracts the
/// <c>fulfillmentFacts</c> channel's <c>bindings.kafka.topic</c> — the same
/// discipline <c>OrdersFactTopic</c> already follows.
/// </summary>
public static class FulfillmentFactTopic
{
    public const string Name = "otc.fulfillment.facts.v1";
}
