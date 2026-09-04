using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §4.1, §11.1 — the fourteen-row step table, exhaustively:
/// EVERY one of the ten consumed facts × EVERY one of the nine
/// <see cref="OrderStatus"/> values, built from real <see cref="Order"/>
/// instances with no store and no framework. The four self-produced facts
/// (SO2) are covered separately, since they carry no precondition at all.
/// </summary>
public sealed class SagaStepTableTests
{
    private static readonly OrderStatus[] _allStatuses =
    [
        OrderStatus.Placed, OrderStatus.StockReserved, OrderStatus.CreditApproved, OrderStatus.Confirmed,
        OrderStatus.Despatched, OrderStatus.Invoiced, OrderStatus.Paid, OrderStatus.Completed, OrderStatus.Cancelled,
    ];

    private static readonly string[] _consumedFacts =
    [
        "order.placed.v1", "stock.reserved.v1", "stock.rejected.v1", "credit.approved.v1", "credit.rejected.v1",
        "stock.released.v1", "order.despatched.v1", "invoice.issued.v1", "payment.received.v1", "credit.released.v1",
    ];

    public static IEnumerable<object[]> FactsCrossStatuses()
    {
        foreach (var eventType in _consumedFacts)
        {
            foreach (var status in _allStatuses)
            {
                yield return [eventType, status];
            }
        }
    }

    [Theory]
    [MemberData(nameof(FactsCrossStatuses))]
    public void SagaStepTable_AppliesExactlyWhenThePreconditionIsMetAndLeavesTheOrderUntouchedOtherwise(string eventType, OrderStatus status)
    {
        var order = OrderTestData.RehydratedOrder(status, cancellationReason: status == OrderStatus.Cancelled ? CancellationReason.StockRejected : null);
        var fact = BuildFact(eventType, "credit_rejected");
        var step = SagaStepTable.For(eventType);

        Assert.NotNull(step);
        var precondition = PreconditionOf(step);
        var (expectedNextStatus, expectedEventCount, expectedOwed) = ExpectedOutcome(eventType);

        if (status == precondition)
        {
            ApplyIfPreconditionMet(order, step!, fact);

            Assert.Equal(expectedNextStatus, order.Status);
            Assert.Equal(expectedEventCount, order.DomainEvents.Count);
            Assert.Equal(expectedOwed, CommandAfterOf(step!));
        }
        else
        {
            var beforeStatus = order.Status;

            var applied = ApplyIfPreconditionMet(order, step!, fact);

            Assert.False(applied, $"{eventType} must not apply when the order is {status}, only when it is {precondition}.");
            Assert.Equal(beforeStatus, order.Status);
            Assert.Empty(order.DomainEvents);
        }
    }

    [Fact]
    public void R21_CreditApprovedV1_PerformsBothEdgesInOneLoadSaveAndRaisesExactlyOneOrderConfirmed()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var fact = BuildFact("credit.approved.v1", "credit_rejected");
        var step = SagaStepTable.For("credit.approved.v1");

        ((SagaStep.Advance)step!).Apply!(order, fact);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        var raised = Assert.Single(order.DomainEvents);
        Assert.IsType<OrderConfirmed>(raised);
        Assert.Equal(SagaCommandKind.DespatchCreate, ((SagaStep.Advance)step).CommandAfter);
    }

    [Fact]
    public void R23_InvoiceIssuedV1_AdvancesToInvoicedAndOwesNothing()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Despatched);
        var fact = BuildFact("invoice.issued.v1", "credit_rejected");
        var step = (SagaStep.Advance)SagaStepTable.For("invoice.issued.v1")!;

        step.Apply!(order, fact);

        Assert.Equal(OrderStatus.Invoiced, order.Status);
        Assert.Null(step.CommandAfter);
    }

    [Fact]
    public void R26_StockRejectedV1_CancelsWithEmptyCompensationStepsAndOwesNothing()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.Placed);
        var fact = BuildFact("stock.rejected.v1", "credit_rejected");
        var step = (SagaStep.Cancel)SagaStepTable.For("stock.rejected.v1")!;

        order.Cancel(step.Reason(fact), step.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(order.DomainEvents));
        Assert.Equal(CancellationReason.StockRejected, cancelled.CancellationReason);
        Assert.Empty(cancelled.CompensationSteps);
    }

    [Fact]
    public void R27_CreditRejectedV1_LeavesTheStatusUntouchedAndOwesStockRelease()
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var step = (SagaStep.Advance)SagaStepTable.For("credit.rejected.v1")!;

        Assert.Null(step.Apply);
        Assert.Equal(SagaCommandKind.StockRelease, step.CommandAfter);
        Assert.Equal(OrderStatus.StockReserved, order.Status);
    }

    [Theory]
    [InlineData("credit_rejected", CancellationReason.CreditRejected)]
    [InlineData("order_cancelled", CancellationReason.OperatorCancelled)]
    public void R28_SO7_StockReleasedV1_CancelsWithExactlyOneStockReleasedCompensationStepBuiltFromTheObservedFact(string wireReason, CancellationReason expectedReason)
    {
        var order = OrderTestData.RehydratedOrder(OrderStatus.StockReserved);
        var fact = BuildFact("stock.released.v1", wireReason);
        var step = (SagaStep.Cancel)SagaStepTable.For("stock.released.v1")!;

        var reason = step.Reason(fact);
        var compensationSteps = step.CompensationSteps(fact);
        order.Cancel(reason, compensationSteps, fact.OccurredAt, UniqueId.From(fact.EventId));

        Assert.Equal(expectedReason, reason);
        var cancelled = Assert.IsType<OrderCancelled>(Assert.Single(order.DomainEvents));
        var compensationStep = Assert.Single(cancelled.CompensationSteps);
        Assert.Equal(CompensationStepKind.StockReleased, compensationStep.Step);
        Assert.Equal(fact.EventId, compensationStep.EventId!.Value.Value);
        Assert.Equal(fact.EventType, compensationStep.EventType);
        Assert.Equal(fact.OccurredAt, compensationStep.OccurredAt);
    }

    [Theory]
    [InlineData("order.confirmed.v1")]
    [InlineData("order.completed.v1")]
    [InlineData("order.cancelled.v1")]
    [InlineData("order.saga_failed.v1")]
    public void SO2_TheFourSelfProducedFacts_MapToSkip(string eventType)
    {
        var step = SagaStepTable.For(eventType);

        Assert.IsType<SagaStep.Skip>(step);
    }

    private static SagaFact BuildFact(string eventType, string stockReleasedReason)
    {
        object payload = eventType == "stock.released.v1"
            ? new StockReleasedPayload("ORD-000001", "COMPANY-01", [], stockReleasedReason)
            : new object();

        return new SagaFact(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            AggregateId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            OccurredAt: OrderTestData.Now.AddMinutes(5),
            Payload: payload);
    }

    private static OrderStatus PreconditionOf(SagaStep? step) => step switch
    {
        SagaStep.Advance advance => advance.Precondition,
        SagaStep.Cancel cancel => cancel.Precondition,
        _ => throw new InvalidOperationException($"Unexpected step shape {step}."),
    };

    private static SagaCommandKind? CommandAfterOf(SagaStep step) => step switch
    {
        SagaStep.Advance advance => advance.CommandAfter,
        SagaStep.Cancel => null,
        _ => throw new InvalidOperationException($"Unexpected step shape {step}."),
    };

    /// <summary>Mimics exactly what <c>SagaFactHandler</c> will do (design.md §4.2, §5.1): compare status to precondition by equality, and only then apply. Returns whether the step applied.</summary>
    private static bool ApplyIfPreconditionMet(Order order, SagaStep step, SagaFact fact)
    {
        switch (step)
        {
            case SagaStep.Advance advance:
                if (order.Status != advance.Precondition)
                {
                    return false;
                }

                advance.Apply?.Invoke(order, fact);
                return true;

            case SagaStep.Cancel cancel:
                if (order.Status != cancel.Precondition)
                {
                    return false;
                }

                order.Cancel(cancel.Reason(fact), cancel.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId));
                return true;

            default:
                throw new InvalidOperationException($"Unexpected step shape {step}.");
        }
    }

    private static (OrderStatus NextStatus, int EventCount, SagaCommandKind? Owed) ExpectedOutcome(string eventType) => eventType switch
    {
        "order.placed.v1" => (OrderStatus.Placed, 0, SagaCommandKind.StockReserve),
        "stock.reserved.v1" => (OrderStatus.StockReserved, 0, SagaCommandKind.CreditHold),
        "stock.rejected.v1" => (OrderStatus.Cancelled, 1, null),
        "credit.approved.v1" => (OrderStatus.Confirmed, 1, SagaCommandKind.DespatchCreate),
        "credit.rejected.v1" => (OrderStatus.StockReserved, 0, SagaCommandKind.StockRelease),
        "stock.released.v1" => (OrderStatus.Cancelled, 1, null),
        "order.despatched.v1" => (OrderStatus.Despatched, 0, SagaCommandKind.InvoiceIssue),
        "invoice.issued.v1" => (OrderStatus.Invoiced, 0, null),
        "payment.received.v1" => (OrderStatus.Paid, 0, null),
        "credit.released.v1" => (OrderStatus.Completed, 1, null),
        _ => throw new InvalidOperationException($"No expected outcome fixture for '{eventType}'."),
    };
}
