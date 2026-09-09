using OrderToCash.Gateway.Domain.Sse;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Ported from #7's <c>domain/sse/cursor.spec.ts</c> — the SSE frame
/// <c>id:</c> cursor shape (openapi.yaml's own example:
/// <c>id: 1755511234567-17</c>).
/// </summary>
public sealed class CursorGeneratorTests
{
    [Fact]
    public void Next_ProducesTheOpenApiYamlFrameFormatShape_EpochMsDashSeq()
    {
        var generator = new CursorGenerator(() => new DateTimeOffset(2026, 8, 18, 10, 15, 2, 100, TimeSpan.Zero));

        Assert.Equal("1787048102100-1", generator.Next());
    }

    [Fact]
    public void Next_NeverRepeatsACursor_ForTwoFramesMintedWithinTheSameMillisecond()
    {
        var fixedInstant = new DateTimeOffset(2026, 8, 18, 10, 15, 2, 100, TimeSpan.Zero);
        var generator = new CursorGenerator(() => fixedInstant);

        var first = generator.Next();
        var second = generator.Next();

        Assert.NotEqual(first, second);
        Assert.Equal("1787048102100-2", second);
    }

    [Fact]
    public void Next_AdvancesTheEpochMillisecondPrefix_WhenTheClockAdvances()
    {
        var current = new DateTimeOffset(2026, 8, 18, 10, 15, 2, 100, TimeSpan.Zero);
        var generator = new CursorGenerator(() => current);

        var first = generator.Next();
        current = current.AddSeconds(1);
        var second = generator.Next();

        Assert.Equal("1787048102100-1", first);
        Assert.Equal("1787048103100-2", second);
    }

    /// <summary>
    /// The one structural adaptation from #7's own single-threaded
    /// (Node) version, recorded in the ported-idiom ledger: the sequence
    /// counter uses <see cref="System.Threading.Interlocked.Increment(ref long)"/>
    /// because THIS hub is fed by two genuinely concurrent NATS
    /// subscription loops. Proves uniqueness holds under real concurrency,
    /// not merely under a single-threaded assumption — the class of defect
    /// a plain <c>sequence += 1</c> ported unchanged would reintroduce
    /// silently (no exception, just occasional duplicate cursors under
    /// load).
    /// </summary>
    [Fact]
    public void Next_NeverProducesADuplicateCursor_UnderGenuineConcurrentCalls()
    {
        var fixedInstant = new DateTimeOffset(2026, 8, 18, 10, 15, 2, 100, TimeSpan.Zero);
        var generator = new CursorGenerator(() => fixedInstant);
        const int callsPerThread = 2_000;
        const int threadCount = 8;

        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        var barrier = new Barrier(threadCount);
        var threads = Enumerable.Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                for (var i = 0; i < callsPerThread; i++)
                {
                    results.Add(generator.Next());
                }
            }))
            .ToList();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Equal(callsPerThread * threadCount, results.Count);
        Assert.Equal(results.Count, results.Distinct(StringComparer.Ordinal).Count());
    }
}
