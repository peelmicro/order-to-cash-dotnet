namespace OrderToCash.Notifications.Infrastructure.Messaging.Consumers;

/// <summary>
/// The three fact topics this service consumes — derived from
/// <c>specs/shared/asyncapi.yaml</c>'s <c>ordersFacts</c>,
/// <c>fulfillmentFacts</c> and <c>billingFacts</c> channels' own
/// <c>bindings.kafka.topic</c>, never retyped — the same literals Orders'
/// own <c>SagaFactTopics</c> carries. Notifications reads all three because
/// its seven notified facts (domain-model.md §7.3) span all three
/// producers; which of the fourteen facts on these topics are actually acted
/// on is filtered downstream in <c>NotificationFactsConsumer</c>, not here.
/// </summary>
public static class NotificationFactTopics
{
    public const string OrdersFacts = "otc.orders.facts.v1";

    public const string FulfillmentFacts = "otc.fulfillment.facts.v1";

    public const string BillingFacts = "otc.billing.facts.v1";

    /// <summary>All three, in the order <c>NotificationFactsConsumer</c> subscribes to them.</summary>
    public static readonly IReadOnlyList<string> All = [OrdersFacts, FulfillmentFacts, BillingFacts];
}
