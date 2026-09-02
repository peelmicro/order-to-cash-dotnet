using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Table T-1 (specs/shared/domain-model.md §3.3) and the vocabulary that
/// encodes it — R8, R9, and the two completeness checks of design.md §11.4.
/// </summary>
public sealed class OrderStateMachineTests
{
    private static readonly UniqueId _causationId = UniqueId.New();

    /// <summary>
    /// Table T-1 rows 2–12, transcribed independently from
    /// specs/shared/domain-model.md §3.3 — not read from
    /// <see cref="OrderStateMachine.LegalEdges"/>, per CLAUDE.md's warning
    /// that a test reading the same constant the code reads proves only that
    /// the constant equals itself (tasks.md §7 trap 3). Asserted both ways:
    /// nothing in T-1 is missing from the code, and nothing in the code was
    /// added beyond T-1. Arming row 9 (design.md §11.3): deleting
    /// <c>new(Confirmed, Cancelled)</c> from the production set must fail
    /// this test.
    /// </summary>
    [Fact]
    public void LegalEdges_Are_Exactly_The_Eleven_Status_To_Status_Edges_Of_Table_T1()
    {
        var transcribedFromSpec = new HashSet<OrderTransition>
        {
            new(OrderStatus.Placed, OrderStatus.StockReserved),        // row 2
            new(OrderStatus.StockReserved, OrderStatus.CreditApproved), // row 3
            new(OrderStatus.CreditApproved, OrderStatus.Confirmed),     // row 4
            new(OrderStatus.Confirmed, OrderStatus.Despatched),         // row 5
            new(OrderStatus.Despatched, OrderStatus.Invoiced),          // row 6
            new(OrderStatus.Invoiced, OrderStatus.Paid),                // row 7
            new(OrderStatus.Paid, OrderStatus.Completed),               // row 8
            new(OrderStatus.Placed, OrderStatus.Cancelled),             // row 9
            new(OrderStatus.StockReserved, OrderStatus.Cancelled),      // row 10
            new(OrderStatus.CreditApproved, OrderStatus.Cancelled),     // row 11
            new(OrderStatus.Confirmed, OrderStatus.Cancelled),          // row 12
        };

        var fromProduction = new HashSet<OrderTransition>(OrderStateMachine.LegalEdges);

        Assert.True(transcribedFromSpec.SetEquals(fromProduction), "The eleven edges transcribed from Table T-1 must equal OrderStateMachine.LegalEdges exactly, in both directions.");
        Assert.Equal(11, fromProduction.Count);
    }

    /// <summary>Every enum member round-trips its exact wire token, compared by value — not a case transform of the C# member name (task 2.6).</summary>
    [Fact]
    public void OrderStatuses_And_CancellationReasons_RoundTrip_Their_Wire_Tokens()
    {
        var expectedStatusTokens = new Dictionary<OrderStatus, string>
        {
            [OrderStatus.Placed] = "placed",
            [OrderStatus.StockReserved] = "stock_reserved",
            [OrderStatus.CreditApproved] = "credit_approved",
            [OrderStatus.Confirmed] = "confirmed",
            [OrderStatus.Despatched] = "despatched",
            [OrderStatus.Invoiced] = "invoiced",
            [OrderStatus.Paid] = "paid",
            [OrderStatus.Completed] = "completed",
            [OrderStatus.Cancelled] = "cancelled",
        };

        foreach (var (status, token) in expectedStatusTokens)
        {
            Assert.Equal(token, OrderStatuses.ToToken(status));
            Assert.Equal(status, OrderStatuses.Parse(token));
        }

        var expectedReasonTokens = new Dictionary<CancellationReason, string>
        {
            [CancellationReason.StockRejected] = "stock_rejected",
            [CancellationReason.CreditRejected] = "credit_rejected",
            [CancellationReason.OperatorCancelled] = "operator_cancelled",
        };

        foreach (var (reason, token) in expectedReasonTokens)
        {
            Assert.Equal(token, CancellationReasons.ToToken(reason));
            Assert.Equal(reason, CancellationReasons.Parse(token));
        }
    }

    /// <summary>Both refusal codes of <see cref="CancellationReasons.Parse"/>, distinctly (task 2.7).</summary>
    [Fact]
    public void R10_CancellationReasons_Parse_RaisesWhenTheTokenIsMissingOrOutsideTheClosedSet()
    {
        Assert.Equal("order.cancellation_reason_required", Assert.Throws<CancellationReasonRequiredError>(() => CancellationReasons.Parse(null)).Code);
        Assert.Equal("order.cancellation_reason_required", Assert.Throws<CancellationReasonRequiredError>(() => CancellationReasons.Parse(string.Empty)).Code);
        Assert.Equal("order.cancellation_reason_required", Assert.Throws<CancellationReasonRequiredError>(() => CancellationReasons.Parse("   ")).Code);

        var unknown = Assert.Throws<UnknownCancellationReasonError>(() => CancellationReasons.Parse("not_a_real_reason"));
        Assert.Equal("order.cancellation_reason_unknown", unknown.Code);
        Assert.Equal("not_a_real_reason", unknown.OffendingToken);
    }

    /// <summary>
    /// Walks the real edges from <see cref="Order.Place"/> for the whole
    /// happy path, and separately for each of the four cancellable sources —
    /// the legal-walk half of R8, complementing R9's illegal-matrix half
    /// (design.md §3.4).
    /// </summary>
    [Fact]
    public void R8_Order_WalksEveryLegalEdgeOfTableT1()
    {
        var order = OrderTestData.PlacedOrder();
        Assert.Equal(OrderStatus.Placed, order.Status);

        order.MarkStockReserved(OrderTestData.Now);
        Assert.Equal(OrderStatus.StockReserved, order.Status);

        order.ApproveCredit(OrderTestData.Now);
        Assert.Equal(OrderStatus.CreditApproved, order.Status);

        order.Confirm(OrderTestData.Now, _causationId);
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        order.MarkDespatched(OrderTestData.Now);
        Assert.Equal(OrderStatus.Despatched, order.Status);

        order.MarkInvoiced(OrderTestData.Now);
        Assert.Equal(OrderStatus.Invoiced, order.Status);

        order.MarkPaid(OrderTestData.Now);
        Assert.Equal(OrderStatus.Paid, order.Status);

        order.Complete(OrderTestData.Now, _causationId);
        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    /// <summary>The four legal cancel sources succeed with T-1's paired reason; the five illegal ones (including <c>Cancelled</c> itself) raise <see cref="OrderNotCancellableError"/> (R8).</summary>
    [Fact]
    public void R8_Order_ReachesCancelledOnlyFromPlacedStockReservedCreditApprovedAndConfirmed()
    {
        var legalSources = new (OrderStatus From, CancellationReason Reason)[]
        {
            (OrderStatus.Placed, CancellationReason.StockRejected),
            (OrderStatus.StockReserved, CancellationReason.CreditRejected),
            (OrderStatus.CreditApproved, CancellationReason.OperatorCancelled),
            (OrderStatus.Confirmed, CancellationReason.OperatorCancelled),
        };

        foreach (var (from, reason) in legalSources)
        {
            var order = OrderTestData.RehydratedOrder(from);

            order.Cancel(reason, [], OrderTestData.Now, _causationId);

            Assert.Equal(OrderStatus.Cancelled, order.Status);
            Assert.Equal(reason, order.CancellationReason);
        }

        var illegalSources = new[] { OrderStatus.Despatched, OrderStatus.Invoiced, OrderStatus.Paid, OrderStatus.Completed };

        foreach (var from in illegalSources)
        {
            var order = OrderTestData.RehydratedOrder(from);

            var error = Assert.Throws<OrderNotCancellableError>(() => order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId));

            Assert.Equal("order.not_cancellable", error.Code);
            Assert.Equal(from, order.Status);
        }

        var alreadyCancelled = OrderTestData.RehydratedOrder(OrderStatus.Cancelled, CancellationReason.OperatorCancelled);
        Assert.Throws<OrderNotCancellableError>(() => alreadyCancelled.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId));
    }

    /// <summary>All sixteen outbound attempts from the two terminal statuses are refused (O7, R8).</summary>
    [Fact]
    public void R8_Order_TreatsCompletedAndCancelledAsTerminal()
    {
        foreach (var terminalStatus in new[] { OrderStatus.Completed, OrderStatus.Cancelled })
        {
            foreach (var (name, action) in AllNineTriggerActions())
            {
                if (name == "Place")
                {
                    continue;
                }

                var order = terminalStatus == OrderStatus.Cancelled
                    ? OrderTestData.RehydratedOrder(terminalStatus, CancellationReason.OperatorCancelled)
                    : OrderTestData.RehydratedOrder(terminalStatus);

                Assert.ThrowsAny<IllegalOrderTransitionError>(() => action(order));
                Assert.Equal(terminalStatus, order.Status);
            }
        }
    }

    /// <summary>
    /// R9, exhaustively: 9 statuses × 8 reachable targets = 72 attemptable
    /// pairs (<c>Placed</c> is never a target — creation is not a
    /// transition), of which 11 are legal and 61 must raise, leaving status,
    /// event count and <c>UpdatedAt</c> untouched (design.md §3.4). The
    /// three counts are asserted explicitly so that adding a status without
    /// extending this test fails. Arming row 6: moving the <c>Raise</c>
    /// above the <c>IsLegal</c> guard must fail this test.
    /// </summary>
    [Fact]
    public void R9_Order_RaisesOnEveryFromToPairAbsentFromTableT1WithoutMutatingStateOrAppendingAnEvent()
    {
        var fromStatuses = Enum.GetValues<OrderStatus>();
        var targetActions = ReachableTargetActions();

        Assert.Equal(9, fromStatuses.Length);
        Assert.Equal(8, targetActions.Count);

        var attempted = 0;
        var legal = 0;
        var illegal = 0;

        foreach (var from in fromStatuses)
        {
            foreach (var (to, action) in targetActions)
            {
                attempted++;

                var order = from == OrderStatus.Cancelled
                    ? OrderTestData.RehydratedOrder(from, CancellationReason.OperatorCancelled)
                    : OrderTestData.RehydratedOrder(from);

                var isLegalPair = LegalPairsFromSpec().Contains(new OrderTransition(from, to));

                if (isLegalPair)
                {
                    legal++;
                    action(order);
                    Assert.Equal(to, order.Status);
                    continue;
                }

                illegal++;

                var eventCountBefore = order.DomainEvents.Count;
                var updatedAtBefore = order.UpdatedAt;

                Assert.ThrowsAny<IllegalOrderTransitionError>(() => action(order));

                Assert.Equal(from, order.Status);
                Assert.Equal(eventCountBefore, order.DomainEvents.Count);
                Assert.Equal(updatedAtBefore, order.UpdatedAt);
            }
        }

        Assert.Equal(72, attempted);
        Assert.Equal(11, legal);
        Assert.Equal(61, illegal);
    }

    private static HashSet<OrderTransition> LegalPairsFromSpec() =>
    [
        new(OrderStatus.Placed, OrderStatus.StockReserved),
        new(OrderStatus.StockReserved, OrderStatus.CreditApproved),
        new(OrderStatus.CreditApproved, OrderStatus.Confirmed),
        new(OrderStatus.Confirmed, OrderStatus.Despatched),
        new(OrderStatus.Despatched, OrderStatus.Invoiced),
        new(OrderStatus.Invoiced, OrderStatus.Paid),
        new(OrderStatus.Paid, OrderStatus.Completed),
        new(OrderStatus.Placed, OrderStatus.Cancelled),
        new(OrderStatus.StockReserved, OrderStatus.Cancelled),
        new(OrderStatus.CreditApproved, OrderStatus.Cancelled),
        new(OrderStatus.Confirmed, OrderStatus.Cancelled),
    ];

    /// <summary>The eight reachable targets, each as an <c>Action&lt;Order&gt;</c> that attempts to reach it — <c>Placed</c> excluded, since no method targets it (design.md §3.4).</summary>
    private static List<(OrderStatus To, Action<Order> Action)> ReachableTargetActions() =>
    [
        (OrderStatus.StockReserved, order => order.MarkStockReserved(OrderTestData.Now)),
        (OrderStatus.CreditApproved, order => order.ApproveCredit(OrderTestData.Now)),
        (OrderStatus.Confirmed, order => order.Confirm(OrderTestData.Now, _causationId)),
        (OrderStatus.Despatched, order => order.MarkDespatched(OrderTestData.Now)),
        (OrderStatus.Invoiced, order => order.MarkInvoiced(OrderTestData.Now)),
        (OrderStatus.Paid, order => order.MarkPaid(OrderTestData.Now)),
        (OrderStatus.Completed, order => order.Complete(OrderTestData.Now, _causationId)),
        (OrderStatus.Cancelled, order => order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId)),
    ];

    private static List<(string Name, Action<Order> Action)> AllNineTriggerActions() =>
    [
        ("Place", _ => throw new InvalidOperationException("Place is a factory, not an in-place trigger.")),
        ("MarkStockReserved", order => order.MarkStockReserved(OrderTestData.Now)),
        ("ApproveCredit", order => order.ApproveCredit(OrderTestData.Now)),
        ("Confirm", order => order.Confirm(OrderTestData.Now, _causationId)),
        ("MarkDespatched", order => order.MarkDespatched(OrderTestData.Now)),
        ("MarkInvoiced", order => order.MarkInvoiced(OrderTestData.Now)),
        ("MarkPaid", order => order.MarkPaid(OrderTestData.Now)),
        ("Complete", order => order.Complete(OrderTestData.Now, _causationId)),
        ("Cancel", order => order.Cancel(CancellationReason.OperatorCancelled, [], OrderTestData.Now, _causationId)),
    ];
}
