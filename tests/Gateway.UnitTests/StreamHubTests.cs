using OrderToCash.Gateway.Application.Stream;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>The first three cases are ported from #7's <c>application/stream-hub.spec.ts</c> (which has exactly three); the remaining three (<see cref="MintCursor_NeverAddsToTheReplayBuffer"/>, <see cref="Publish_NeverDeliversAFrame_ToADroppedSubscription"/>, <see cref="Unsubscribe_RemovesTheSubscriber_FromSubscriberCount"/>) are NOT ported — #7's own suite never directly guards them; the first two are properties <c>stream.controller.ts</c>'s own comments merely claim.</summary>
public sealed class StreamHubTests
{
    private static DateTimeOffset FixedInstant => new(2026, 8, 18, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publish_EmitsAFrameToLiveSubscribers_WithAFreshCursor()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        var (_, reader) = hub.Subscribe();

        var published = hub.Publish("order.updated", Guid.Parse("11111111-1111-1111-1111-111111111111"), """{"orderId":"order-1","status":"placed"}""");

        var received = await reader.ReadAsync();
        Assert.Equal(published.Cursor, received.Cursor);
        Assert.Equal("order.updated", received.EventType);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), received.OrderId);
    }

    [Fact]
    public void ReplayAfter_ResumesFromTheBuffer_ResumedTrue_EverythingAfterTheGivenCursor()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        var orderId = Guid.NewGuid();
        var first = hub.Publish("order.updated", orderId, """{"orderId":"x"}""");
        hub.Publish("timeline.appended", orderId, """{"orderId":"x","eventType":"stock.reserved.v1"}""");

        var result = hub.ReplayAfter(first.Cursor);

        Assert.True(result.Resumed);
        var missed = Assert.Single(result.Missed);
        Assert.Equal("timeline.appended", missed.EventType);
    }

    [Fact]
    public void ReplayAfterNull_AFreshConnectionWithNoLastEventId_IsResumedFalse()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        hub.Publish("order.updated", Guid.NewGuid(), """{"orderId":"x"}""");

        var result = hub.ReplayAfter(null);

        Assert.False(result.Resumed);
        Assert.Empty(result.Missed);
    }

    /// <summary>
    /// The property <c>StreamEndpoints</c>'s own comment on `ping`/`stream.ready`
    /// asserts but nothing else in this suite directly exercises: a MINTED
    /// cursor (never a PUBLISHED one) is never replayable — the structural
    /// reason a client whose remembered cursor happens to be a `ping`'s
    /// answers `resumed: false`. Guards against a future edit that
    /// accidentally routes <see cref="StreamHub.MintCursor"/> through
    /// <see cref="StreamHub.Publish"/> (or otherwise pushes it into the
    /// buffer).
    /// </summary>
    [Fact]
    public void MintCursor_NeverAddsToTheReplayBuffer()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        hub.Publish("order.updated", Guid.NewGuid(), """{"orderId":"x"}""");

        var minted = hub.MintCursor();

        var result = hub.ReplayAfter(minted);
        Assert.False(result.Resumed);
        Assert.Empty(result.Missed);
    }

    /// <summary>An unsubscribed connection's channel must never receive a frame published after it left — proves <see cref="StreamHub.Unsubscribe"/> genuinely removes the subscriber from the broadcast, not merely from a count.</summary>
    [Fact]
    public void Publish_NeverDeliversAFrame_ToADroppedSubscription()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        var (subscriptionId, reader) = hub.Subscribe();
        hub.Unsubscribe(subscriptionId);

        hub.Publish("order.updated", Guid.NewGuid(), """{"orderId":"x"}""");

        Assert.False(reader.TryRead(out _));
        Assert.True(reader.Completion.IsCompleted);
    }

    [Fact]
    public void Unsubscribe_RemovesTheSubscriber_FromSubscriberCount()
    {
        var hub = new StreamHub(() => FixedInstant, 10);
        var (subscriptionId, _) = hub.Subscribe();
        Assert.Equal(1, hub.SubscriberCount);

        hub.Unsubscribe(subscriptionId);

        Assert.Equal(0, hub.SubscriberCount);
    }
}
