namespace OrderToCash.SharedKernel;

/// <summary>
/// ISO 4217's minor-unit exponent for a currency code — SA-5
/// (specs/shared/openapi.yaml's Money section): "Formatting for humans
/// happens ... from the currency's ISO 4217 minor-unit exponent (EUR, GBP
/// and USD are 2; JPY is 0; BHD is 3). No response carries that exponent:
/// it is a property of the currency code itself." So a formatter reading
/// only a code (never a locale, never a per-request field) must be able to
/// answer this question on its own — this is that answer.
/// </summary>
/// <remarks>
/// <b>Ported-idiom ledger (corrected, backlog id 100 fix round 1 — D1/D5).</b>
/// #7 (TypeScript) originally answered this with
/// <c>Intl.NumberFormat(locale, { style: 'currency', currency })
/// .resolvedOptions().maximumFractionDigits</c>
/// (<c>apps/web/app/lib/money.ts:22</c>, and this feature's first-round
/// port to <c>packages/shared-kernel/src/domain/currency-exponent.ts</c> —
/// not <c>apps/projector/src/domain/currency-exponent.ts</c>, which does
/// not exist; that citation in the original ledger row was wrong). That was
/// a defect, not merely a different valid source: ECMA-402's <c>Intl</c>
/// reports CLDR's <em>display</em>-digits convention, which disagrees with
/// the ISO 4217 minor-unit exponent for several currencies (measured on
/// Node v24.19.0: <c>AFN ALL IRR KPW LAK LBP MGA MMK SOS SYP YER HUF COP
/// IDR</c> all report 0 display digits under <c>Intl</c> while ISO 4217
/// gives each of them 2), so it only happened to answer correctly for the
/// two currencies this feature's original acceptance examples probed (JPY,
/// BHD). Fix round 1 replaced #7's <c>currencyExponent</c> with the
/// identical ISO 4217 literal table below (see D3's whole-table guard),
/// so both repositories now read the same fixed list rather than one of
/// them asking a locale API a question it does not answer.
///
/// .NET has no equivalent entry point. <see cref="System.Globalization.RegionInfo"/>
/// maps a REGION (a country/territory, e.g. <c>"JP"</c>) to an
/// <c>ISOCurrencySymbol</c> — the direction this feature needs inverted —
/// and <see cref="System.Globalization.NumberFormatInfo.CurrencyDecimalDigits"/>
/// is a property of a <em>culture's</em> <c>NumberFormat</c>, itself owned
/// by the SAME region, not by a currency code: several currencies (EUR)
/// map to dozens of regions, and nothing in the BCL inverts "currency code
/// -&gt; region" at all. Even doing that inversion by hand — iterating every
/// installed culture, keeping the first whose <c>ISOCurrencySymbol</c>
/// matches — does not recover the right answer on this machine's ICU/CLDR
/// data: a throwaway probe (not committed; see
/// progress/impl_timeline_money_reads_as_minor_units.md) found
/// <c>CurrencyDecimalDigits == 2</c> for BOTH a JPY-matching region ("JP")
/// and a BHD-matching region ("BH"), i.e. the exact two currencies this
/// feature's own acceptance examples require to render as 0 and 3
/// fraction digits. <c>NumberFormatInfo.CurrencyDecimalDigits</c> is a
/// culture's DISPLAY convention (CLDR's <c>currencyFormatLength</c>/
/// <c>symbol</c> data defaults to 2 almost everywhere), not the ISO 4217
/// minor-unit exponent — so it answers a different question than the one
/// SA-5 asks, decisively, not merely "a candidate to check" as originally
/// supposed. No BCL API returns an ISO 4217 minor-unit exponent for a
/// currency CODE. So this table is hand-written, sourced from the ISO 4217
/// published exponent lists — the zero-decimal and three/four-decimal
/// currency sets the ISO 4217 maintenance agency publishes and that are
/// commonly reproduced (e.g. Wikipedia's "ISO 4217" article, Active codes
/// table, "Minor unit" column). Every currency not listed defaults to 2,
/// which covers EUR, USD and GBP — the only three <c>CurrencySeed.All</c>
/// currently seeds.
/// </remarks>
public static class CurrencyExponent
{
    private const int DefaultExponent = 2;

    /// <summary>
    /// Currencies whose ISO 4217 minor-unit exponent is NOT 2 — every other
    /// code defaults to <see cref="DefaultExponent"/>. Exposed read-only
    /// (backlog id 103, fix round 1) so a test can compare the COMPILED
    /// table against <c>apps/web/src/lib/currency-exponents.json</c> — the
    /// single committed data file the web imports directly — rather than
    /// parsing this file's C# text, which a comment, an <c>#if false</c>
    /// region or a second entry on one line could fool. See
    /// <c>tests/SharedKernel.UnitTests/CurrencyExponentWebParityTests.cs</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> NonDefaultExponents = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // Zero decimal digits.
        ["BIF"] = 0,
        ["CLP"] = 0,
        ["DJF"] = 0,
        ["GNF"] = 0,
        ["ISK"] = 0,
        ["JPY"] = 0,
        ["KMF"] = 0,
        ["KRW"] = 0,
        ["PYG"] = 0,
        ["RWF"] = 0,
        ["UGX"] = 0,
        ["VND"] = 0,
        ["VUV"] = 0,
        ["XAF"] = 0,
        ["XOF"] = 0,
        ["XPF"] = 0,

        // Three decimal digits.
        ["BHD"] = 3,
        ["IQD"] = 3,
        ["JOD"] = 3,
        ["KWD"] = 3,
        ["LYD"] = 3,
        ["OMR"] = 3,
        ["TND"] = 3,

        // Four decimal digits.
        ["CLF"] = 4,
        ["UYW"] = 4,

        // Fund codes with a published ISO 4217 minor-unit exponent of zero
        // (backlog id 100, fix round 1 — D1/D3): UYI was missing from the
        // original table.
        ["UYI"] = 0,
    };

    /// <summary>The ISO 4217 minor-unit exponent for <paramref name="currency"/> — an unrecognised or malformed code defaults to 2, the common case (matches #7's <c>currencyExponent</c>, which falls back the same way).</summary>
    public static int Of(string currency) =>
        NonDefaultExponents.TryGetValue(currency, out var exponent) ? exponent : DefaultExponent;
}
