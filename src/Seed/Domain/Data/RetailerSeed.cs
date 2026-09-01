using OrderToCash.Seed.Domain.Deterministic;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded retailer (buyer) row — ported from #7's <c>data/retailers.data.ts</c>.</summary>
public sealed record RetailerSeed(
    Guid Id,
    string Code,
    string Name,
    string Country,
    string Vat,
    string Gln,
    string CurrencyCode);

/// <summary>
/// The 7 retailers, exactly as #7 specifies (feature_list.json #12
/// acceptance: "7 retailers"). GLNs are sequences 1-7 (companies.data.ts
/// continues from 21 so GLNs stay unique across the whole catalogue).
/// </summary>
public static class Retailers
{
    public static readonly IReadOnlyList<RetailerSeed> All =
    [
        new(DeterministicId.Of("retailer:CarrefourEs"), "CarrefourEs", "Carrefour España", "ES", "ESA28425270", Gs1Identifiers.MakeGln(1), "EUR"),
        new(DeterministicId.Of("retailer:CarrefourFr"), "CarrefourFr", "Carrefour France", "FR", "FR45652014051", Gs1Identifiers.MakeGln(2), "EUR"),
        new(DeterministicId.Of("retailer:LeroyMerlinEs"), "LeroyMerlinEs", "Leroy Merlin España", "ES", "ESA28398950", Gs1Identifiers.MakeGln(3), "EUR"),
        new(DeterministicId.Of("retailer:LeroyMerlinFr"), "LeroyMerlinFr", "Leroy Merlin France", "FR", "FR32384657943", Gs1Identifiers.MakeGln(4), "EUR"),
        new(DeterministicId.Of("retailer:AldiEs"), "AldiEs", "Aldi España", "ES", "ESA65037725", Gs1Identifiers.MakeGln(5), "EUR"),
        new(DeterministicId.Of("retailer:AldiDe"), "AldiDe", "Aldi Deutschland", "DE", "DE812631079", Gs1Identifiers.MakeGln(6), "EUR"),
        new(DeterministicId.Of("retailer:AldiGb"), "AldiGb", "Aldi UK", "GB", "GB245012348", Gs1Identifiers.MakeGln(7), "GBP"),
    ];

    public static RetailerSeed ByCode(string code) =>
        All.FirstOrDefault(retailer => retailer.Code == code)
            ?? throw new InvalidOperationException($"retailerByCode: unknown retailer code \"{code}\"");
}
