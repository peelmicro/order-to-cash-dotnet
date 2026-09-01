using OrderToCash.Seed.Domain.Deterministic;

namespace OrderToCash.Seed.Domain.Data;

/// <summary>One seeded currency row — ported from #7's <c>data/currencies.data.ts</c>.</summary>
public sealed record CurrencySeed(Guid Id, string Code, string IsoNumber, string Symbol, int DecimalPoints);

/// <summary>
/// The three seeded currencies (feature_list.json #12 acceptance: "3
/// currencies"). Matches domain-model.md §2.1 / §7.2 — ISO 4217 alpha-3
/// codes; <see cref="CurrencySeed.DecimalPoints"/> is rendering metadata
/// only, never used in arithmetic (which stays integer minor units
/// everywhere in this codebase).
/// </summary>
public static class Currencies
{
    public static readonly IReadOnlyList<CurrencySeed> All =
    [
        new(DeterministicId.Of("currency:USD"), "USD", "840", "$", 2),
        new(DeterministicId.Of("currency:EUR"), "EUR", "978", "€", 2),
        new(DeterministicId.Of("currency:GBP"), "GBP", "826", "£", 2),
    ];

    public static Guid IdByCode(string code) =>
        All.FirstOrDefault(currency => currency.Code == code)?.Id
            ?? throw new InvalidOperationException($"currencyIdByCode: unknown currency code \"{code}\"");
}
