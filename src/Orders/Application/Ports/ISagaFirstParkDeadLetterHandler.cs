namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// <c>OR3</c>'s first-park hook — feature <c>observability_reliability</c>,
/// design.md §4.4. Invoked by <c>SagaCommandDispatcher</c> exactly once per
/// exhaustion cycle, and only after <see cref="ISagaCommandStore.ParkAsync"/>
/// reports it actually performed the <c>parked</c> transition (a
/// <see langword="false"/> means a racing dispatcher already reported the
/// row <c>sent</c> — dead-lettering a row that just turned out to have
/// succeeded would be wrong). The implementation's own responsibility is to
/// claim the row's dead-letter marker at-most-once
/// (<see cref="ISagaCommandStore.TryClaimDeadLetterAsync"/>) and, only on a
/// winning claim, append the order's <c>order.saga_failed.v1</c> fact and
/// republish the triggering envelope to its source topic's <c>.dlq</c> — a
/// later, losing claim (a re-park of the same row) must do neither.
/// </summary>
public interface ISagaFirstParkDeadLetterHandler
{
    /// <param name="claimed">The row's shape at claim time — carries the triggering envelope bytes/topic the <c>.dlq</c> republish needs, with no second read.</param>
    /// <param name="attempts">The row's TOTAL accumulated attempts after this park cycle (the caller's already-parked <c>attempts</c> plus this cycle's), carried into <c>order.saga_failed.v1</c>'s payload verbatim.</param>
    /// <param name="lastError">The final in-line attempt's error, carried into the fact and the <c>.dlq</c> headers.</param>
    Task HandleAsync(SagaCommandRecord claimed, int attempts, string lastError, CancellationToken cancellationToken);
}
