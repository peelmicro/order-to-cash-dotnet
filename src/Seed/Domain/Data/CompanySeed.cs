using OrderToCash.Seed.Domain.Deterministic;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded company (supplier) row — ported from #7's <c>data/companies.data.ts</c>.</summary>
public sealed record CompanySeed(
    Guid Id,
    string Code,
    string Name,
    string Country,
    string Vat,
    string Gln,
    string CurrencyCode);

/// <summary>
/// 22 companies (suppliers), varied countries (feature_list.json #12
/// acceptance: "20+ companies"). GLN sequences start at 21 — the 7
/// retailers already own sequences 1-7 (<see cref="Retailers"/>) and GLNs
/// must be unique across the whole seeded catalogue.
/// </summary>
public static class Companies
{
    private static readonly (string Code, string Name, string Country, string Vat, string CurrencyCode)[] _raw =
    [
        // ES (4)
        ("IBERFOODS", "Iberian Foods Distribution SA", "ES", "ESA80907397", "EUR"),
        ("SPANATURAL", "Hispania Natural Foods SL", "ES", "ESB82591744", "EUR"),
        ("TOOLIBERIA", "Herramientas Ibéricas SA", "ES", "ESA84606310", "EUR"),
        ("MEDFRESH", "Mediterráneo Fresh Goods SL", "ES", "ESB63022260", "EUR"),
        // FR (3)
        ("FRESHFR", "Fraîcheur de France SARL", "FR", "FR76403355947", "EUR"),
        ("OUTILFRANCE", "Outillage de France SAS", "FR", "FR89552120222", "EUR"),
        ("GALLIAGOODS", "Gallia Goods Distribution SA", "FR", "FR23334028554", "EUR"),
        // DE (3)
        ("GERMANFOODS", "Deutsche Lebensmittel GmbH", "DE", "DE136695970", "EUR"),
        ("BAUWERK", "Bauwerk Werkzeuge GmbH", "DE", "DE811115660", "EUR"),
        ("RHEINGOODS", "Rheingold Handels GmbH", "DE", "DE147426685", "EUR"),
        // GB (3)
        ("UKDISTRIB", "British Isles Distribution Ltd", "GB", "GB434031494", "GBP"),
        ("LONDONTOOLS", "London Tools Supply Ltd", "GB", "GB113292750", "GBP"),
        ("ALBIONFOODS", "Albion Foods Ltd", "GB", "GB980780684", "GBP"),
        // IT (3)
        ("ITALPASTA", "Pastificio Italiano SRL", "IT", "IT00743110157", "EUR"),
        ("ROMATOOLS", "Attrezzi di Roma SRL", "IT", "IT01654060157", "EUR"),
        ("MILANOGOODS", "Milano Distribuzione SRL", "IT", "IT12842760151", "EUR"),
        // PT (2)
        ("LUSOFOODS", "Luso Alimentos Lda", "PT", "PT502757191", "EUR"),
        ("PORTOTOOLS", "Porto Ferramentas Lda", "PT", "PT503504457", "EUR"),
        // NL (2)
        ("DUTCHGOODS", "Nederlandse Groothandel BV", "NL", "NL805806053B01", "EUR"),
        ("HOLLANDTOOLS", "Holland Gereedschap BV", "NL", "NL818838663B01", "EUR"),
        // BE (2)
        ("BENELUXFOODS", "Benelux Voeding NV", "BE", "BE0429646425", "EUR"),
        ("BRUSSELSTOOLS", "Brussels Outillage NV", "BE", "BE0475747019", "EUR"),
    ];

    public static readonly IReadOnlyList<CompanySeed> All = _raw
        .Select((entry, index) => new CompanySeed(
            DeterministicId.Of($"company:{entry.Code}"),
            entry.Code,
            entry.Name,
            entry.Country,
            entry.Vat,
            Gs1Identifiers.MakeGln(21 + index),
            entry.CurrencyCode))
        .ToArray();

    public static CompanySeed ByCode(string code) =>
        All.FirstOrDefault(company => company.Code == code)
            ?? throw new InvalidOperationException($"companyByCode: unknown company code \"{code}\"");
}
