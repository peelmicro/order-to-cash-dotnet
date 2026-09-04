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
