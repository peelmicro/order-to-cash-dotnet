using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Review round 1, A1 — <c>OperatorCancelRequestedEnvelope.Topic</c> is a
/// DELIBERATE third literal copy of <c>"otc.orders.facts.v1"</c> (the other
/// two: <see cref="OrdersFactTopic.Name"/> and
/// <c>SagaFactTopics.OrdersFacts</c>), kept so the Application-layer file
/// that declares it need not reference <c>Infrastructure.Outbox</c>. This
/// TEST may reference Infrastructure freely (only production code is
/// bounded), so it is the guard against the two copies drifting apart.
/// </summary>
public sealed class OperatorCancelRequestedEnvelopeTests
{
    [Fact]
    public void Topic_EqualsOrdersFactTopicName()
    {
        Assert.Equal(OrdersFactTopic.Name, OperatorCancelRequestedEnvelope.Topic);
    }
}
