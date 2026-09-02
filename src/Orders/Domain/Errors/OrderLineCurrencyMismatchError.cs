using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Errors;

/// <summary>
/// Raised when a line's <c>unitPrice</c> or <c>lineDiscount</c> is not in
/// the order's currency — invariant O2 (specs/shared/requirements.md R2).
/// Enforced explicitly on the aggregate, before the candidate totals are
/// built, so the caller sees an <em>order</em> invariant rather than the
/// <em>shared-kernel</em> <c>money.cross_currency</c> a mismatched
/// <c>Money.Add</c> would otherwise raise one step later (design.md §5.3).
/// </summary>
public sealed class OrderLineCurrencyMismatchError : DomainError
{
    public OrderLineCurrencyMismatchError(string orderCurrency, string lineCurrency)
        : base(
            "order.line_currency_mismatch",
            $"A line amount is in currency '{lineCurrency}', but the order's currency is '{orderCurrency}'.")
    {
        OrderCurrency = orderCurrency;
        LineCurrency = lineCurrency;
    }

    public string OrderCurrency { get; }

    public string LineCurrency { get; }
}
