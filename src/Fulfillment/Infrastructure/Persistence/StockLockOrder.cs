namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// `FS19` — the application-fixed total lock order over a set of product
/// codes: distinct by <see cref="StringComparer.OrdinalIgnoreCase"/>, then
/// sorted ascending by the INVARIANT-UPPERCASED code with
/// <see cref="StringComparer.Ordinal"/>. Deliberately a pure, DB-free
/// function — <see cref="EfCoreStockItemRepository"/> is the only caller,
/// and factoring it out here lets the unit suite prove the ordering
/// property with no database at all.
/// </summary>
public static class StockLockOrder
{
    public static IReadOnlyList<string> Fix(IReadOnlyList<string> productCodes) =>
        [.. productCodes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code.ToUpperInvariant(), StringComparer.Ordinal)];
}
