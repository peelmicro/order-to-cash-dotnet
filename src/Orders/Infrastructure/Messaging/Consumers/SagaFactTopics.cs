namespace OrderToCash.Orders.Infrastructure.Messaging.Consumers;

/// <summary>
/// The three fact topics the saga orchestrator consumes — derived from
/// <c>specs/shared/asyncapi.yaml</c>'s <c>ordersFacts</c>,
/// <c>fulfillmentFacts</c> and <c>billingFacts</c> channels' own
/// <c>bindings.kafka.topic</c>, never retyped (design.md §3.2). Guarded by
/// <c>tests/Orders.UnitTests/SagaFactTopicsTests.cs</c>, which reads the spec
/// as text — the same discipline <see cref="Outbox.OrdersFactTopic"/> already
/// follows for the producer side.
/// </summary>
public static class SagaFactTopics
{
    public const string OrdersFacts = "otc.orders.facts.v1";

    public const string FulfillmentFacts = "otc.fulfillment.facts.v1";

    public const string BillingFacts = "otc.billing.facts.v1";

    /// <summary>All three, in the order <c>SagaFactsConsumer</c> subscribes to them.</summary>
    public static readonly IReadOnlyList<string> All = [OrdersFacts, FulfillmentFacts, BillingFacts];
}
