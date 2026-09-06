namespace OrderToCash.Billing.Domain;

/// <summary>
/// The closed set of reasons a <c>credit.hold</c> can be refused —
/// <c>asyncapi.yaml</c> <c>CreditRejectedPayload.reason</c> /
/// <c>CreditHoldReplyPayload.reason</c>. <see cref="OverLimit"/> is the
/// aggregate's own word (`BC14`) — an adapter behind the credit-decision
/// port cannot construct it, by the separate, narrower
/// <c>AdapterRejectionReason</c> enum (§6.1).
/// </summary>
public enum CreditRejectionReason
{
    OverLimit,
    SimulatedCentsRule,
    SimulatedFailureRate,
}

/// <summary>Maps <see cref="CreditRejectionReason"/> to and from the wire's snake_case tokens.</summary>
public static class CreditRejectionReasons
{
    public static string ToToken(CreditRejectionReason reason) => reason switch
    {
        CreditRejectionReason.OverLimit => "over_limit",
        CreditRejectionReason.SimulatedCentsRule => "simulated_cents_rule",
        CreditRejectionReason.SimulatedFailureRate => "simulated_failure_rate",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognised CreditRejectionReason member."),
    };
}
