using System.Collections.Frozen;
using static OrderToCash.Orders.Domain.OrderStatus;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// Table T-1's eleven status-to-status edges (specs/shared/domain-model.md
/// §3.3), rows 2–12, encoded once as data. Row 1 (<c>(none) -&gt; placed</c>)
/// is creation, not a transition — it is <see cref="Order.Place"/>, and it
/// has no <c>from</c> to look up. <c>System.Collections.Frozen</c> is BCL,
/// not a framework, and buys a faster lookup for a set built once and read
/// on every transition (design.md §3.1).
/// </summary>
/// <remarks>
/// <see cref="Order"/>'s only writer of <c>Status</c> is its own private
/// <c>TransitionTo</c> — this table is read-only data and exposing it does
/// not weaken that. It is <see langword="public"/> rather than the
/// <see langword="internal"/> shown in design.md §3.1's illustrative snippet
/// because the required transcription test
/// (<c>OrderStateMachineTests.LegalEdges_Are_Exactly_The_Eleven_Status_To_Status_Edges_Of_Table_T1</c>,
/// tasks.md §2.5) asserts set equality directly against
/// <see cref="LegalEdges"/> from a separate test assembly, and
/// <c>InternalsVisibleTo</c> would have opened every other internal member
/// of this project to the test project as a side effect, not just this one
/// intentionally-public table.
/// </remarks>
public static class OrderStateMachine
{
    public static readonly FrozenSet<OrderTransition> LegalEdges = new OrderTransition[]
    {
        new(Placed, StockReserved),        // T-1 row 2
        new(StockReserved, CreditApproved), // T-1 row 3
        new(CreditApproved, Confirmed),     // T-1 row 4
        new(Confirmed, Despatched),         // T-1 row 5
        new(Despatched, Invoiced),          // T-1 row 6
        new(Invoiced, Paid),                // T-1 row 7
        new(Paid, Completed),               // T-1 row 8
        new(Placed, Cancelled),             // T-1 row 9
        new(StockReserved, Cancelled),      // T-1 row 10
        new(CreditApproved, Cancelled),     // T-1 row 11
        new(Confirmed, Cancelled),          // T-1 row 12
    }.ToFrozenSet();

    /// <summary>
    /// The only authority <c>Order.TransitionTo</c> consults. Terminality
    /// (O7) needs no explicit check: <see cref="OrderStatus.Completed"/> and
    /// <see cref="OrderStatus.Cancelled"/> appear in <see cref="LegalEdges"/>
    /// only as targets, never as a <c>From</c>, so this returns
    /// <see langword="false"/> for all sixteen outbound pairs without a
    /// single <c>if</c> (design.md §3.3).
    /// </summary>
    public static bool IsLegal(OrderStatus from, OrderStatus to) => LegalEdges.Contains(new OrderTransition(from, to));
}
