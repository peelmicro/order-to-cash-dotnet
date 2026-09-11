using OrderToCash.Orders.Domain;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>The three reasons a fact was deliberately ignored (design.md §5.4, extended by feature <c>operator_cancel_races_saga_forward_progress</c>, id 62) — the <c>saga_ignored_facts.marker</c> column's closed set.</summary>
public enum SagaIgnoredFactMarker
{
    /// <summary>R25 — the order exists, but its status did not match the step's precondition.</summary>
    PreconditionUnmet,

    /// <summary>SO8 — the fact's <c>correlationId</c> matches no order in the write model.</summary>
    UnknownOrder,

    /// <summary>
    /// Id 62 — the order's status DID match this <c>Advance</c> step's
    /// precondition, but an operator-cancel compensation (<c>credit.release</c>
    /// or <c>stock.release</c>) is already enqueued for this order, so
    /// forward progress is superseded rather than applied: applying it would
    /// leave the compensation's own completion fact stranded (R25's
    /// equality-only check would later find the order has moved past the
    /// status it expects and correctly-but-harmfully ignore it — the exact
    /// mechanism #7's own <c>orders-cancel.integration.spec.ts</c> disclosed
    /// and never fixed). Never raised for <c>credit.released.v1</c>'s OWN
    /// two compensation-chain <c>Advance</c> variants (<see cref="SagaFactHandler"/>'s
    /// own guard excludes them by <c>eventType</c>) — those must always
    /// proceed, since they ARE the compensation.
    /// </summary>
    Superseded,
}

/// <summary>Maps <see cref="SagaIgnoredFactMarker"/> to its snake_case wire/storage token — the <c>saga_ignored_facts.marker</c> column value.</summary>
public static class SagaIgnoredFactMarkers
{
    public static string ToToken(SagaIgnoredFactMarker marker) => marker switch
    {
        SagaIgnoredFactMarker.PreconditionUnmet => "precondition_unmet",
        SagaIgnoredFactMarker.UnknownOrder => "unknown_order",
        SagaIgnoredFactMarker.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, "Unrecognised SagaIgnoredFactMarker member."),
    };
}

/// <summary>One durably-recorded ignored fact (design.md §5.4). <see cref="ObservedStatus"/> is populated for <see cref="SagaIgnoredFactMarker.PreconditionUnmet"/> and <see cref="SagaIgnoredFactMarker.Superseded"/>; <see cref="ExpectedStatus"/> only for <see cref="SagaIgnoredFactMarker.PreconditionUnmet"/> (a superseded fact's precondition DID match — there is no "expected, different" status to report). Both are <see langword="null"/> for <see cref="SagaIgnoredFactMarker.UnknownOrder"/>, where <see cref="OrderId"/> is also <see langword="null"/>.</summary>
public sealed record SagaIgnoredFactRecord(
    Guid EventId,
    string EventType,
    Guid? OrderId,
    Guid CorrelationId,
    SagaIgnoredFactMarker Marker,
    OrderStatus? ObservedStatus = null,
    OrderStatus? ExpectedStatus = null);

/// <summary>
/// The R25 + SO8 durable ignored-fact record, inserted through the
/// AMBIENT scoped <c>DbContext</c> — <see cref="ISagaCommandStore"/>'s own
/// no-<c>tx</c>-parameter shape. Written only inside a first-delivery
/// <c>RunOnceAsync</c> (design.md §5.4), so the write is itself idempotent
/// under the dedup layer.
/// </summary>
public interface ISagaIgnoredFactRecorder
{
    Task RecordAsync(SagaIgnoredFactRecord record, CancellationToken cancellationToken);
}
