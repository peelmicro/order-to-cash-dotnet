using OrderToCash.Seed.Domain.Deterministic;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded product row — ported from #7's <c>data/products.data.ts</c>.</summary>
public sealed record ProductSeed(
    Guid Id,
    string Code,
    string Ean,
    string Name,
    string Description,
    long Price,
    string CurrencyCode);

/// <summary>
/// 12 products across the 3 currencies (feature_list.json #12 acceptance:
/// "10+ products"). PRD-0001's price (24999 = EUR 249.99) is chosen
/// deliberately so a single line of quantity 1 already totals ".99" — the
/// line the cancelled sample saga (<see cref="Sagas.SagaFixtures"/>) uses.
/// </summary>
public static class Products
{
    private static readonly (string Code, string Name, string Description, long Price, string CurrencyCode)[] _raw =
    [
        // EUR (8) — PRD-0001 is the ".99 line" (see class summary above).
        ("PRD-0001", "Ration Pack Bundle", "Mixed grocery ration pack, 1 unit", 24999, "EUR"),
        ("PRD-0002", "Pasta 500g Case (24u)", "Case of 24 x 500g dried pasta", 1849, "EUR"),
        ("PRD-0003", "Olive Oil 1L Case (12u)", "Case of 12 x 1L extra virgin olive oil", 2295, "EUR"),
        ("PRD-0004", "Claw Hammer 16oz", "Forged steel claw hammer, 16oz head", 1450, "EUR"),
        ("PRD-0005", "Screwdriver Set 6pc", "6-piece flathead/Phillips screwdriver set", 825, "EUR"),
        ("PRD-0006", "Paint Roller Kit", "Roller, tray and 2 refill sleeves", 645, "EUR"),
        ("PRD-0007", "Garden Hose 20m", "Reinforced PVC garden hose, 20 metres", 3275, "EUR"),
        ("PRD-0008", "Laundry Detergent 5L", "5L concentrated liquid laundry detergent", 1489, "EUR"),
        // GBP (2)
        ("PRD-0009", "English Breakfast Tea 250g", "250g loose-leaf English breakfast tea", 379, "GBP"),
        ("PRD-0010", "Digestive Biscuits 400g", "400g pack of digestive biscuits", 165, "GBP"),
        // USD (2)
        ("PRD-0011", "Maple Syrup 1L", "1L pure maple syrup", 1749, "USD"),
        ("PRD-0012", "Almond Butter 500g", "500g smooth almond butter", 895, "USD"),
    ];

    public static readonly IReadOnlyList<ProductSeed> All = _raw
        .Select((entry, index) => new ProductSeed(
            DeterministicId.Of($"product:{entry.Code}"),
            entry.Code,
            Gs1Identifiers.MakeEan13(index + 1),
            entry.Name,
            entry.Description,
            entry.Price,
            entry.CurrencyCode))
        .ToArray();

    public static ProductSeed ByCode(string code) =>
        All.FirstOrDefault(product => product.Code == code)
            ?? throw new InvalidOperationException($"productByCode: unknown product code \"{code}\"");
}
