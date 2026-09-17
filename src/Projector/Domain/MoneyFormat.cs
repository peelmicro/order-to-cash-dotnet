using OrderToCash.SharedKernel;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// Renders a <see langword="long"/> minor-units money value with its
/// currency code for a timeline summary — <c>PR16</c> +
/// <c>timeline_money_reads_as_minor_units</c> (backlog id 100): SA-5 says
/// human formatting scales by the currency's own ISO 4217 minor-unit
/// exponent, so <c>(9245, "EUR")</c> -&gt; <c>"92.45 EUR"</c>, never the
/// unscaled <c>"9245 EUR"</c> a human would misread as a hundred times the
/// real amount. Delegates entirely to <see cref="MoneyText"/> — the ONE
/// implementation this repository's timeline summaries, seed fixtures and
/// notification emails all share, so they read alike by construction.
/// </summary>
public static class MoneyFormat
{
    public static string Of(long minorUnits, string currency) => MoneyText.Format(minorUnits, currency);
}
