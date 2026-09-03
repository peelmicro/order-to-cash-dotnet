using System.Globalization;
using OrderToCash.Orders.Domain.Errors;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// The nine statuses of the <c>Order</c> state machine
/// (specs/shared/domain-model.md §3.3, Table T-1). The enum is the domain
/// vocabulary; <see cref="OrderStatuses"/> carries the snake_case wire and
/// storage tokens so nobody is tempted to derive one from
/// <c>ToString().ToLower()</c>.
/// </summary>
public enum OrderStatus
{
    Placed,
    StockReserved,
    CreditApproved,
    Confirmed,
    Despatched,
    Invoiced,
    Paid,
    Completed,
    Cancelled,
}

/// <summary>
/// Maps <see cref="OrderStatus"/> to and from the snake_case tokens
/// <c>openapi.yaml</c>'s <c>OrderStatus</c> enum and the <c>status</c>
/// column publish and store — an explicit table, compared by
/// <see cref="StringComparison.Ordinal"/>, never a case transform of the
/// C# member name.
/// </summary>
public static class OrderStatuses
{
    public static string ToToken(OrderStatus status) => status switch
    {
        OrderStatus.Placed => "placed",
        OrderStatus.StockReserved => "stock_reserved",
        OrderStatus.CreditApproved => "credit_approved",
        OrderStatus.Confirmed => "confirmed",
        OrderStatus.Despatched => "despatched",
        OrderStatus.Invoiced => "invoiced",
        OrderStatus.Paid => "paid",
        OrderStatus.Completed => "completed",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unrecognised OrderStatus member."),
    };

    /// <summary>
    /// Parses a previously-stored or wire-received status token, raising
    /// <see cref="InvalidOrderSnapshotError"/> on anything outside the
    /// closed set — a load-time fault, not a business rejection of a live
    /// request (orders_acceptance's follow-up 3, closing
    /// review_orders_aggregate.md's advisory A3). No order id is available
    /// at this call site (the caller, <c>OrderRowMapper</c>, has not yet
    /// resolved a row into an aggregate), so <c>orderId</c> is
    /// <see langword="null"/> here — the same optionality #7's own
    /// <c>InvalidOrderSnapshotError</c> declares.
    /// </summary>
    public static OrderStatus Parse(string? token) => token switch
    {
        "placed" => OrderStatus.Placed,
        "stock_reserved" => OrderStatus.StockReserved,
        "credit_approved" => OrderStatus.CreditApproved,
        "confirmed" => OrderStatus.Confirmed,
        "despatched" => OrderStatus.Despatched,
        "invoiced" => OrderStatus.Invoiced,
        "paid" => OrderStatus.Paid,
        "completed" => OrderStatus.Completed,
        "cancelled" => OrderStatus.Cancelled,
        _ => throw new InvalidOrderSnapshotError(default, $"status \"{token ?? "<null>"}\" is not a recognised OrderStatus token."),
    };

    /// <summary>Formats a raw, out-of-range underlying value for <see cref="InvalidOrderSnapshotError"/> when a stored status is not even a defined enum member (§8.3's residual defensive check on <c>Order.Rehydrate</c>).</summary>
    internal static string DescribeUndefinedValue(OrderStatus status) => ((int)status).ToString(CultureInfo.InvariantCulture);
}
