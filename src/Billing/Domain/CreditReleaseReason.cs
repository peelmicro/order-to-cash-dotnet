namespace OrderToCash.Billing.Domain;

/// <summary>
/// The closed set of reasons a <c>release</c> ledger entry can be appended
/// for — <c>asyncapi.yaml</c> <c>CreditReleasedPayload.reason</c>.
/// <see cref="OrderCancelled"/> is the only reason this feature's
/// responder can trigger (`BC25` — <c>reason</c> is not a caller-supplied
/// field on <c>billing.credit.release</c>); <see cref="InvoicePaid"/> is
/// set only by feature 22's payment-registration flow.
/// </summary>
public enum CreditReleaseReason
{
    InvoicePaid,
    OrderCancelled,
}

/// <summary>Maps <see cref="CreditReleaseReason"/> to and from the wire's snake_case tokens.</summary>
public static class CreditReleaseReasons
{
    public static string ToToken(CreditReleaseReason reason) => reason switch
    {
        CreditReleaseReason.InvoicePaid => "invoice_paid",
        CreditReleaseReason.OrderCancelled => "order_cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognised CreditReleaseReason member."),
    };
}
