using System.Text.Json;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// Backlog id 103, fix round 1 (CLAUDE.md defeat-list row 11): the web's
/// currency exponent table used to be guarded by a TypeScript test that
/// parsed THIS repository's own C# source as text with a regex, and that
/// guard was beaten three ways in review — a block comment, an
/// <c>#if false</c> region, two rows written on one line (defeat-list rows
/// 4, 5 and 11) — because none of those change what the .NET compiler
/// actually builds, and a test that never asks the compiler could not see
/// them.
///
/// This test compares the COMPILED <see cref="CurrencyExponent.NonDefaultExponents"/>
/// dictionary against the ONE committed data file the web imports directly,
/// <c>apps/web/src/lib/currency-exponents.json</c>
/// (<c>apps/web/src/lib/currency-exponent.ts</c>), in both directions, so a
/// comment or a dead region in either file can no longer fool the guard —
/// the compiler already discarded that text before this test ever runs.
/// <c>apps/web/src/lib/currency-exponent.test.ts</c> is this test's web-side
/// twin: it proves the web module does not silently diverge from the same
/// JSON file by ignoring its own import.
/// </summary>
public sealed class CurrencyExponentWebParityTests
{
    private static readonly string _jsonPath =
        RepositoryPaths.Find(Path.Combine("apps", "web", "src", "lib", "currency-exponents.json"));

    [Fact]
    public void NonDefaultExponents_MatchesTheWebsJsonTable_InBothDirections()
    {
        Assert.True(
            File.Exists(_jsonPath),
            $"expected the web's committed currency exponent table at \"{_jsonPath}\", but no file exists there — " +
            "a missing file must fail loudly, not be treated as an empty (and therefore silently agreeing) table.");

        var web = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_jsonPath));
        Assert.True(web is not null && web.Count > 0, $"\"{_jsonPath}\" parsed to null or an empty table.");

        var backend = CurrencyExponent.NonDefaultExponents;

        var missingFromWeb = backend.Keys
            .Where(code => !web!.ContainsKey(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        var missingFromBackend = web!.Keys
            .Where(code => !backend.ContainsKey(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        var valueMismatches = backend.Keys
            .Where(code => web.ContainsKey(code) && backend[code] != web[code])
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code => $"{code}: backend={backend[code]}, web={web[code]}")
            .ToArray();

        var noDifferences = missingFromWeb.Length == 0 && missingFromBackend.Length == 0 && valueMismatches.Length == 0;

        Assert.True(
            noDifferences,
            "the backend's compiled CurrencyExponent.NonDefaultExponents and the web's currency-exponents.json " +
            $"differ. In backend only: [{string.Join(", ", missingFromWeb)}]. " +
            $"In web only: [{string.Join(", ", missingFromBackend)}]. " +
            $"Value mismatches: [{string.Join(", ", valueMismatches)}].");
    }
}
