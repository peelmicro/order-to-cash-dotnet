using System.Globalization;

namespace OrderToCash.Projector.Infrastructure.Persistence;

/// <summary>
/// The wire format every instant is stored in — the SAME format as
/// <c>OrderToCash.Contracts.Wire.InstantJsonConverter</c> and
/// <c>MongoSeedWriter</c>'s own private <c>Iso()</c> helper: three fraction
/// digits, a literal <c>Z</c>. Never <c>"O"</c>, which would write seven
/// fraction digits and <c>+00:00</c> and break <c>PR14</c>'s <c>$max</c> and
/// <c>PR10</c>'s string sort on the same document (<c>PR41</c>, ledger
/// <b>L21</b>).
/// </summary>
public static class InstantWire
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public static string Of(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
}
