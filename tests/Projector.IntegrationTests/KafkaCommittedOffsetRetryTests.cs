using System.Diagnostics;
using Confluent.Kafka;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// Backlog id 69, bullet 5 — the deterministic proof for
/// <see cref="KafkaCommittedOffsetRetry"/>, by a change of KIND rather than
/// of probability: a coordinator withheld for a CONTROLLED interval longer
/// than the retired five-attempt budget makes that budget lose EVERY time and
/// the wall-clock deadline win EVERY time.
///
/// <para>The withholding is a real <see cref="Stopwatch"/> against real wall
/// clock — the property under test IS wall-clock, so faking the clock would
/// test the fake. What is NOT real here is the broker: reproducing a Kafka
/// coordinator that stays unloaded for a controlled interval is not something
/// a Testcontainers broker can be asked for, so the probe delegate stands in
/// for <c>consumer.Committed(...)</c> and throws the exact
/// <see cref="ErrorCode.NotCoordinatorForGroup"/> the SA-3 verification run
/// observed. That is the substitution this proof makes, stated rather than
/// hidden: the policy is exercised for real, the broker is not.</para>
///
/// <para>Starts no container — it is in this project because the code it
/// guards is, and it has no collection, so no collection fixture is
/// constructed for it.</para>
/// </summary>
public sealed class KafkaCommittedOffsetRetryTests
{
    /// <summary>The retired shape: five attempts, four 300 ms sleeps between them — 1.2 s of wall-clock, whatever the broker does.</summary>
    private const int RetiredAttemptBudget = 5;

    private static readonly TimeSpan _retiredSleep = TimeSpan.FromMilliseconds(300);

    /// <summary>Comfortably longer than the retired budget's 1.2 s, so the difference below is structural rather than marginal.</summary>
    private static readonly TimeSpan _coordinatorWithheldFor = TimeSpan.FromSeconds(2);

    /// <summary>"Every time" is a claim about repetition, so each race runs this many times and the assertion is over ALL of them.</summary>
    private const int Runs = 3;

    [Fact]
    public void TheRetiredFiveAttemptBudget_LosesToACoordinatorWithheldForLongerThanIt_EveryTime()
    {
        for (var run = 1; run <= Runs; run++)
        {
            var probe = WithholdingCoordinator(_coordinatorWithheldFor);
            var stopwatch = Stopwatch.StartNew();

            var failure = Record.Exception(() => RetiredAttemptCountedRead(probe.Read));

            stopwatch.Stop();

            Assert.True(
                failure is KafkaException,
                $"run {run}/{Runs}: the RETIRED five-attempt budget survived a coordinator withheld for {_coordinatorWithheldFor.TotalSeconds:F1}s, in " +
                $"{stopwatch.Elapsed.TotalSeconds:F1}s and {probe.Attempts} attempt(s). This test is the control for the wall-clock deadline below — if the " +
                $"attempt-counted shape can win this race, the two are not being separated by a difference of KIND and the deadline test proves nothing. " +
                $"Observed outcome: {failure?.ToString() ?? "no exception"}.");

            Assert.True(
                stopwatch.Elapsed < _coordinatorWithheldFor,
                $"run {run}/{Runs}: the RETIRED budget did lose, but only after {stopwatch.Elapsed.TotalSeconds:F1}s — longer than the " +
                $"{_coordinatorWithheldFor.TotalSeconds:F1}s the coordinator was withheld for, so it lost for some reason OTHER than running out of " +
                $"wall-clock early. A loss for the wrong reason is not evidence (backlog id 69, acceptance bullet 3).");
        }
    }

    [Fact]
    public void TheWallClockDeadline_AbsorbsTheSameWithheldCoordinator_EveryTime()
    {
        for (var run = 1; run <= Runs; run++)
        {
            var probe = WithholdingCoordinator(_coordinatorWithheldFor);
            var stopwatch = Stopwatch.StartNew();

            var failure = Record.Exception(() =>
                Assert.Equal(42, KafkaCommittedOffsetRetry.Read(probe.Read, "the committed offset for group 'projector'")));

            stopwatch.Stop();

            Assert.True(
                failure is null,
                $"run {run}/{Runs}: KafkaCommittedOffsetRetry.Read gave up on a coordinator withheld for only {_coordinatorWithheldFor.TotalSeconds:F1}s, after " +
                $"{stopwatch.Elapsed.TotalSeconds:F1}s and {probe.Attempts} attempt(s), against its own " +
                $"{KafkaCommittedOffsetRetry.CoordinatorReadyBudget.TotalSeconds:F0}s WALL-CLOCK budget. A budget counted in attempts expires in about a second " +
                $"against an error the broker returns immediately; this one must not. Failure: {failure}.");

            Assert.True(
                stopwatch.Elapsed >= _coordinatorWithheldFor,
                $"run {run}/{Runs}: KafkaCommittedOffsetRetry.Read returned after only {stopwatch.Elapsed.TotalSeconds:F2}s, before the coordinator it was " +
                $"waiting for became ready at {_coordinatorWithheldFor.TotalSeconds:F1}s. It cannot have absorbed the wait by pacing, so this run proves " +
                "nothing about pacing.");
        }
    }

    [Fact]
    public void AGenuineFailure_IsNotRetried_AndSurfacesOnTheFirstAttempt()
    {
        var attempts = 0;
        var genuine = new KafkaException(new Error(ErrorCode.UnknownTopicOrPart, "Broker: Unknown topic or partition"));

        var stopwatch = Stopwatch.StartNew();
        var thrown = Assert.Throws<KafkaException>(() => KafkaCommittedOffsetRetry.Read<int>(
            () =>
            {
                attempts++;
                throw genuine;
            },
            "the committed offset for group 'projector'"));
        stopwatch.Stop();

        Assert.True(
            ReferenceEquals(genuine, thrown) && attempts == 1,
            $"a non-coordinator KafkaException ({genuine.Error.Code}) was retried {attempts} time(s) over {stopwatch.Elapsed.TotalMilliseconds:F0} ms, or was " +
            "not the exception that surfaced. The retired shape caught EVERY KafkaException, so a genuine failure — an unknown topic, a closed handle, an " +
            "auth error — was retried four times and then reported as the LAST attempt's exception, 1.2 s late and wearing the disguise of a transient. " +
            $"Only the three coordinator codes may be retried. Surfaced: {thrown.Error.Code} — {thrown.Error.Reason}.");
    }

    [Fact]
    public void WhenTheCoordinatorNeverBecomesReady_TheTimeoutNamesTheBudgetTheAttemptsAndTheLastBrokerError()
    {
        var attempts = 0;

        var thrown = Assert.Throws<TimeoutException>(() => KafkaCommittedOffsetRetry.Read<int>(
            () =>
            {
                attempts++;
                throw new KafkaException(new Error(ErrorCode.NotCoordinatorForGroup, "Broker: Not coordinator"));
            },
            "the committed offset for group 'projector' on 'orders.facts'",
            budget: TimeSpan.FromMilliseconds(900),
            interval: TimeSpan.FromMilliseconds(100)));

        Assert.Contains("the committed offset for group 'projector' on 'orders.facts'", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("NotCoordinatorForGroup", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("Broker: Not coordinator", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("wall-clock budget of 0.9s", thrown.Message, StringComparison.Ordinal);
        Assert.IsType<KafkaException>(thrown.InnerException);
        Assert.True(
            attempts >= 2,
            $"the budget was exhausted after only {attempts} attempt(s) over a 900 ms budget paced 100 ms apart — the loop is not retrying at all, so its " +
            "message would be naming a budget it never actually spent.");
    }

    /// <summary>
    /// A stand-in for <c>consumer.Committed(...)</c> against a broker whose
    /// group coordinator is still loading: it throws the exact error code the
    /// SA-3 verification run observed until <paramref name="withheldFor"/> of
    /// real wall-clock has passed, then answers.
    /// </summary>
    private static WithholdingCoordinatorProbe WithholdingCoordinator(TimeSpan withheldFor) => new(withheldFor);

    private sealed class WithholdingCoordinatorProbe(TimeSpan withheldFor)
    {
        private readonly Stopwatch _sinceConstruction = Stopwatch.StartNew();

        public int Attempts { get; private set; }

        public int Read()
        {
            Attempts++;

            return _sinceConstruction.Elapsed < withheldFor
                ? throw new KafkaException(new Error(ErrorCode.NotCoordinatorForGroup, "Broker: Not coordinator"))
                : 42;
        }
    }

    /// <summary>
    /// A faithful replica of the shape <see cref="KafkaCommittedOffsetRetry"/>
    /// replaced — five attempts, a 300 ms sleep between them, catching every
    /// <see cref="KafkaException"/>, the fifth attempt's exception propagating.
    /// It is the control: it makes the difference this class proves a
    /// difference of KIND, without mutating the real policy to get it.
    /// </summary>
    private static int RetiredAttemptCountedRead(Func<int> read)
    {
        var result = 0;

        for (var attempt = 1; attempt <= RetiredAttemptBudget; attempt++)
        {
            try
            {
                result = read();
                break;
            }
            catch (KafkaException) when (attempt < RetiredAttemptBudget)
            {
                Thread.Sleep(_retiredSleep);
            }
        }

        return result;
    }
}
