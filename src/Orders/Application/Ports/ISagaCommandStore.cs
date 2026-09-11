using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>Whether an enqueue actually inserted a new row, or found the command already owed/sent (design.md §6.3 — a duplicate-key hit on the unique <c>(order_id, command)</c> index).</summary>
public enum EnqueueOutcome
{
    Enqueued,
    AlreadyEnqueued,
}

/// <summary>
/// The durable row's shape a claim hands the caller — everything
/// <see cref="ISagaCommands"/> and the retry policy need, with no second
/// read. <see cref="TriggeringEventEnvelope"/>/<see cref="TriggeringEventTopic"/>
/// (feature <c>observability_reliability</c>, design.md §4.2) are the raw
/// bytes and source topic of the fact that owed this command, captured
/// verbatim at enqueue time — <see langword="null"/> for a row enqueued
/// before this feature's columns existed, or for any other enqueue site
/// that genuinely supplies no envelope (none exists today).
/// <c>CancelOrderCommandHandler</c>'s operator-cancel compensation rows are
/// NOT such a row: since feature
/// <c>operator_note_survives_the_compensation_branches</c> (id 71) both of
/// its enqueue sites carry the synthetic <c>orders.cancel.requested</c>
/// envelope, and a parked row of theirs is republished on first park
/// exactly like any other row — see <see cref="FindOperatorCancelNoteAsync"/>'s
/// own remarks for the write.
/// Defaulted here (not on <see cref="ISagaCommandStore.EnqueueAsync"/>,
/// which forces every caller to be explicit) so callers that do not care
/// about them need not be touched by this addition.
/// </summary>
public sealed record SagaCommandRecord(
    Guid Id,
    Guid OrderId,
    string OrderReference,
    SagaCommandKind Command,
    string Payload,
    Guid TriggeringEventId,
    int Attempts,
    byte[]? TriggeringEventEnvelope = null,
    string? TriggeringEventTopic = null);

/// <summary>
/// The durable <c>saga_commands</c> queue (design.md §6.3) — enqueue, claim,
/// claim-due, mark-sent, park. No <c>tx</c> parameter anywhere: the ambient
/// transaction comes from the caller's DI scope (feature 14, §2.1), exactly
/// as <see cref="IOrderRepository"/> already does.
/// </summary>
public interface ISagaCommandStore
{
    /// <summary>
    /// Inserts a <c>pending</c> row inside the caller's ambient transaction. A
    /// duplicate-key hit on <c>(order_id, command)</c> is not an error — it
    /// means the command is already owed or already sent (SO3).
    /// </summary>
    /// <param name="triggeringEventEnvelope">
    /// Feature <c>observability_reliability</c>, <c>OR3</c>/<c>R29</c>'s
    /// dead-letter clause (design.md §4.2) — the RAW bytes of the fact that
    /// owed this command, captured verbatim (never re-serialised), for a
    /// fact-triggered enqueue. <c>CancelOrderCommandHandler</c>'s
    /// operator-cancel compensation rows are RPC-triggered, not
    /// fact-triggered, and MUST supply the synthetic
    /// <c>orders.cancel.requested</c> envelope
    /// (<c>OperatorCancelRequestedEnvelope.Build</c>) here — passing
    /// <see langword="null"/> for those two sites would silently lose both
    /// the DLQ parity this port exists for and the operator's note.
    /// This parameter stays nullable only because
    /// <see cref="SagaFact.TriggeringEventEnvelope"/> is itself a
    /// defaulted <c>byte[]?</c>, threaded straight through
    /// (<c>SagaFactHandler.cs</c>), so fixtures that do not care about
    /// dead-lettering are not forced to supply one. #7's own port makes
    /// this parameter non-nullable at compile time
    /// (<c>saga-command-store.port.ts:40</c>, <c>:51</c>) — #8 does not
    /// carry that compile-time guarantee over for any future caller, only
    /// per-site discipline and tests for the two sites that exist today.
    /// </param>
    /// <param name="triggeringEventTopic">The source topic <paramref name="triggeringEventEnvelope"/> was consumed from — one of <c>SagaFactTopics</c>'s three (<c>Infrastructure/Messaging/Consumers/</c>), never re-derived from <paramref name="command"/> or the order. <see langword="null"/> alongside a <see langword="null"/> envelope.</param>
    Task<EnqueueOutcome> EnqueueAsync(
        Guid orderId,
        string orderReference,
        SagaCommandKind command,
        string payload,
        Guid triggeringEventId,
        byte[]? triggeringEventEnvelope,
        string? triggeringEventTopic,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims exactly one row by <c>(orderId, command)</c> for dispatch,
    /// under a bounded lease (SO11) — a conditional <c>UPDATE</c> that
    /// affects at most one row. Returns <see langword="null"/> when it
    /// affects zero rows: a stale signal, a row already <c>sent</c>, or one
    /// currently held by a concurrent claimant/the sweeper — a SILENT no-op
    /// (design.md §6.2), never an error.
    /// </summary>
    Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken);

    /// <summary>The sweeper's batch claim (design.md §6.4): every stale <c>pending</c> row (SO3's crash window) and every due <c>parked</c> row (SO5), up to <paramref name="batchSize"/>, each under the same bounded lease as <see cref="TryClaimAsync"/>.</summary>
    Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Marks a claimed row <c>sent</c> — a reply was delivered, including a business rejection (SO6). Never means "the saga advanced".</summary>
    Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed row <c>parked</c> on exhaustion (SO5): accumulates
    /// <see cref="SagaCommandRecord.Attempts"/>, records the last error, and
    /// schedules the next retry on capped backoff. A conditional
    /// <c>UPDATE ... WHERE status &lt;&gt; 'sent'</c> — mirrors #7's own
    /// <c>notAlreadySent()</c> guard
    /// (<c>drizzle-saga-command-store.ts:116-124</c>) — so a row a
    /// concurrent dispatcher has already reported <c>sent</c> is never
    /// overwritten <c>parked</c>. Returns <see langword="true"/> iff THIS
    /// call actually performed the transition; <c>SagaCommandDispatcher</c>'s
    /// first-park hook (<c>OR3</c>) runs only when this is
    /// <see langword="true"/>.
    /// </summary>
    Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a claimed row <c>rejected</c> — the TERMINAL end state for a
    /// command whose responder replied with a business-rejection
    /// <c>RpcError</c> (feature 42, <see cref="SagaCommandBusinessRejectionError"/>).
    /// Retrying it can never succeed, so unlike <see cref="ParkAsync"/> there
    /// is no <c>next_attempt_at</c> to schedule — accumulates
    /// <see cref="SagaCommandRecord.Attempts"/> the same way and records the
    /// last error, but the row is never claimed again:
    /// <see cref="ClaimDueAsync"/>'s predicate matches only
    /// <c>pending</c>/<c>parked</c>, so a <c>rejected</c> row is structurally
    /// excluded from every future sweep with no extra guard needed there.
    /// </summary>
    Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken);

    /// <summary>
    /// <c>OR3</c>'s "at most once per row" claim (design.md §4.3) — ONE
    /// conditional <c>UPDATE ... WHERE id = @commandId AND dead_lettered_at
    /// IS NULL</c>, whose affected-row count is the answer. Never a
    /// <c>SELECT</c> then an <c>INSERT</c>/<c>UPDATE</c> — <c>CLAUDE.md</c>
    /// records a shipped defect of exactly that check-then-act shape.
    /// Returns <see langword="true"/> iff THIS caller won the claim (the row
    /// was not already dead-lettered); a losing caller (a later sweep cycle
    /// re-parking the same row) gets <see langword="false"/> and must not
    /// append a second <c>order.saga_failed.v1</c> or a second <c>.dlq</c>
    /// publish.
    /// </summary>
    Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken);

    /// <summary>
    /// Feature <c>operator_note_survives_the_compensation_branches</c> (id
    /// 71) — a bare, read-only lookup for the operator's cancellation note,
    /// never a claim and never a mutation. Reuses the SAME column R29's
    /// dead-letter clause already populates
    /// (<see cref="SagaCommandRecord.TriggeringEventEnvelope"/>).
    /// <c>triggering_event_envelope</c> is written EXACTLY ONCE, by
    /// <see cref="EnqueueAsync"/>'s own <c>INSERT</c> — a duplicate
    /// <c>(order_id, command)</c> enqueue leaves the existing row's envelope
    /// untouched (<see cref="EnqueueOutcome.AlreadyEnqueued"/>). No code
    /// path anywhere rewrites an envelope once inserted.
    /// <see cref="OrderToCash.Orders.Application.Commands.CancelOrderCommandHandler"/>'s
    /// <c>credit_approved</c>/<c>confirmed</c> branch inserts
    /// <c>credit.release</c>'s row directly, carrying the synthetic
    /// <c>orders.cancel.requested</c> envelope (and the note, when
    /// supplied); the same branch's <c>stock.release</c> row does not exist
    /// yet at that point and is inserted LATER, once <c>credit.released.v1</c>
    /// arrives, carrying THAT real fact's own bytes (no note) — a NEW row,
    /// never a rewrite of an old one. This checks <c>credit.release</c>
    /// FIRST, then <c>stock.release</c> — the <c>stock_reserved</c> branch's
    /// own (and only) note-bearing row — and returns the first envelope
    /// whose <c>eventType</c> is the synthetic <c>orders.cancel.requested</c>
    /// one, so a <c>credit.release</c> row that DOES carry a synthetic
    /// envelope always wins over a <c>stock.release</c> row that also does
    /// (the ordinary case has at most one; both is a race outside today's
    /// known reachable paths, guarded defensively rather than left to
    /// coincidence). <see langword="null"/> when neither row exists, neither
    /// carries the synthetic envelope (a saga-decided
    /// <c>stock_rejected</c>/<c>credit_rejected</c> cancellation), or the
    /// operator supplied no note.
    /// </summary>
    Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Feature <c>operator_cancel_races_saga_forward_progress</c> (id 62) —
    /// a bare, read-only existence check: does ANY <c>credit.release</c> or
    /// <c>stock.release</c> row already exist for <paramref name="orderId"/>,
    /// regardless of status (<c>pending</c>, <c>parked</c>, <c>sent</c> —
    /// even <c>rejected</c>)? Existence alone is the signal
    /// <see cref="Sagas.SagaFactHandler"/> needs: once EITHER row has been
    /// enqueued, an operator cancel (or a fact-driven rejection — this check
    /// does not distinguish the two, unlike <see cref="FindOperatorCancelNoteAsync"/>)
    /// is under way for this order, and no UNRELATED forward-progress
    /// <c>Advance</c> step may apply on top of it — see
    /// <see cref="Application.Sagas.SagaFactHandler.HandleAsync"/>'s own
    /// remarks for why <c>credit.released.v1</c> itself is exempt from this
    /// check (its own <c>Advance</c> variants ARE the compensation, never a
    /// competitor to it). Never a claim, never a mutation — safe to call
    /// from a read-only ambient transaction.
    /// </summary>
    Task<bool> HasPendingCompensationAsync(Guid orderId, CancellationToken cancellationToken);
}
