using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// `FS20`, ledger L4. Raised when a replenishment or a reservation's summed
/// line units would overflow the <c>int</c>-typed unit counters — C#
/// <c>int</c> arithmetic is unchecked by default
/// (<c>Directory.Build.props</c> sets no <c>CheckForOverflowUnderflow</c>),
/// so without this guard <c>units + quantity</c> would wrap to a negative
/// value in silence.
/// </summary>
public sealed class StockUnitOverflowError(string productCode, string operation)
    : DomainError("STOCK_UNIT_OVERFLOW", $"Product '{productCode}': {operation} would overflow the unit counter.")
{
    public string ProductCode { get; } = productCode;

    public string Operation { get; } = operation;
}
