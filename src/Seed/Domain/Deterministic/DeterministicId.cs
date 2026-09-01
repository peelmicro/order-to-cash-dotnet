using System.Security.Cryptography;
using System.Text;

namespace OrderToCash.Seed.Domain.Deterministic;

/// <summary>
/// Derives a UUID-<b>shaped</b> identifier from a stable string namespace,
/// ported byte-for-byte from #7's
/// <c>apps/seed/src/deterministic.ts#deterministicId</c> so this seed writes
/// the SAME ids #7's seed writes: SHA-256 is SHA-256 regardless of runtime,
/// so the hex digest — and therefore every id derived from it — is
/// byte-identical between the TypeScript and this port.
///
/// Two calls with the same <c>namespace</c> always return the same
/// <see cref="Guid"/>: this is what makes "running the seed twice is a
/// no-op" (feature_list.json #12) fall out of upsert-by-id rather than
/// needing its own dedup mechanism. It is NOT a random UUIDv4 — the
/// version/variant nibbles are forced to satisfy the RFC 4122 v4 shape, but
/// the value carries none of a v4's actual entropy.
/// </summary>
public static class DeterministicId
{
    private const string SeedPrefix = "otc-seed:";

    public static Guid Of(string @namespace)
    {
        var hex = HashHex(SeedPrefix + @namespace);

        var timeLow = hex[..8];
        var timeMid = hex[8..12];

        // WART, PRESERVED DELIBERATELY (task instruction: "port all three
        // exactly, including the skipped hex character"). #7's own
        // deterministic.ts does not slice hex[12..16] — it slices
        // hex[13..16], silently skipping hex[12] entirely. Reproducing #7's
        // ids means reproducing #7's derivation, warts included, because
        // parity is judged by the resulting bytes, not by what the
        // "cleaner" derivation would have produced.
        var timeHiAndVersion = "4" + hex[13..16];

        var variantNibbleValue = (Convert.ToInt32(hex[16].ToString(), 16) & 0x3) | 0x8;
        var variantNibble = variantNibbleValue.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        var clockSeqAndReserved = variantNibble + hex[17..20];

        var node = hex[20..32];

        var uuid = $"{timeLow}-{timeMid}-{timeHiAndVersion}-{clockSeqAndReserved}-{node}";
        return Guid.Parse(uuid);
    }

    private static string HashHex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }
}
