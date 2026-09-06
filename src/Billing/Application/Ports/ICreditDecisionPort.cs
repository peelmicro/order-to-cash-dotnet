using OrderToCash.Billing.Domain;

namespace OrderToCash.Billing.Application.Ports;

/// <summary>Everything an adapter may see. It is told the amount and the line's state; it is NOT given the aggregate, the repository, the transaction or the clock (design.md §6.1).</summary>
public sealed record CreditDecisionRequest(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    long AmountMinorUnits,
    string Currency,
    long AvailableCreditMinorUnits);

/// <summary>
/// The two simulator refusal reasons ONLY. <c>over_limit</c> is
/// deliberately absent by construction — it is the aggregate's own word,
/// and only the aggregate may say it (`BC14`). #7 expressed this as
/// <c>Exclude&lt;CreditRejectionReason, 'over_limit'&gt;</c>, a structural
/// type operation C# has no equivalent of; this SEPARATE closed enum is
/// the rendering that keeps the same compile-time guarantee (design.md §6.1,
/// §15 ledger <c>L24</c>).
/// </summary>
public enum AdapterRejectionReason
{
    SimulatedCentsRule,
    SimulatedFailureRate,
}

/// <summary>
/// The total, exhaustive mapping <see cref="AdapterRejectionReason"/> →
/// <see cref="CreditRejectionReason"/>, in the ONE place this feature
/// performs it. C# enums are not closed types (any <see langword="int"/>
/// is a representable, unnamed value), so a switch with no catch-all arm at
/// all fails to compile even when every NAMED member is handled (`CS8524`)
/// — the catch-all below exists for that reason and throws, which is what
/// turns a member added to <see cref="AdapterRejectionReason"/> without a
/// corresponding arm into a loud runtime failure the moment anything calls
/// this mapping for it, exactly the shape <c>CreditEntryTypes.ToToken</c>
/// already uses for the same reason.
/// </summary>
public static class AdapterRejectionReasons
{
    public static CreditRejectionReason ToDomainReason(AdapterRejectionReason reason) => reason switch
    {
        AdapterRejectionReason.SimulatedCentsRule => CreditRejectionReason.SimulatedCentsRule,
        AdapterRejectionReason.SimulatedFailureRate => CreditRejectionReason.SimulatedFailureRate,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognised AdapterRejectionReason member."),
    };
}

/// <summary>The credit-decision port's outcome — either it approves, or it refuses with one of the two simulator reasons.</summary>
public abstract record CreditDecision
{
    public sealed record Approve : CreditDecision;

    /// <summary><c>over_limit</c> is deliberately unconstructible here (`BC14`) — <see cref="AdapterRejectionReason"/> has no such member.</summary>
    public sealed record Refuse(AdapterRejectionReason Reason) : CreditDecision;
}

/// <summary>Feature 20's seam, fixed now (design.md §6). Bound today by <c>AlwaysApproveCreditDecision</c>; feature 20 replaces the DI registration only.</summary>
public interface ICreditDecisionPort
{
    /// <summary>
    /// Called ONCE per hold, and ONLY when the aggregate has already
    /// answered <c>Fits</c> (`BC13`). MUST NOT perform I/O — it runs inside
    /// the credit line's row lock.
    /// </summary>
    ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken cancellationToken);
}
