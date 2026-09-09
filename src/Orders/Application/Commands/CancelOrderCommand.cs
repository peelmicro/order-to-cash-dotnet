using OrderToCash.Cqrs;
using OrderToCash.Orders.Domain;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// The <c>orders.cancel</c> command — <c>asyncapi.yaml</c>
/// <c>OrdersCancelRequestPayload</c>. <c>Reason</c> is deliberately NOT a
/// field here: the wire schema's own <c>reason</c> is a <c>const</c>
/// (<c>operator_cancelled</c>, the only value a caller may ever send —
/// <c>stock_rejected</c>/<c>credit_rejected</c> are decided by the saga,
/// never requested), validated at the wire boundary
/// (<c>OrdersCancelRequestValidator</c>) before this command is even built,
/// so the handler always cancels with exactly one
/// <see cref="CancellationReason.OperatorCancelled"/>.
/// </summary>
/// <remarks>
/// <paramref name="Note"/> is carried because the wire declares it, but it
/// cannot reach the read-model timeline in this deployment — see
/// <c>CancelOrderCommandHandler</c>'s own remarks and
/// <c>progress/impl_orders_cancel_responder.md</c>'s disclosure. It is not
/// silently dropped; it is genuinely unused past this record, and that is
/// stated here rather than left to look like an oversight.
/// </remarks>
public sealed record CancelOrderCommand(Guid OrderId, string? Note) : ICommand<CancelOrderResult>;

/// <summary>
/// What <see cref="CancelOrderCommandHandler"/> returns on every branch that
/// does not throw. <see cref="CancellationReason"/> is present only when
/// <see cref="Status"/> is already <see cref="OrderStatus.Cancelled"/> (the
/// immediate branch); <see cref="CompensationPlanned"/> is always present —
/// <c>asyncapi.yaml</c> <c>OrdersCancelReplyPayload.compensationPlanned</c>
/// is a REQUIRED field, empty for the immediate branch, never omitted.
/// Tokens are the wire's own <c>credit_release</c>/<c>stock_release</c> —
/// deliberately NOT the same spelling as
/// <see cref="Domain.CompensationStepKind"/>'s wire tokens
/// (<c>credit_released</c>/<c>stock_released</c>, past tense): one names
/// what WILL be released, the other names what WAS released, and
/// <c>asyncapi.yaml</c> gives them genuinely different enums.
/// </summary>
public sealed record CancelOrderResult(
    Guid OrderId,
    string OrderReference,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    IReadOnlyList<string> CompensationPlanned);
