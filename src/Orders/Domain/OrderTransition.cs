namespace OrderToCash.Orders.Domain;

/// <summary>
/// One (<see cref="From"/>, <see cref="To"/>) status-to-status edge. A
/// <c>readonly record struct</c> gives value equality, which is exactly what
/// <see cref="OrderStateMachine.LegalEdges"/>'s set-membership test wants,
/// and avoids allocating one instance per attempted pair in the R9 test's 72
/// combinations (design.md §2).
/// </summary>
public readonly record struct OrderTransition(OrderStatus From, OrderStatus To);
