using OrderToCash.Contracts.Envelopes;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The synthetic <c>orders.cancel.requested</c> envelope's payload shape —
/// feature <c>operator_note_survives_the_compensation_branches</c> (id 71),
/// porting #7's <c>cancel-order.handler.ts:175</c>/<c>:234</c>, which build
/// it via <c>buildTriggeringEnvelope</c> (<c>:272-286</c>), verbatim.
/// Deliberately NOT one of <c>Contracts.Facts.FactCatalog</c>'s fourteen
/// real fact types — it is never published on the real wire, only stored in
/// <c>saga_commands.triggering_event_envelope</c> (the column itself is
/// <c>specs/observability_reliability/design.md</c> §4.1, not §4.2 — §4.2
/// is fact-triggered threading through <c>SagaFactsConsumer</c>/
/// <c>SagaFactHandler</c>, which this RPC-triggered enqueue is not) and,
/// when the row parks, republished verbatim to <c>&lt;ordersFactsTopic&gt;.dlq</c>
/// — diagnostic bookkeeping for "why was this enqueued", truthfully tagged
/// as an operator request, never as a fact that did not happen. R29 does
/// not require this envelope (an operator cancel has no triggering fact in
/// R29's sense); it is #7's own named convention, ported for parity.
/// </summary>
public sealed record OperatorCancelRequestedPayload(Guid OrderId, string Reason, string? Note);

/// <summary>
/// Builds the synthetic envelope both operator-cancel compensation enqueue
/// sites (<see cref="CancelOrderCommandHandler.BeginCreditReleaseCompensationAsync"/>,
/// <see cref="CancelOrderCommandHandler.BeginStockReleaseCompensationAsync"/>)
/// share — the SAME diagnostic shape either way, exactly as #7's single
/// <c>buildTriggeringEnvelope</c> is shared by both of its own branches.
/// </summary>
public static class OperatorCancelRequestedEnvelope
{
    public const string EventType = "orders.cancel.requested";

    public static byte[] Build(UniqueId orderId, UniqueId requestId, DateTimeOffset occurredAt, string? note)
    {
        var envelope = new Envelope<OperatorCancelRequestedPayload>(
            requestId.Value,
            EventType,
            orderId.Value,
            orderId.Value,
            requestId.Value,
            occurredAt,
            new OperatorCancelRequestedPayload(orderId.Value, "operator_cancelled", note));

        return RpcJson.Serialize(envelope);
    }

    /// <summary>
    /// The topic the built envelope is stored against — #7's own
    /// <c>ordersFactsTopic</c> (<c>cancel-order.handler.ts:89</c>, <c>:176</c>,
    /// <c>:235</c>), which #7 injects into its handler's constructor rather
    /// than importing a producer-side constant. A DELIBERATE third literal
    /// copy of <c>"otc.orders.facts.v1"</c> — review round 1's A1: this
    /// Application-layer file must not reference
    /// <c>Infrastructure.Outbox.OrdersFactTopic</c> (a NEW Application →
    /// Infrastructure edge). Application → <c>Infrastructure.Messaging.Rpc</c>
    /// has FOUR references, this file's own <c>using</c> (for
    /// <c>RpcJson</c>, below) among them, alongside <c>ISagaCommands.cs</c>,
    /// <c>SagaCommandRequestFactory.cs</c> and
    /// <c>CancelOrderCommandHandler.cs</c> — corrected from this comment's
    /// own earlier "three pre-existing", which miscounted by not counting
    /// this file's own import. All four are routed to backlog id 76
    /// (<c>application_layer_depends_on_infrastructure_unguarded</c>), not
    /// refactored here. Guarded against
    /// drift by <c>OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName</c>
    /// (a parity assertion, in the TEST file, which may reference
    /// Infrastructure freely).
    /// </summary>
    public const string Topic = "otc.orders.facts.v1";
}
