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
/// The sixth dispatch-owed event (feature <c>orders_cancel_responder</c>) —
/// published by <c>HandleCreditReleasedFactCommandHandler</c> ONLY when the
/// <c>credit.released.v1</c> fact matched its <c>credit_approved</c>/
/// <c>confirmed</c> variant (<see cref="SagaStepTable"/>) and therefore owed
/// <see cref="SagaCommandKind.StockRelease"/> next — deliberately NOT the
/// same type as <see cref="CreditRejectionRecorded"/> even though both map
/// to the identical owed command: the triggering fact and the branch of the
/// saga they belong to are genuinely different (R27's credit rejection vs.
/// the operator-cancel reverse-order-of-acquisition compensation), and
/// collapsing them into one type would make a reader unable to tell which
/// branch actually fired from the event alone.
/// </summary>
public sealed record CreditReleasedForCancellationRecorded(Guid OrderId, Guid CorrelationId);
