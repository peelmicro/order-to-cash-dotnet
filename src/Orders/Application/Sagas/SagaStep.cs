using OrderToCash.Orders.Domain;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The three kinds of reaction a consumed fact provokes (design.md §4.1) —
/// pure data and pure functions, no I/O, no framework reference anywhere in
/// this file or its neighbours in <c>Application/Sagas/</c>.
/// </summary>
public abstract record SagaStep
{
    /// <summary>The fact is self-produced or otherwise never routed here — <see cref="SagaStepTable.For"/> never actually returns this for a routed fact (SO2 is enforced earlier, in <c>SagaFactsConsumer</c>); kept as the belt-and-braces default (design.md §5.1 step 1).</summary>
    public sealed record Skip : SagaStep;

    /// <summary>
    /// A fact that — when the order is in <see cref="Precondition"/> — may
    /// apply zero or more aggregate calls and owes a follow-up command.
    /// <see cref="Apply"/> is <see langword="null"/> when the status is
    /// deliberately left unchanged (R19, R27); <see cref="CommandAfter"/> is
    /// <see langword="null"/> when nothing is owed after this step (R23).
    /// </summary>
    public sealed record Advance(
        OrderStatus Precondition,
        Action<Domain.Order, SagaFact>? Apply,
        SagaCommandKind? CommandAfter) : SagaStep;

    /// <summary>
    /// A fact that — when the order is in <see cref="Precondition"/> —
    /// cancels the order with a reason and compensation steps derived from
    /// the observed fact itself (R26, R28, SO7).
    /// </summary>
    public sealed record Cancel(
        OrderStatus Precondition,
        Func<SagaFact, CancellationReason> Reason,
        Func<SagaFact, IReadOnlyList<OrderCompensationStep>> CompensationSteps) : SagaStep;
}
