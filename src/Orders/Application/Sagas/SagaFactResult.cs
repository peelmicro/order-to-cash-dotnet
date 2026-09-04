using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>The three outcomes <see cref="SagaFactHandler.HandleAsync"/> can report (design.md §5.1).</summary>
public enum SagaFactOutcome
{
    /// <summary>The dedup record already existed (R18) — nothing was mutated, nothing emitted.</summary>
    Duplicate,

    /// <summary>Recorded and acknowledged without effect — unknown order (SO8) or unmet precondition (R25).</summary>
    Ignored,

    /// <summary>The step applied — the aggregate was (possibly) changed and the transaction committed. <see cref="Enqueued"/> names the command owed, if any.</summary>
    Processed,
}

/// <summary>What <see cref="SagaFactHandler.HandleAsync"/> returns — the outcome, and the command it enqueued (only on <see cref="SagaFactOutcome.Processed"/>, and only when the step owed one).</summary>
public sealed record SagaFactResult(SagaFactOutcome Outcome, SagaCommandRef? Enqueued);
