namespace OrderToCash.Fulfillment.Application;

/// <summary>
/// <c>stock.reserve</c> resolved no known stock item on ANY line — there is
/// no carrier aggregate for a fact (design.md §3.3, §4.6). A TERMINAL refusal
/// (mapped to <c>NOT_FOUND</c>) — distinct from
/// <see cref="OrderToCash.SharedKernel.DomainError"/> the same way Orders'
/// <c>ReferenceDataNotFoundError</c> is not a <c>DomainError</c>: this is an
/// application-layer refusal above the domain, raised because the domain
/// service returned <c>NoCarrier</c> rather than because an aggregate
/// invariant was violated.
/// </summary>
public sealed class NoKnownStockItemError(string companyCode)
    : Exception($"No line of this request resolves to a known stock item under company '{companyCode}'.")
{
    public string CompanyCode { get; } = companyCode;
}

/// <summary><c>stock.replenish</c> named a <c>productCode</c> with no stock item under the request's <c>companyCode</c> — all-or-nothing (`FS14`): nothing is replenished.</summary>
public sealed class UnknownStockItemError(string companyCode, string productCode)
    : Exception($"No stock item for company '{companyCode}', product '{productCode}'.")
{
    public string CompanyCode { get; } = companyCode;

    public string ProductCode { get; } = productCode;
}

/// <summary>
/// design.md §4.4: <c>stock.release</c>'s step 2 (the authoritative,
/// lock-protected re-read) found a reservation whose <c>stock_id</c> was not
/// locked in step 1 — impossible in practice, since an order's reservations
/// are created once, under the stock locks, by <c>stock.reserve</c>. A
/// defensive branch, not an expected path: the service refuses to release
/// under a lock it does not hold, and lets the orchestrator retry
/// (mapped to the TRANSIENT code <c>UNAVAILABLE</c>, `FS21`).
/// </summary>
public sealed class ConcurrentReservationChangeError(string orderReference)
    : Exception($"Order '{orderReference}': a reservation referenced a stock row not locked in this transaction.")
{
    public string OrderReference { get; } = orderReference;
}
