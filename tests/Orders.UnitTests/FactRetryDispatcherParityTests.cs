using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OR2, design.md §3.4's three cases — pure text through <see cref="System.IO"/>,
/// the <c>IdempotentConsumerParityTests</c> shape, over a NEW canonical
/// file rather than widening that one (design.md §3.4's own instruction).
/// </summary>
public sealed partial class FactRetryDispatcherParityTests
{
    private const string CanonicalRelativePath = "src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs";

    // The three service-name tokens the canonical/copy files may never
    // carry outside their two normalised regions.
    private static readonly string[] _serviceTokens = ["Orders", "Projector", "Notifications"];

    // "matched by suffix rather than by literal text" — this file's own
    // whitelist (design.md §3.2's banner), narrower than IdempotentConsumer's
    // because FactRetryDispatcher needs no EF Core / SqlClient namespace at
    // all. Feature observability_reliability (phase 14, group A3h) adds two
    // more: System.Diagnostics (Stopwatch, otc_fact_processing_latency_ms)
    // and .Infrastructure.Observability (OtcMetrics, each service's own
    // copy under the SAME namespace-suffix shape .Application.Ports already
    // establishes).
    private static readonly string[] _usingWhitelistSuffixes =
    [
        "System.Diagnostics",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        ".Application.Ports",
        ".Infrastructure.Observability",
    ];

    private static readonly string[] _serviceScopedUsingSuffixes = [".Application.Ports", ".Infrastructure.Observability"];

    /// <summary>The literal expected set — never derived from which copies happen to exist, so a violation cannot remove itself from the population.</summary>
    private static readonly string[] _expectedServicesWithACopy = ["Notifications", "Orders", "Projector"];

    [Fact]
    public void HoldsEveryFactConsumingServicesCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var canonical = NormalizeCanonical(ReadFile(root, CanonicalRelativePath));

        foreach (var service in _expectedServicesWithACopy)
        {
            if (service == "Orders")
            {
                continue; // the canonical itself.
            }

            var copyPath = $"src/{service}/Infrastructure/Messaging/FactRetryDispatcher.cs";
            var copy = NormalizeCanonical(ReadFile(root, copyPath));

            Assert.True(
                canonical == copy,
                $"{service}'s FactRetryDispatcher.cs (at {copyPath}) diverges from the canonical {CanonicalRelativePath} outside the banner and the namespace line.");
        }
    }

    [Fact]
    public void KeepsTheCanonicalAdoptableVerbatimNamingNoServiceAndReferencingNothingServiceSpecific()
    {
        var root = RepositoryPaths.Find(string.Empty);
        AssertAdoptable(ReadFile(root, CanonicalRelativePath), CanonicalRelativePath);
    }

    /// <summary>
    /// Discovery by <c>Presentation/*FactsConsumer.cs</c>, EXCLUDING
    /// <c>tests/</c> BY PATH — never "has a FactRetryDispatcher.cs copy",
    /// which is the self-selecting filter design.md §3.4 names: a violation
    /// (a service that dropped its copy) would remove itself from that
    /// population instead of failing it. The discovered set is asserted
    /// equal to the LITERAL expected set, with the rest derived by
    /// subtraction.
    /// </summary>
    [Fact]
    public void RequiresACopyOfTheDispatcherFromEveryServiceThatOwnsAFactConsumer_AndFromNoOther()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var srcRoot = Path.Combine(root, "src");

        var servicesWithFactConsumer = Directory.EnumerateFiles(srcRoot, "*FactsConsumer.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Presentation{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(srcRoot, path).Split(Path.DirectorySeparatorChar)[0])
            .Distinct()
            .OrderBy(service => service, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(_expectedServicesWithACopy, servicesWithFactConsumer);

        foreach (var service in servicesWithFactConsumer)
        {
            var copyPath = Path.Combine(root, $"src/{service}/Infrastructure/Messaging/FactRetryDispatcher.cs");
            Assert.True(File.Exists(copyPath), $"{service} owns a fact consumer but has no {copyPath}.");
        }

        // And from no other — every OTHER src/<Service> must carry no copy.
        var everyService = Directory.EnumerateDirectories(srcRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Where(name => name is not ("SharedKernel" or "Contracts" or "Cqrs"))
            .ToList();

        foreach (var service in everyService.Except(servicesWithFactConsumer))
        {
            var copyPath = Path.Combine(root, $"src/{service}/Infrastructure/Messaging/FactRetryDispatcher.cs");
            Assert.False(File.Exists(copyPath), $"{service} owns no fact consumer but carries a copy at {copyPath}.");
        }
    }

    private static string ReadFile(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static string NormalizeCanonical(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bodyStart = 0;

        while (bodyStart < lines.Length && IsBannerLine(lines[bodyStart]))
        {
            bodyStart++;
        }

        var service = ExtractNamespaceService(lines, bodyStart);

        var body = lines.Skip(bodyStart)
            .Where(line => !NamespaceLineRegex().IsMatch(line))
            .Select(line => NormalizeOwnServiceUsingLine(line, service));
        return string.Join('\n', body);
    }

    private static string ExtractNamespaceService(string[] lines, int bodyStart)
    {
        for (var i = bodyStart; i < lines.Length; i++)
        {
            var match = OwnNamespaceRegex().Match(lines[i]);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        throw new InvalidOperationException("No 'namespace OrderToCash.<Service>....;' line found.");
    }

    private static string NormalizeOwnServiceUsingLine(string line, string service)
    {
        var match = UsingDirectiveRegex().Match(line);
        if (!match.Success)
        {
            return line;
        }

        var importedNamespace = match.Groups[1].Value;
        var ownPrefix = $"OrderToCash.{service}";

        if (!importedNamespace.StartsWith(ownPrefix, StringComparison.Ordinal))
        {
            return line;
        }

        var suffix = importedNamespace[ownPrefix.Length..];
        return _serviceScopedUsingSuffixes.Contains(suffix, StringComparer.Ordinal)
            ? $"using <Service>{suffix};"
            : line;
    }

    [GeneratedRegex(@"^\s*namespace\s+OrderToCash\.([A-Za-z0-9_]+)\.")]
    private static partial Regex OwnNamespaceRegex();

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

            var usingMatch = UsingDirectiveRegex().Match(line);
            if (usingMatch.Success)
            {
                var importedNamespace = usingMatch.Groups[1].Value;
                var allowed = _usingWhitelistSuffixes.Any(suffix =>
                    string.Equals(importedNamespace, suffix, StringComparison.Ordinal) ||
                    importedNamespace.EndsWith(suffix, StringComparison.Ordinal));

                Assert.True(allowed, $"{relativePath}:{i + 1} imports '{importedNamespace}', which is outside this file's using whitelist.");
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

    [GeneratedRegex(@"^\s*using\s+([A-Za-z0-9_.]+)\s*;")]
    private static partial Regex UsingDirectiveRegex();
}
