namespace OrderToCash.Projector.Infrastructure.Messaging.Consumers;

/// <summary>
/// The three fact topics the projector consumes — derived from
/// <c>specs/shared/asyncapi.yaml</c>'s <c>ordersFacts</c>,
/// <c>fulfillmentFacts</c> and <c>billingFacts</c> channels' own
/// <c>bindings.kafka.topic</c>, never retyped — the same literals
/// <c>NotificationFactTopics</c>/<c>SagaFactTopics</c> carry. THREE topic
/// names, not fourteen event types — a subscription list, not a routing
/// table (<c>PR1</c>).
/// </summary>
public static class ProjectorFactTopics
{
    public const string OrdersFacts = "otc.orders.facts.v1";

    public const string FulfillmentFacts = "otc.fulfillment.facts.v1";

    public const string BillingFacts = "otc.billing.facts.v1";

    /// <summary>All three, in the order <c>ProjectorFactsConsumer</c> subscribes to them.</summary>
    public static readonly IReadOnlyList<string> All = [OrdersFacts, FulfillmentFacts, BillingFacts];
}
