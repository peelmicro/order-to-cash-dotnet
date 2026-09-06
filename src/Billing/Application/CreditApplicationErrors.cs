namespace OrderToCash.Billing.Application;

/// <summary>
/// `BC3` — no credit line exists for the named <c>(retailerCode, companyCode)</c>
/// pair. Lives in <c>Application/</c>, not <c>Domain/</c>: this is a
/// contract violation of the incoming command, not a statement about a
/// credit line's state — the domain layer has no vocabulary for "the pair
/// you named does not exist" (design.md §5.4, mirroring
/// <c>src/Fulfillment/Application/StockApplicationErrors.cs</c>).
/// </summary>
public sealed class CreditLineNotFoundError(string retailerCode, string companyCode)
    : Exception($"No credit line exists for retailer '{retailerCode}', company '{companyCode}'.")
{
    public string RetailerCode { get; } = retailerCode;

    public string CompanyCode { get; } = companyCode;
}

/// <summary>`BC4` — the requested currency differs from the resolved credit line's. An order is single-currency by M2/O1 and a credit line's currency is its retailer's, so a mismatch is a defective message, not a statement about the buyer's credit.</summary>
public sealed class CreditCurrencyMismatchError(string expected, string received)
    : Exception($"Requested currency '{received}' differs from the credit line's currency '{expected}'.")
{
    public string Expected { get; } = expected;

    public string Received { get; } = received;
}
