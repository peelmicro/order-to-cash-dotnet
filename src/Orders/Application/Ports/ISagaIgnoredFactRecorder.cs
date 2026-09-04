using OrderToCash.Orders.Domain;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>The two reasons a fact was deliberately ignored (design.md §5.4) — the <c>saga_ignored_facts.marker</c> column's closed set.</summary>
public enum SagaIgnoredFactMarker
{
    /// <summary>R25 — the order exists, but its status did not match the step's precondition.</summary>
    PreconditionUnmet,

    /// <summary>SO8 — the fact's <c>correlationId</c> matches no order in the write model.</summary>
    UnknownOrder,
}

/// <summary>Maps <see cref="SagaIgnoredFactMarker"/> to its snake_case wire/storage token — the <c>saga_ignored_facts.marker</c> column value.</summary>
public static class SagaIgnoredFactMarkers
{
    public static string ToToken(SagaIgnoredFactMarker marker) => marker switch
    {
        SagaIgnoredFactMarker.PreconditionUnmet => "precondition_unmet",
        SagaIgnoredFactMarker.UnknownOrder => "unknown_order",
        _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, "Unrecognised SagaIgnoredFactMarker member."),
    };
}

/// <summary>One durably-recorded ignored fact (design.md §5.4). <see cref="ObservedStatus"/>/<see cref="ExpectedStatus"/> are populated only for <see cref="SagaIgnoredFactMarker.PreconditionUnmet"/>; both are <see langword="null"/> for <see cref="SagaIgnoredFactMarker.UnknownOrder"/>, where <see cref="OrderId"/> is also <see langword="null"/>.</summary>
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
