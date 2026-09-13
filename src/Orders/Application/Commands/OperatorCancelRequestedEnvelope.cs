using OrderToCash.Contracts.Envelopes;
using OrderToCash.Orders.Application.Ports;
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
/// Builds the synthetic envelope <see cref="CancelOrderCommandHandler.BeginStockReleaseCompensationAsync"/>
/// stores — SA-4 made this the ONE direct enqueue site (both the
/// <c>stock_reserved</c> and the <c>credit_approved</c>/<c>confirmed</c>
/// branch call it; the credit-first branch's own separate enqueue,
/// <c>BeginCreditReleaseCompensationAsync</c>, was retired with it), so
/// there is no longer a second call site to share this shape with — #7's
/// own single <c>buildTriggeringEnvelope</c> was already shared by both of
/// ITS branches, which is the precedent this now matches exactly, not
/// merely mirrors.
/// </summary>
public static class OperatorCancelRequestedEnvelope
{
    public const string EventType = "orders.cancel.requested";

    public static byte[] Build(UniqueId orderId, UniqueId requestId, DateTimeOffset occurredAt, string? note, IRpcRequestSerializer serializer)
    {
        var envelope = new Envelope<OperatorCancelRequestedPayload>(
            requestId.Value,
            EventType,
            orderId.Value,
            orderId.Value,
            requestId.Value,
            occurredAt,
            new OperatorCancelRequestedPayload(orderId.Value, "operator_cancelled", note));

        return serializer.Serialize(envelope);
    }

    /// <summary>
    /// The topic the built envelope is stored against — #7's own
    /// <c>ordersFactsTopic</c> (<c>cancel-order.handler.ts:89</c>, <c>:176</c>,
    /// <c>:235</c>), which #7 injects into its handler's constructor rather
    /// than importing a producer-side constant. A DELIBERATE second literal
    /// copy of <c>"otc.orders.facts.v1"</c> — review round 1's A1: this
    /// Application-layer file must not reference
    /// <c>Infrastructure.Outbox.OrdersFactTopic</c> (a NEW Application →
    /// Infrastructure edge). Feature 76
    /// (<c>application_layer_depends_on_infrastructure_unguarded</c>) closed
    /// the four Application → <c>Infrastructure.Messaging.Rpc</c> references
    /// this comment used to name (this file's own <c>using</c> for
    /// <c>RpcJson</c> among them, alongside <c>ISagaCommands.cs</c>,
    /// <c>SagaCommandRequestFactory.cs</c> and
    /// <c>CancelOrderCommandHandler.cs</c>): the RPC payload records moved to
    /// <c>Contracts/Rpc</c>, and this file's own serialisation now goes
    /// through <see cref="IRpcRequestSerializer"/> rather than
    /// <c>RpcJson</c> directly. Guarded against
    /// drift by <c>OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName</c>
    /// (a parity assertion, in the TEST file, which may reference
    /// Infrastructure freely).
    /// </summary>
    public const string Topic = "otc.orders.facts.v1";
}
