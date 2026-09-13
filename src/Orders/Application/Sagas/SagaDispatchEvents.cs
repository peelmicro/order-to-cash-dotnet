namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The five dispatch-owed application events (design.md §5.5) — plain
/// records carrying only <see cref="OrderId"/> and <see cref="CorrelationId"/>,
/// published by the relevant fact <c>ICommandHandler</c> STRICTLY AFTER
/// commit, and turned into an <see cref="Ports.SagaCommandRef"/> signal by
/// <c>OrderSagas.cs</c>'s five <c>IEventHandler&lt;T&gt;</c> classes — #7's
/// <c>@Saga</c> role, played by an in-process signal because .NET's
/// dispatcher has no framework-level analogue that stays off the consume
/// loop (SO10).
/// </summary>
public sealed record OrderPlacedFactRecorded(Guid OrderId, Guid CorrelationId);

public sealed record OrderMarkedStockReserved(Guid OrderId, Guid CorrelationId);

public sealed record CreditRejectionRecorded(Guid OrderId, Guid CorrelationId);

public sealed record OrderConfirmedBySaga(Guid OrderId, Guid CorrelationId);

public sealed record OrderMarkedDespatched(Guid OrderId, Guid CorrelationId);

/// <summary>
/// The sixth dispatch-owed event — SA-4 (the human-gated shared-spec
/// amendment ruled 2026-09-11) moved this from <c>credit.released.v1</c> to
/// <c>stock.released.v1</c>: published by <c>HandleStockReleasedFactCommandHandler</c>
/// ONLY when the fact matched its <c>credit_approved</c>/<c>confirmed</c>
/// variant (<see cref="SagaStepTable"/>) and therefore owed
/// <see cref="SagaCommandKind.CreditRelease"/> next — the CONTESTED resource
/// (stock) having just been released is what makes credit due, the reverse
/// of the pre-SA-4 shape. Deliberately its OWN type, not reused from any
/// other owed-command event: the triggering fact and the branch of the saga
/// it belongs to are what a reader needs to tell apart.
/// </summary>
public sealed record StockReleasedForCancellationRecorded(Guid OrderId, Guid CorrelationId);

/// <summary>
/// The seventh dispatch-owed event — id 62 fix round 1, F1. <c>credit.approved.v1</c>
/// is the ONE fact type whose <see cref="SagaFactHandler"/> handling is NOT
/// fully described by <see cref="SagaStepTable"/> alone: SA-4's late-approval
/// branch (<c>SagaFactHandler.HandleAsync</c>'s own
/// <c>lateForAnAcceptedOperatorCancel</c> check, run BEFORE <see cref="SagaStepTable.ForStatus"/>
/// is ever consulted) enqueues <see cref="SagaCommandKind.CreditRelease"/>
/// directly, never going through the ordinary Advance's own
/// <c>SagaCommandKind.DespatchCreate</c> at all. <c>HandleCreditApprovedFactCommandHandler</c>
/// publishes THIS event, never <see cref="OrderConfirmedBySaga"/>, when that
/// is the branch that fired — <c>OrderConfirmedBySagaHandler</c>'s own
/// hard-coded <c>SagaCommandKind.DespatchCreate</c> signal would otherwise
/// claim a saga_commands row that was never enqueued, stranding the real
/// <c>credit.release</c> row for the sweeper alone to find.
/// </summary>
public sealed record LateCreditApprovalForCancellationRecorded(Guid OrderId, Guid CorrelationId);
