using OrderToCash.Orders.Domain.Errors;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// The closed set of reasons an order may be cancelled for
/// (specs/shared/domain-model.md §3.1: <c>CancellationReason</c> is a closed
/// set of <c>stock_rejected</c>, <c>credit_rejected</c>,
/// <c>operator_cancelled</c>).
/// </summary>
public enum CancellationReason
{
    StockRejected,
    CreditRejected,
    OperatorCancelled,
}

/// <summary>
/// Maps <see cref="CancellationReason"/> to and from its snake_case wire
/// token, and owns the parse boundary R10's unwanted-behaviour clause lives
/// at in C#: an <c>enum</c> parameter cannot be absent, so "no reason was
/// supplied" only becomes reachable when a token is parsed off the wire or
/// out of a fact payload (design.md §6.2).
/// </summary>
public static class CancellationReasons
{
    public static string ToToken(CancellationReason reason) => reason switch
    {
        CancellationReason.StockRejected => "stock_rejected",
        CancellationReason.CreditRejected => "credit_rejected",
        CancellationReason.OperatorCancelled => "operator_cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unrecognised CancellationReason member."),
    };

    /// <summary>
    /// Parses a wire or persisted token. <c>null</c>, empty or whitespace
    /// raises <see cref="CancellationReasonRequiredError"/> (a missing
    /// reason — a contract failure by the sender); a non-empty token outside
    /// the closed set raises <see cref="UnknownCancellationReasonError"/> (an
    /// unknown reason — usually a version skew). The two are distinct codes
    /// because a caller — and <c>saga.md</c>'s dead-letter reasoning — needs
    /// to tell them apart.
    /// </summary>
    public static CancellationReason Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new CancellationReasonRequiredError();
        }

        return token switch
        {
            "stock_rejected" => CancellationReason.StockRejected,
            "credit_rejected" => CancellationReason.CreditRejected,
            "operator_cancelled" => CancellationReason.OperatorCancelled,
            _ => throw new UnknownCancellationReasonError(token),
        };
    }
}
