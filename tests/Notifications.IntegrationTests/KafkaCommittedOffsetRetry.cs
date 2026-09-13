using Confluent.Kafka;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Backlog id 69, bullet 5 — the retry policy every committed-offset probe in
/// this repository now shares.
///
/// <para>The shape it replaces counted its budget in ATTEMPTS: five attempts
/// with a 300 ms sleep between them, catching EVERY <see cref="KafkaException"/>.
/// Both halves were wrong for the failure it exists to absorb.</para>
///
/// <list type="number">
/// <item><b>A budget counted in attempts is only a budget in wall-clock if
/// every attempt costs wall-clock.</b> <c>IConsumer.Committed</c> issues a
/// FindCoordinator lookup on a freshly-built handle, and a broker that has
/// not finished loading the group answers <c>Broker: Not coordinator</c>
/// almost immediately rather than timing out — so the whole five-attempt
/// budget could expire in ~1.2 s of sleeps against a brand-new single-node
/// broker whose coordinator takes longer than that to load. Measured, not
/// supposed: <c>OffsetContractTests.PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker</c>
/// went red on exactly this in the SA-3 verification run (1 failed of 1833,
/// against an otherwise identical green run on the same tree). The budget is
/// therefore a WALL-CLOCK deadline here — #7's own shape at
/// <c>apps/projector/src/test-support/kafka-test-fixture.ts:93-126</c>
/// (<c>waitForConsumerGroupReady</c>: a 60 000 ms deadline, a 300 ms
/// interval, a loading coordinator treated as not-ready).</item>
/// <item><b>Catching every <see cref="KafkaException"/> retries genuine
/// failures too</b>, and then reports the LAST attempt's exception as though
/// it were the first — so an unknown topic, a closed handle or an auth
/// failure was retried four times and surfaced 1.2 s late, wearing the
/// disguise of a transient. Only the three GROUP-COORDINATOR codes are
/// retried here (<see cref="IsCoordinatorNotReady"/>); everything else
/// propagates on the first attempt, untouched.</item>
/// </list>
///
/// <para>Proved by a change of KIND, not of probability, in
/// <c>KafkaCommittedOffsetRetryTests</c>: against a coordinator withheld for
/// a controlled interval LONGER than the old attempt budget, the old shape
/// loses every time and this one wins every time.</para>
///
/// <para>Duplicated verbatim (namespace apart) in
/// <c>tests/Notifications.IntegrationTests</c> and
/// <c>tests/Projector.IntegrationTests</c>, which is this repository's
/// established stance for test support that four projects need and no shared
/// test project exists to hold — the same stance
/// <c>NotificationOffsetSupport</c>/<c>ProjectorOffsetSupport</c> already
/// record ("duplicated here rather than cross-project-referenced"). Each copy
/// carries its OWN copy of the deterministic proof, so no copy is guarded
/// only by a sibling's test.</para>
/// </summary>
internal static class KafkaCommittedOffsetRetry
{
    /// <summary>The wall-clock deadline a not-yet-ready group coordinator is given — #7's <c>waitForConsumerGroupReady</c> budget.</summary>
    internal static readonly TimeSpan CoordinatorReadyBudget = TimeSpan.FromSeconds(60);

    /// <summary>The pacing interval between retries — the same 300 ms the attempt-counted shape used, now applied against a deadline rather than a count.</summary>
    internal static readonly TimeSpan CoordinatorRetryInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The three broker error codes that mean "ask again, the group's
    /// coordinator is not ready yet" — never "this read is wrong". Everything
    /// else is a genuine failure and is not retried.
    /// </summary>
    internal static bool IsCoordinatorNotReady(KafkaException exception) =>
        exception.Error.Code is ErrorCode.NotCoordinatorForGroup
            or ErrorCode.GroupCoordinatorNotAvailable
            or ErrorCode.GroupLoadInProgress;

    /// <summary>
    /// Runs <paramref name="read"/> until it succeeds, until it fails with
    /// something other than a coordinator-not-ready error (which propagates
    /// immediately and untouched), or until <paramref name="budget"/> of
    /// WALL-CLOCK has elapsed — at which point a <see cref="TimeoutException"/>
    /// naming the budget, the attempts made and the last broker error is
    /// thrown, with the last <see cref="KafkaException"/> as its inner
    /// exception.
    /// </summary>
    /// <param name="read">The probe — <c>consumer.Committed(partitions, requestTimeout)</c> at every production call site.</param>
    /// <param name="description">What is being read, for the timeout message: a reader who sees this failure must not have to open the stack line to learn what it was about (backlog id 82).</param>
    /// <param name="budget">Defaults to <see cref="CoordinatorReadyBudget"/>. Parameterised so the deterministic proof can drive the SAME code with a controlled interval instead of waiting a real minute.</param>
    /// <param name="interval">Defaults to <see cref="CoordinatorRetryInterval"/>.</param>
    internal static T Read<T>(Func<T> read, string description, TimeSpan? budget = null, TimeSpan? interval = null)
    {
        var effectiveBudget = budget ?? CoordinatorReadyBudget;
        var effectiveInterval = interval ?? CoordinatorRetryInterval;
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 0;

        while (true)
        {
            attempts++;

            try
            {
                return read();
            }
            catch (KafkaException ex) when (IsCoordinatorNotReady(ex))
            {
                if (startedAt.Elapsed + effectiveInterval >= effectiveBudget)
                {
                    throw new TimeoutException(
                        $"{description}: the group coordinator was still not ready after {startedAt.Elapsed.TotalSeconds:F1}s and {attempts} attempt(s) paced " +
                        $"{effectiveInterval.TotalMilliseconds:F0} ms apart, against a wall-clock budget of {effectiveBudget.TotalSeconds:F1}s. " +
                        $"Last broker error: {ex.Error.Code} — {ex.Error.Reason}.",
                        ex);
                }

                Thread.Sleep(effectiveInterval);
            }
        }
    }
}
