using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>OB1</c>, design.md §8.4's three cases over the SEVEN guarded files —
/// modelled case-for-case on <see cref="IdempotentConsumerParityTests"/> and
/// reusing its helper shapes (<c>RepositoryPaths.Find</c>, banner stripping,
/// namespace-line stripping, the service-token scan), so a reader who has
/// read one has read both. Pure text, <see cref="System.IO"/> only, no
/// container. <see cref="IdempotentConsumerParityTests"/> itself is never
/// modified by this class — a landed guard is not widened to accommodate a
/// new one.
/// </summary>
public sealed partial class OutboxRelayParityTests
{
    // The seven files design.md §8.3/§8.4 hold in the OB1 set, after the
    // 2026-09-05 gate ruling widened it back to #7's full scope.
    private static readonly string[] _guardedFileNames =
    [
        "OutboxRelay.cs",
        "OutboxRelayOptions.cs",
        "OutboxRelayBackgroundService.cs",
        "OutboxEnvelopeMapper.cs",
        "KafkaFactPublisher.cs",
        "OutboxWriter.cs",
        "KafkaOptions.cs",
    ];

    private const string CanonicalService = "Orders";
    private const string CanonicalDirectory = "src/Orders/Infrastructure/Outbox";

    // The five service-name tokens the canonical files may never carry
    // outside their two normalised regions (design.md §8.4 case 2).
    private static readonly string[] _serviceTokens = ["Orders", "Fulfillment", "Billing", "Projector", "Notifications"];

    // design.md §8.4 case 2 — the two additions this guard's widened scope
    // needed over IdempotentConsumerParityTests' own whitelist:
    // System.Text.Json (OutboxWriter's payload serialisation) and
    // .Domain.Events (OutboxWriter's FactEvent parameter).
    private static readonly string[] _usingWhitelistSuffixes =
    [
        "System.Data",
        "System.Text.Json",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions",
        "Confluent.Kafka",
        "OrderToCash.SharedKernel",
        "OrderToCash.Contracts",
        ".Application.Ports",
        ".Domain.Events",
        ".Infrastructure.Persistence",
        ".Infrastructure.Persistence.Entities",
        ".Infrastructure.Outbox",
    ];

    [Fact]
    public void HoldsEveryWriteModelsCopyOfTheOutboxRelayFamilyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine()
    {
        var root = RepositoryPaths.Find(string.Empty);

        var copies = DiscoverCopyServices(root);
        // Non-vacuity: at least THREE members, named — the assertion that
        // says out loud the guard is armed rather than comparing Orders
        // with itself (design.md §8.4 case 1).
        Assert.True(copies.Count >= 3, $"Expected at least 3 services owning the outbox-relay family; found {copies.Count}: {string.Join(", ", copies)}.");

        foreach (var fileName in _guardedFileNames)
        {
            var canonicalPath = $"{CanonicalDirectory}/{fileName}";
            var canonicalLines = NormalizedLines(ReadFile(root, canonicalPath));

            foreach (var service in copies.Where(s => s != CanonicalService))
            {
                var copyPath = $"src/{service}/Infrastructure/Outbox/{fileName}";
                var copyLines = NormalizedLines(ReadFile(root, copyPath));

                var (matches, firstDifferingLine, canonicalLine, copyLine) = CompareLines(canonicalLines, copyLines);

                Assert.True(
                    matches,
                    $"{service}'s {fileName} (at {copyPath}) diverges from the canonical {canonicalPath} " +
                    $"outside the banner, the namespace line and the using lines, at normalised line {firstDifferingLine}: " +
                    $"canonical=\"{canonicalLine}\" vs copy=\"{copyLine}\".");
            }
        }
    }

    [Fact]
    public void KeepsTheCanonicalFamilyAdoptableVerbatimNamingNoServiceAndImportingNothingServiceSpecific()
    {
        var root = RepositoryPaths.Find(string.Empty);

        foreach (var fileName in _guardedFileNames)
        {
            var relativePath = $"{CanonicalDirectory}/{fileName}";
            AssertAdoptable(ReadFile(root, relativePath), relativePath);
        }
    }

    [Fact]
    public void RequiresTheOutboxRelayFamilyFromEveryServiceThatOwnsARelationalOutboxConfiguration()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var servicesWithOutbox = _serviceTokens.Where(service => HasRelationalOutboxConfiguration(root, service)).ToList();

        var missing = servicesWithOutbox
            .SelectMany(service => _guardedFileNames
                .Where(fileName => !File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Outbox/{fileName}")))
                .Select(fileName => $"{service}/{fileName}"))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"service(s)/file(s) with a relational outbox configuration but no copy of the outbox-relay family: {string.Join(", ", missing)}.");
    }

    /// <summary>Every <c>src/&lt;Service&gt;</c> carrying a relational <c>OutboxMessageConfiguration.cs</c> AND every one of the seven guarded files — the discriminator design.md §8.4 fixes, read from the filesystem, never from a hand-maintained list.</summary>
    private static List<string> DiscoverCopyServices(string root) =>
        _serviceTokens
            .Where(service => HasRelationalOutboxConfiguration(root, service))
            .Where(service => _guardedFileNames.All(fileName => File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Outbox/{fileName}"))))
            .ToList();

    private static bool HasRelationalOutboxConfiguration(string root, string service) =>
        File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs"));

    private static string ReadFile(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    /// <summary>Strips the banner, the single <c>namespace</c> line and every <c>using</c> line (plain or aliased) — the three regions design.md §8.4 normalises. Everything else is compared verbatim, line by line.</summary>
    private static string[] NormalizedLines(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bodyStart = 0;

        while (bodyStart < lines.Length && IsBannerLine(lines[bodyStart]))
        {
            bodyStart++;
        }

        return lines
            .Skip(bodyStart)
            .Where(line => !NamespaceLineRegex().IsMatch(line))
            .Where(line => !UsingDirectiveRegex().IsMatch(line))
            .ToArray();
    }

    private static (bool Matches, int FirstDifferingLine, string CanonicalLine, string CopyLine) CompareLines(string[] canonical, string[] copy)
    {
        var length = Math.Max(canonical.Length, copy.Length);
        for (var i = 0; i < length; i++)
        {
            var canonicalLine = i < canonical.Length ? canonical[i] : "<end of file>";
            var copyLine = i < copy.Length ? copy[i] : "<end of file>";

            if (!string.Equals(canonicalLine, copyLine, StringComparison.Ordinal))
            {
                return (false, i + 1, canonicalLine, copyLine);
            }
        }

        return (true, -1, string.Empty, string.Empty);
    }

    private static bool IsBannerLine(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static void AssertAdoptable(string content, string relativePath)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bannerLineCount = lines.TakeWhile(IsBannerLine).Count();

        for (var i = 0; i < lines.Length; i++)
        {
            var isBanner = i < bannerLineCount;
            var isNamespaceLine = NamespaceLineRegex().IsMatch(lines[i]);
            if (isBanner || isNamespaceLine)
            {
                continue;
            }

            var line = lines[i];

            // A `using` line — plain or aliased — matching the whitelist is
            // exempt from the token scan below (design.md §8.4's own
            // carve-out, widened for the alias form §8.2.1 introduces).
            var usingMatch = UsingDirectiveRegex().Match(line);
            if (usingMatch.Success)
            {
                var importedNamespace = usingMatch.Groups["namespace"].Value;
                var allowed = _usingWhitelistSuffixes.Any(entry =>
                    string.Equals(importedNamespace, entry, StringComparison.Ordinal) ||
                    importedNamespace.EndsWith(entry, StringComparison.Ordinal) ||
                    importedNamespace.StartsWith(entry + ".", StringComparison.Ordinal) ||
                    // The aliased form (design.md §8.2.1) names a TYPE, not a
                    // namespace — "…Infrastructure.Persistence.OrdersDbContext"
                    // never literally ENDS with ".Infrastructure.Persistence",
                    // so a middle-segment match is needed for it too.
                    importedNamespace.Contains(entry + ".", StringComparison.Ordinal));

                Assert.True(allowed, $"{relativePath}:{i + 1} imports '{importedNamespace}', which is outside design.md §8.4's using whitelist.");
                continue;
            }

            foreach (var token in _serviceTokens)
            {
                Assert.False(
                    ContainsTokenCaseInsensitive(line, token),
                    $"{relativePath}:{i + 1} names '{token}' outside the banner/namespace line: \"{line.Trim()}\"");
            }
        }
    }

    private static bool ContainsTokenCaseInsensitive(string line, string token) =>
        line.Contains(token, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\s*namespace\s")]
    private static partial Regex NamespaceLineRegex();

    /// <summary>
    /// Matches BOTH <c>using X.Y.Z;</c> and the aliased form
    /// <c>using Alias = X.Y.Z;</c> design.md §8.2.1 introduces —
    /// <see cref="IdempotentConsumerParityTests"/>' own
    /// <c>UsingDirectiveRegex</c> matches only the plain form, and is left
    /// untouched (design.md §8.4's own instruction).
    /// </summary>
    [GeneratedRegex(@"^\s*using\s+(?:[A-Za-z0-9_]+\s*=\s*)?(?<namespace>[A-Za-z0-9_.]+)\s*;")]
    private static partial Regex UsingDirectiveRegex();
}
