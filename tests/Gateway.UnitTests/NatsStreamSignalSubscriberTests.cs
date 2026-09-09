using OrderToCash.Gateway.Infrastructure.Messaging;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// <see cref="NatsStreamSignalSubscriber.ExtractOrderIdFromSubject"/> — the
/// pure parsing step (no live NATS broker) that reads the exact format
/// <c>NatsUpdateSignalPublisher</c> constructs a subject from. The
/// broker-level "a malformed frame is logged and skipped, without breaking
/// consumption of the next well-formed frame" case is proved against a
/// REAL broker instead — <c>tests/Gateway.IntegrationTests/StreamHttpTests.cs</c>
/// › <c>AMalformedSignalFrame_IsSkipped_WithoutBreakingConsumptionOfTheNextWellFormedFrame</c>.
/// </summary>
public sealed class NatsStreamSignalSubscriberTests
{
    [Fact]
    public void ExtractOrderIdFromSubject_ReturnsTheTrailingToken_ForAWellFormedOrderUpdatedSubject()
    {
        var orderId = Guid.Parse("9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f");

        var extracted = NatsStreamSignalSubscriber.ExtractOrderIdFromSubject($"readmodel.order.updated.{orderId:D}");

        Assert.Equal(orderId, extracted);
    }

    [Fact]
    public void ExtractOrderIdFromSubject_ReturnsTheTrailingToken_ForAWellFormedTimelineAppendedSubject()
    {
        var orderId = Guid.Parse("9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f");

        var extracted = NatsStreamSignalSubscriber.ExtractOrderIdFromSubject($"readmodel.timeline.appended.{orderId:D}");

        Assert.Equal(orderId, extracted);
    }

    [Fact]
    public void ExtractOrderIdFromSubject_ThrowsFormatException_WhenTheSubjectHasNoDotAtAll()
    {
        Assert.Throws<FormatException>(() => NatsStreamSignalSubscriber.ExtractOrderIdFromSubject("nodotshere"));
    }

    [Fact]
    public void ExtractOrderIdFromSubject_ThrowsFormatException_WhenTheSubjectEndsWithATrailingDot()
    {
        Assert.Throws<FormatException>(() => NatsStreamSignalSubscriber.ExtractOrderIdFromSubject("readmodel.order.updated."));
    }

    [Fact]
    public void ExtractOrderIdFromSubject_ThrowsFormatException_WhenTheTrailingTokenIsNotAGuid()
    {
        Assert.Throws<FormatException>(() => NatsStreamSignalSubscriber.ExtractOrderIdFromSubject("readmodel.order.updated.not-a-guid"));
    }
}
