using OrderToCash.Gateway.Domain.Sse;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>Ported from #7's <c>domain/sse/replay-buffer.spec.ts</c> — R55's bounded replay buffer, one case each.</summary>
public sealed class ReplayBufferTests
{
    [Fact]
    public void ReplayAfter_ReturnsEverythingAfterAKnownCursor_ResumedTrue()
    {
        var buffer = new ReplayBuffer<string>(10);
        buffer.Push("c1", "a");
        buffer.Push("c2", "b");
        buffer.Push("c3", "c");

        var result = buffer.ReplayAfter("c1");

        Assert.True(result.Resumed);
        Assert.Equal(["b", "c"], result.Missed);
    }

    [Fact]
    public void ReplayAfter_ReturnsAnEmptyMissedList_WhenTheClientIsAlreadyCaughtUpToTheNewestCursor()
    {
        var buffer = new ReplayBuffer<string>(10);
        buffer.Push("c1", "a");

        var result = buffer.ReplayAfter("c1");

        Assert.True(result.Resumed);
        Assert.Empty(result.Missed);
    }

    [Fact]
    public void ReplayAfter_ResumedFalseNothingMissed_WhenNoLastEventIdWasSentAtAll()
    {
        var buffer = new ReplayBuffer<string>(10);
        buffer.Push("c1", "a");

        var result = buffer.ReplayAfter(null);

        Assert.False(
            result.Resumed,
            $"a client that sent no Last-Event-ID at all was reported resumed=true with {result.Missed.Count} missed item(s) — there is nothing to resume from.");
        Assert.Empty(result.Missed);
    }

    /// <summary>R55/openapi.yaml "the buffer is bounded" — a cursor older than the buffer holds resolves resumed:false, not an error and not a replay of stale data.</summary>
    [Fact]
    public void ReplayAfter_ACursorOlderThanTheBufferHolds_ResolvesResumedFalse()
    {
        var buffer = new ReplayBuffer<string>(2);
        buffer.Push("c1", "a");
        buffer.Push("c2", "b");
        buffer.Push("c3", "c"); // evicts c1

        var evicted = buffer.ReplayAfter("c1");
        var stillHeld = buffer.ReplayAfter("c2");

        Assert.False(
            evicted.Resumed,
            $"cursor 'c1' has been evicted from a 2-slot buffer, yet ReplayAfter reported resumed=true with {evicted.Missed.Count} missed item(s). A cursor the buffer no longer holds must resolve resumed:false, never a partial replay presented as a resumption.");
        Assert.Empty(evicted.Missed);
        Assert.True(
            stillHeld.Resumed,
            "cursor 'c2' is still inside the 2-slot buffer, yet ReplayAfter reported resumed=false — a cursor the buffer DOES hold must resume.");
        Assert.Equal(["c"], stillHeld.Missed);
    }

    [Fact]
    public void ReplayAfter_AnUnknownCursorNeverIssued_IsTreatedTheSameAsOneThatAgedOut_ResumedFalse()
    {
        var buffer = new ReplayBuffer<string>(10);
        buffer.Push("c1", "a");

        var result = buffer.ReplayAfter("not-a-real-cursor");

        Assert.False(
            result.Resumed,
            $"an unknown cursor that was never issued was reported resumed=true with {result.Missed.Count} missed item(s). It must be treated exactly like one that aged out.");
        Assert.Empty(result.Missed);
    }

    [Fact]
    public void Size_NeverGrowsPastItsConfiguredCapacity()
    {
        var buffer = new ReplayBuffer<string>(2);
        buffer.Push("c1", "a");
        buffer.Push("c2", "b");
        buffer.Push("c3", "c");

        Assert.Equal(2, buffer.Size);
    }

    [Fact]
    public void Constructor_RejectsANonPositiveCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayBuffer<string>(0));
    }
}
