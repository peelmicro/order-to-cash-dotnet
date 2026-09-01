using OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded stock row — ported from #7's <c>data/stock.data.ts</c>.</summary>
public sealed record StockSeed(Guid Id, string CompanyCode, string ProductCode, int Units, int ReservedUnits, int LowStockThreshold);

/// <summary>
/// Initial Fulfillment stock — derived straight from <see cref="SagaFixtures.All"/>
/// (single source of truth) rather than duplicating quantities, so the
/// stock table and the fabricated reservation/despatch history can never
/// drift apart, PLUS a baseline row per (company, product) for every
/// company the sagas never touch — so no company can ever hit a
/// <c>stock.reserve</c> <c>NOT_FOUND</c> wall regardless of which product a
/// later demo order names. Ported from #7's <c>data/stock.data.ts</c>.
/// </summary>
public static class StockCatalog
{
    private const int InitialUnitsOnHand = 500;
    private const int LowStockThreshold = 20;

    private sealed class PairAccumulator
    {
        public required string CompanyCode { get; init; }

        public required string ProductCode { get; init; }

        public int Consumed { get; set; }
    }

    public static readonly IReadOnlyList<StockSeed> All = Build();

    private static IReadOnlyList<StockSeed> Build()
    {
        var pairs = new Dictionary<string, PairAccumulator>(StringComparer.Ordinal);

        foreach (var saga in SagaFixtures.All)
        {
            foreach (var reservation in saga.Reservations)
            {
                var key = $"{reservation.CompanyCode}::{reservation.ProductCode}";
                if (!pairs.TryGetValue(key, out var accumulator))
                {
                    accumulator = new PairAccumulator { CompanyCode = reservation.CompanyCode, ProductCode = reservation.ProductCode };
                    pairs[key] = accumulator;
                }

                if (reservation.Status == "consumed")
                {
                    accumulator.Consumed += reservation.Units;
                }
            }
        }

        var sagaCoveredCompanies = pairs.Values.Select(pair => pair.CompanyCode).ToHashSet(StringComparer.Ordinal);

        var sagaDerived = pairs.Values.Select(pair =>
        {
            var units = InitialUnitsOnHand - pair.Consumed;
            if (units < 0)
            {
                throw new InvalidOperationException(
                    $"Stock: ({pair.CompanyCode}, {pair.ProductCode}) would go negative — raise InitialUnitsOnHand");
            }

            return new StockSeed(
                SagaFixtures.StockRowId(pair.CompanyCode, pair.ProductCode),
                pair.CompanyCode,
                pair.ProductCode,
                units,
                0,
                LowStockThreshold);
        });

        var baseline = Companies.All
            .Where(company => !sagaCoveredCompanies.Contains(company.Code))
            .SelectMany(company => Products.All.Select(product => new StockSeed(
                SagaFixtures.StockRowId(company.Code, product.Code),
                company.Code,
                product.Code,
                InitialUnitsOnHand,
                0,
                LowStockThreshold)));

        return [.. sagaDerived.Concat(baseline).OrderBy(s => s.CompanyCode + s.ProductCode, StringComparer.Ordinal)];
    }
}
