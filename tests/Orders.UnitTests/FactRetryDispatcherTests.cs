using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OR1's own unit-level proof, over instant fakes — design.md §3.2. Pure:
/// no broker, no clock skew, no real wall-clock wait. Ledger rows L12,
/// L13, L19.
/// </summary>
public sealed class FactRetryDispatcherTests
{
    private static readonly Guid _eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string EventType = "stock.reserved.v1";
    private const string SourceTopic = "otc.orders.facts.v1";

    private static FactStreamMessage BuildMessage() => new(SourceTopic, 0, 0, "payload-bytes"u8.ToArray());

    private static FactRetryDispatcher BuildDispatcher(
        IFactRetryDelay delay,
        IDeadLetterPublisher deadLetters,
        FactRetryOptions? options = null,
        DateTimeOffset? now = null) =>
        new(
            new FakeClock(now ?? DateTimeOffset.UtcNow),
            delay,
            deadLetters,
            Options.Create(options ?? new FactRetryOptions()),
            NullLogger<FactRetryDispatcher>.Instance);

    [Fact]
    public async Task OR1_RetriesToTheConfiguredMaximumWithExponentialBackoff_ThenPublishesToTheDlqTopicAndReturnsNormally()
    {
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = BuildDispatcher(delay, deadLetters);
        var process = new CountingFailingProcess();

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(3, process.Invocations); // MaxAttempts default.
        Assert.Equal([500, 1000], delay.Delays); // Exponential — not linear.
        var published = Assert.Single(deadLetters.Published);
        Assert.Equal(SourceTopic, published.SourceTopic);
        Assert.Equal(ConsumerName.OrdersSaga, published.FailedConsumer);
        Assert.Equal(3, published.Attempts);
        Assert.Equal(EventType, published.EventType);
    }

    [Fact]
    public async Task OR1_RetriesThenSucceeds_WithoutEverPublishingToTheDlq()
    {
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = BuildDispatcher(delay, deadLetters);
        var process = new SucceedAfterProcess(succeedOnAttempt: 3);

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(3, process.Invocations);
        Assert.Equal([500, 1000], delay.Delays);
        Assert.Empty(deadLetters.Published);
    }

    [Fact]
    public async Task OR1_SucceedsOnTheFirstAttempt_WithNoDelayAndNoDlqPublish()
    {
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = BuildDispatcher(delay, deadLetters);
        var process = new SucceedAfterProcess(succeedOnAttempt: 1);

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(1, process.Invocations);
        Assert.Empty(delay.Delays);
        Assert.Empty(deadLetters.Published);
    }

    /// <summary>
    /// OR5/design.md §7 — otc_fact_processing_latency_ms recorded on the
    /// SUCCESS path, tagged by consumer. D8 (review round 3): asserts the
    /// EXACT clock-driven duration, not merely "a number >= 0" — a fake
    /// clock advanced from entry to the success record makes the value
    /// deterministic, the same discipline #7's
    /// fact-retry-dispatcher-metrics.spec.ts:63 uses (`toBe(240)`).
    /// </summary>
    [Fact]
    public async Task OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer()
    {
        var enteredAt = DateTimeOffset.Parse("2026-08-26T09:00:00.0000000+00:00");
        var exitedAt = enteredAt.AddMilliseconds(240);
        var clock = new FakeClock(enteredAt);
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = new FactRetryDispatcher(
            clock,
            delay,
            deadLetters,
            Options.Create(new FactRetryOptions()),
            NullLogger<FactRetryDispatcher>.Instance);
        var process = new SucceedAfterProcess(succeedOnAttempt: 1, onInvoke: () => clock.UtcNow = exitedAt);

        using var meterListener = MetricCapture.ForInstrument("otc_fact_processing_latency_ms");
        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        var measurement = Assert.Single(meterListener.Measurements);
        Assert.Equal(240, measurement.Value); // 09:00:00.240 - 09:00:00.000, the EXACT clock-driven duration.
        Assert.Equal("OrdersSaga", measurement.Tags.Single(t => t.Key == "consumer").Value?.ToString());
    }

    /// <summary>
    /// OR5/design.md §7 — otc_fact_processing_latency_ms recorded ALSO on
    /// the exhausted-retry/DLQ path, not only on success. D8 (review
    /// round 3): asserts the EXACT clock-driven duration on this path
    /// too — the same discipline #7's
    /// fact-retry-dispatcher-metrics.spec.ts:89 uses (`toBe(5750)`).
    /// <c>MaxAttempts = 1</c> makes this a single failing invocation, so
    /// every clock read AFTER it (<c>firstFailedAt</c>, <c>failedAt</c>,
    /// the histogram's own exit read) observes the same advanced instant
    /// — the dispatcher's exit read is still a FRESH <c>clock.UtcNow</c>
    /// call, not a reuse of <c>failedAt</c>'s stored value, matching #7's
    /// own separate <c>this.clock.now()</c> call at record time.
    /// </summary>
    [Fact]
    public async Task OtcFactProcessingLatencyMs_RecordedOnTheExhaustedRetryDlqPathToo()
    {
        var enteredAt = DateTimeOffset.Parse("2026-08-26T09:00:00.0000000+00:00");
        var exitedAt = enteredAt.AddMilliseconds(5750);
        var clock = new FakeClock(enteredAt);
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var options = new FactRetryOptions { MaxAttempts = 1, BackoffMs = 0 };
        var dispatcher = new FactRetryDispatcher(
            clock,
            delay,
            deadLetters,
            Options.Create(options),
            NullLogger<FactRetryDispatcher>.Instance);
        var process = new AdvanceOnceThenFailProcess(clock, exitedAt);

        using var meterListener = MetricCapture.ForInstrument("otc_fact_processing_latency_ms");
        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.Projector, process.InvokeAsync, CancellationToken.None);

        var measurement = Assert.Single(meterListener.Measurements);
        Assert.Equal(5750, measurement.Value); // 09:00:05.750 - 09:00:00.000.
        Assert.Single(deadLetters.Published);
    }

    /// <summary>Ledger L13 — a host shutdown is not a poison message.</summary>
    [Fact]
    public async Task OR1_ACancelledTokenIsRethrownImmediately_NeitherRetriedNorDeadLettered()
    {
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = BuildDispatcher(delay, deadLetters);
        var process = new AlwaysCancellingProcess();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None));

        Assert.Equal(1, process.Invocations);
        Assert.Empty(delay.Delays);
        Assert.Empty(deadLetters.Published);
    }

    /// <summary>
    /// Ledger L19 — <c>x-attempts</c> (here, <see cref="DeadLetterPublication.Attempts"/>)
    /// is written from the loop's OWN counter, never a fresh read of
    /// <c>options.Value.MaxAttempts</c> at the end. Proven by a fake that
    /// MUTATES the shared, mutable <see cref="FactRetryOptions"/> instance
    /// mid-loop (on its second invocation) — the loop's own bound was
    /// already captured before that mutation, so it still runs three times
    /// and the CORRECT implementation still reports 3; an implementation
    /// that re-reads <c>options.Value.MaxAttempts</c> at the end would
    /// instead report the fake's mutated value (1), diverging from the
    /// three real invocations.
    /// </summary>
    [Fact]
    public async Task OR1_XAttemptsIsWrittenFromTheLoopsOwnCounter_NotFromMaxAttemptsReadAtTheEnd()
    {
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var options = new FactRetryOptions { MaxAttempts = 3, BackoffMs = 500 };
        var dispatcher = BuildDispatcher(delay, deadLetters, options);

        // Mutates `options` (the SAME shared instance) to MaxAttempts = 1
        // on its second invocation — AFTER the dispatcher has already
        // captured its own loop bound, so this can only affect a
        // re-read, never the loop's own count.
        var process = new MutatingCountingProcess(mutateOnAttempt: 2, mutate: () => options.MaxAttempts = 1);

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(3, process.Invocations); // the REAL, observed count.
        var published = Assert.Single(deadLetters.Published);
        Assert.Equal(3, published.Attempts); // must equal the observed count, not the mutated options.MaxAttempts (1).
    }

    /// <summary>
    /// D6 (review round 3) — the three <c>KafkaDeadLetterPublisherTests</c>
    /// cases (one per service) SUPPLY <c>FirstFailedAt</c> as a publication
    /// input; none proves the DISPATCHER itself captures the FIRST
    /// failure's own clock reading rather than the LAST one. SA-3
    /// (backlog id 75) sharpens this further: <c>x-first-failed-at</c> is
    /// the instant the FIRST processing attempt FAILED, never the instant
    /// processing BEGAN
    /// (<c>enteredAt</c>/<paramref name="cancellationToken"/>-scoped entry
    /// read at the top of <c>DispatchAsync</c>) — a distinction the
    /// dispatcher's own entry read and its first catch-block read could
    /// otherwise share the SAME clock value and make indistinguishable.
    /// So the fake here advances the clock DURING attempt 1, before it
    /// throws — <c>enteredAt</c> and attempt 1's own failure instant are
    /// therefore two DIFFERENT values (t0 != t1), and the clock keeps
    /// advancing on every subsequent attempt (t1, t2, t3) so the LAST
    /// attempt's reading (t3) is distinct again. <c>FirstFailedAt</c> must
    /// be t1 — the first attempt's OWN reading, captured by
    /// <c>firstFailedAt ??= clock.UtcNow</c> inside the catch block, never
    /// the entry read (t0) and never a later one (a plain <c>=</c> would
    /// re-stamp it on every attempt and report t3 instead) — while
    /// <c>FailedAt</c> must be t3, the LAST attempt's reading.
    /// </summary>
    [Fact]
    public async Task DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsFailureInstant_NeverTheEntryInstantNorALaterOne()
    {
        var t0 = DateTimeOffset.Parse("2026-09-11T10:00:00.0000000+00:00"); // entry — must NOT surface as FirstFailedAt.
        var t1 = t0.AddSeconds(1); // attempt 1's own failure instant — the expected FirstFailedAt.
        var t2 = t0.AddSeconds(2);
        var t3 = t0.AddSeconds(3); // attempt 3's own failure instant — the expected FailedAt.
        var clock = new FakeClock(t0);
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = new FactRetryDispatcher(
            clock,
            delay,
            deadLetters,
            Options.Create(new FactRetryOptions { MaxAttempts = 3, BackoffMs = 500 }),
            NullLogger<FactRetryDispatcher>.Instance);
        var process = new AdvancingClockFailingProcess(clock, [t1, t2, t3]);

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(3, process.Invocations);
        var published = Assert.Single(deadLetters.Published);
        Assert.Equal(t1, published.FirstFailedAt); // NOT t0 (entry) and NOT t3 (last attempt).
        Assert.Equal(t3, published.FailedAt);
    }

    /// <summary>
    /// SA-3's second clause: "[<c>x-first-failed-at</c>] equals
    /// <c>x-failed-at</c> only when a single attempt was made." With
    /// <c>MaxAttempts = 1</c> the loop runs exactly once, so the catch
    /// block's <c>firstFailedAt ??= clock.UtcNow</c> reading and the
    /// post-loop <c>failedAt</c> reading are necessarily the SAME clock
    /// call's instant — proven here by advancing the clock during that
    /// one attempt so BOTH fields must equal the advanced instant, not the
    /// entry instant.
    /// </summary>
    [Fact]
    public async Task DeadLetterPublication_FirstFailedAt_EqualsFailedAt_WhenOnlyOneAttemptWasMade()
    {
        var enteredAt = DateTimeOffset.Parse("2026-09-11T10:00:00.0000000+00:00");
        var failureAt = enteredAt.AddSeconds(1);
        var clock = new FakeClock(enteredAt);
        var delay = new RecordingFactRetryDelay();
        var deadLetters = new RecordingDeadLetterPublisher();
        var dispatcher = new FactRetryDispatcher(
            clock,
            delay,
            deadLetters,
            Options.Create(new FactRetryOptions { MaxAttempts = 1, BackoffMs = 500 }),
            NullLogger<FactRetryDispatcher>.Instance);
        var process = new AdvancingClockFailingProcess(clock, [failureAt]);

        await dispatcher.DispatchAsync(SourceTopic, BuildMessage(), _eventId, EventType, _correlationId, ConsumerName.OrdersSaga, process.InvokeAsync, CancellationToken.None);

        Assert.Equal(1, process.Invocations);
        var published = Assert.Single(deadLetters.Published);
        Assert.Equal(failureAt, published.FirstFailedAt);
        Assert.Equal(failureAt, published.FailedAt);
        Assert.Equal(published.FailedAt, published.FirstFailedAt);
    }

    /// <summary>
    /// Advances the clock to <paramref name="advanceOnEachAttempt"/>'s
    /// entry for the CURRENT invocation number BEFORE throwing — so every
    /// attempt, including the first, moves the clock away from the entry
    /// reading before its own failure is caught. This is what actually
    /// discriminates "the entry instant" from "the first attempt's own
    /// failure instant": a fake that leaves attempt 1 at the constructor's
    /// clock value (as an earlier draft of this fixture did) makes the two
    /// indistinguishable, because both reads then observe the same t0.
    /// </summary>
    private sealed class AdvancingClockFailingProcess(FakeClock clock, IReadOnlyList<DateTimeOffset> advanceOnEachAttempt)
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            clock.UtcNow = advanceOnEachAttempt[Invocations - 1];
            throw new InvalidOperationException($"simulated failure #{Invocations}");
        }
    }

    private sealed class RecordingFactRetryDelay : IFactRetryDelay
    {
        public List<int> Delays { get; } = [];

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            Delays.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterPublication> Published { get; } = [];

        public Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken)
        {
            Published.Add(publication);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingFailingProcess
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            throw new InvalidOperationException($"simulated failure #{Invocations}");
        }
    }

    private sealed class SucceedAfterProcess(int succeedOnAttempt, Action? onInvoke = null)
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            onInvoke?.Invoke();
            if (Invocations < succeedOnAttempt)
            {
                throw new InvalidOperationException($"simulated failure #{Invocations}");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// D8 (review round 3) — advances the fake clock to <paramref name="advanceTo"/>
    /// INSIDE the single invocation, before throwing, so every clock read
    /// the dispatcher takes AFTER this point (the catch block's
    /// <c>firstFailedAt</c>, the post-loop <c>failedAt</c>, and the
    /// histogram's own exit read) observes the SAME advanced instant —
    /// exactly what a real wall clock would do between entry and exit of
    /// one failing attempt.
    /// </summary>
    private sealed class AdvanceOnceThenFailProcess(FakeClock clock, DateTimeOffset advanceTo)
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            clock.UtcNow = advanceTo;
            throw new InvalidOperationException($"simulated failure #{Invocations}");
        }
    }

    private sealed class AlwaysCancellingProcess
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            throw new OperationCanceledException("simulated shutdown");
        }
    }

    private sealed class MutatingCountingProcess(int mutateOnAttempt, Action mutate)
    {
        public int Invocations { get; private set; }

        public Task InvokeAsync(CancellationToken cancellationToken)
        {
            Invocations++;
            if (Invocations == mutateOnAttempt)
            {
                mutate();
            }

            throw new InvalidOperationException($"simulated failure #{Invocations}");
        }
    }
}
