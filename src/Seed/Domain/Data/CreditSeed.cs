using OrderToCash.Seed.Domain.Deterministic;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded credit line — ported from #7's <c>data/credits.data.ts</c>.</summary>
public sealed record CreditSeed(
    Guid Id,
    string Code,
    string RetailerCode,
    string CompanyCode,
    long CreditLimit,
    string CurrencyCode);

/// <summary>
/// A credit limit for every retailer against its "primary" supplier
/// (<c>CR-000001</c>..<c>CR-000007</c>, in <see cref="Retailers"/> order —
/// the same pair the sample sagas place their orders against), PLUS a
/// baseline credit line for every retailer against every OTHER company
/// (continuing from <c>CR-000008</c>) — ported from #7's
/// <c>data/credits.data.ts</c>, including its own baseline-coverage
/// amendment: without the baseline, an order against any non-primary
/// supplier would have NO credit line at all. 500 000 minor units
/// (EUR/GBP 5 000,00) for every line — "deliberately modest" so a genuine
/// over-limit rejection stays demoable.
/// </summary>
public static class Credits
{
    private const long CreditLimitMinorUnits = 500_000;

    private static readonly IReadOnlyDictionary<string, string> _primarySupplierByRetailer =
        new Dictionary<string, string>
        {
            ["CarrefourEs"] = "IBERFOODS",
            ["CarrefourFr"] = "FRESHFR",
            ["LeroyMerlinEs"] = "TOOLIBERIA",
            ["LeroyMerlinFr"] = "OUTILFRANCE",
            ["AldiEs"] = "SPANATURAL",
            ["AldiDe"] = "GERMANFOODS",
            ["AldiGb"] = "UKDISTRIB",
        };

    private static readonly IReadOnlyList<CreditSeed> _primaryCredits = Retailers.All
        .Select((retailer, index) =>
        {
            var companyCode = PrimarySupplierOf(retailer.Code);
            return new CreditSeed(
                DeterministicId.Of($"credit:{retailer.Code}:{companyCode}"),
                BusinessReference.Credit(index + 1),
                retailer.Code,
                companyCode,
                CreditLimitMinorUnits,
                retailer.CurrencyCode);
        })
        .ToArray();

    private static readonly IReadOnlyList<CreditSeed> _baselineCredits = Retailers.All
        .SelectMany((retailer, retailerIndex) =>
        {
            var primary = PrimarySupplierOf(retailer.Code);
            var otherCompanies = Companies.All.Where(company => company.Code != primary).ToArray();
            return otherCompanies.Select((company, companyIndex) => new CreditSeed(
                DeterministicId.Of($"credit:baseline:{retailer.Code}:{company.Code}"),
                BusinessReference.Credit(
                    _primaryCredits.Count + (retailerIndex * (Companies.All.Count - 1)) + companyIndex + 1),
                retailer.Code,
                company.Code,
                CreditLimitMinorUnits,
                retailer.CurrencyCode));
        })
        .ToArray();

    public static readonly IReadOnlyList<CreditSeed> All = [.. _primaryCredits, .. _baselineCredits];

    public static string PrimarySupplierOf(string retailerCode) =>
        _primarySupplierByRetailer.TryGetValue(retailerCode, out var companyCode)
            ? companyCode
            : throw new InvalidOperationException($"primarySupplierOf: no primary supplier configured for retailer \"{retailerCode}\"");

    public static CreditSeed ByRetailerAndCompany(string retailerCode, string companyCode) =>
        All.FirstOrDefault(credit => credit.RetailerCode == retailerCode && credit.CompanyCode == companyCode)
            ?? throw new InvalidOperationException(
                $"creditByRetailerAndCompany: no credit line for {retailerCode}/{companyCode}");
}
