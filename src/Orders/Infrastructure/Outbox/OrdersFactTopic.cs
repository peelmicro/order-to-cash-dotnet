namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// The one topic every row in this outbox belongs to, by construction —
/// design.md §5.3. Guarded by
/// <c>tests/Orders.UnitTests/OrdersFactTopicTests.cs</c>, which reads
/// <c>specs/shared/asyncapi.yaml</c> as text and extracts the
/// <c>ordersFacts</c> channel's <c>bindings.kafka.topic</c> — the same
/// "derive the topic from the spec, never retype it" discipline
/// <c>infra/kafka/create-topics.sh</c> already follows.
/// </summary>
public static class OrdersFactTopic
{
    public const string Name = "otc.orders.facts.v1";
}
