using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// OI12, design.md §6.4's four cases — pure text through <see cref="System.IO"/>,
/// repository root resolved as in <c>RepositoryPaths</c>; no glob package, no
/// container. Lives beside the canonical copy so the developer editing it
/// gets the red test in the project they are editing.
/// </summary>
public sealed partial class IdempotentConsumerParityTests
{
    private const string CanonicalLedgerRelativePath = "src/Orders/Infrastructure/Messaging/ProcessedEventLedger.cs";
    private const string CanonicalConsumerRelativePath = "src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs";

    // The five service-name tokens the canonical/copy files may never carry
    // outside their two normalised regions (design.md §6.4).
    private static readonly string[] _serviceTokens = ["Orders", "Fulfillment", "Billing", "Projector", "Notifications"];

    // "matched by suffix rather than by literal text" (design.md §6.4).
    private static readonly string[] _usingWhitelistSuffixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "OrderToCash.SharedKernel",
        ".Application.Ports",
        ".Infrastructure.Persistence.Entities",
    ];

    [Fact]
    public void HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var canonicalLedger = NormalizeCanonical(ReadFile(root, CanonicalLedgerRelativePath));
        var canonicalConsumer = NormalizeCanonical(ReadFile(root, CanonicalConsumerRelativePath));

        // Every src/<Service> that has BOTH a relational processed_events
        // configuration AND its own Infrastructure/Messaging/IdempotentConsumer.cs
        // is a "copy" this case ranges over. Today only Orders (the
        // canonical itself) qualifies — this member is vacuous until
        // feature 17 adds the second copy, and says so in its own failure
        // message.
        var copies = DiscoverCopyServices(root);
        Assert.NotEmpty(copies); // Orders itself always qualifies.

        foreach (var service in copies)
        {
            var ledgerPath = $"src/{service}/Infrastructure/Messaging/ProcessedEventLedger.cs";
            var consumerPath = $"src/{service}/Infrastructure/Messaging/IdempotentConsumer.cs";

            var ledger = NormalizeCanonical(ReadFile(root, ledgerPath));
            Assert.True(
                canonicalLedger == ledger,
                $"{service}'s ProcessedEventLedger.cs (at {ledgerPath}) diverges from the canonical " +
                $"{CanonicalLedgerRelativePath} outside the banner and the namespace line.");

            var consumer = NormalizeCanonical(ReadFile(root, consumerPath));
            Assert.True(
                canonicalConsumer == consumer,
                $"{service}'s IdempotentConsumer.cs (at {consumerPath}) diverges from the canonical " +
                $"{CanonicalConsumerRelativePath} outside the banner and the namespace line.");
        }
    }

    [Fact]
    public void KeepsTheCanonicalAdoptableVerbatimNamingNoServiceAndReferencingNothingServiceSpecific()
    {
        var root = RepositoryPaths.Find(string.Empty);
        AssertAdoptable(ReadFile(root, CanonicalLedgerRelativePath), CanonicalLedgerRelativePath);
        AssertAdoptable(ReadFile(root, CanonicalConsumerRelativePath), CanonicalConsumerRelativePath);
    }

    [Fact]
    public void RequiresACopyOfThePatternFromEveryWriteModelThatConsumesFacts()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var servicesWithLedger = _serviceTokens.Where(service => HasRelationalProcessedEventsConfiguration(root, service));

        var missing = servicesWithLedger
            .Where(service => HasKafkaConsumerBackgroundService(root, service))
            .Where(service => !File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Messaging/IdempotentConsumer.cs")))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"service(s) with a processed_events table AND a Kafka consumer BackgroundService but no IdempotentConsumer.cs copy: {string.Join(", ", missing)}. " +
            "Today this set is expected to be empty — no service has a Kafka consumer BackgroundService yet (feature 16 is the first).");
    }

    [Fact]
    public void RequiresADocumentedDivergenceBannerFromACopyThatCannotShareTheCanonicalsTransaction()
    {
        var root = RepositoryPaths.Find(string.Empty);

        foreach (var service in _serviceTokens)
        {
            var consumerPath = Path.Combine(root, $"src/{service}/Infrastructure/Messaging/IdempotentConsumer.cs");
            if (!File.Exists(consumerPath) || HasRelationalProcessedEventsConfiguration(root, service))
            {
                // Not a variant: either no copy exists yet, or the service
                // has a relational ledger and is therefore a "copy"
                // candidate (case 1), never a "variant".
                continue;
            }

            var content = File.ReadAllText(consumerPath);
            var banner = ExtractBanner(content);

            Assert.True(
                banner.Contains(CanonicalConsumerRelativePath, StringComparison.Ordinal),
                $"{service}'s IdempotentConsumer.cs is a variant (no relational processed_events table) and its banner must cite {CanonicalConsumerRelativePath}.");
            Assert.True(
                banner.Contains("Divergence:", StringComparison.Ordinal),
                $"{service}'s IdempotentConsumer.cs is a variant and its banner must carry a line beginning 'Divergence:'.");
        }
    }

    /// <summary>Every <c>src/&lt;Service&gt;</c> carrying BOTH a relational <c>processed_events</c> configuration AND its own copy of <c>IdempotentConsumer.cs</c> — the discriminator design.md §6.4 fixes, read from the filesystem, never from a hand-maintained list.</summary>
    private static List<string> DiscoverCopyServices(string root) =>
        _serviceTokens
            .Where(service => HasRelationalProcessedEventsConfiguration(root, service))
            .Where(service => File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Messaging/IdempotentConsumer.cs")))
            .Where(service => File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Messaging/ProcessedEventLedger.cs")))
            .ToList();

    private static bool HasRelationalProcessedEventsConfiguration(string root, string service) =>
        File.Exists(Path.Combine(root, $"src/{service}/Infrastructure/Persistence/Configurations/ProcessedEventConfiguration.cs"));

    /// <summary>
    /// A best-effort filesystem signal for "this service has a Kafka
    /// consumer BackgroundService" — a class deriving from
    /// <c>BackgroundService</c> that also mentions <c>IConsumer&lt;</c> (the
    /// Confluent.Kafka consumer interface). No service has one yet (feature
    /// 16 is the first), so case 3 is exercised as a computed empty set
    /// today, exactly as design.md §6.4 describes.
    /// </summary>
    private static bool HasKafkaConsumerBackgroundService(string root, string service)
    {
        var serviceDirectory = Path.Combine(root, "src", service);
        if (!Directory.Exists(serviceDirectory))
        {
            return false;
        }

        return Directory.EnumerateFiles(serviceDirectory, "*.cs", SearchOption.AllDirectories)
            .Any(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains(": BackgroundService", StringComparison.Ordinal) && text.Contains("IConsumer<", StringComparison.Ordinal);
            });
    }

    private static string ReadFile(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    /// <summary>Strips the banner and the single <c>namespace</c> line — the two regions design.md §6.4 normalises. Everything else is compared verbatim.</summary>
    private static string NormalizeCanonical(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bodyStart = 0;

        while (bodyStart < lines.Length && IsBannerLine(lines[bodyStart]))
        {
            bodyStart++;
        }

        var body = lines.Skip(bodyStart).Where(line => !NamespaceLineRegex().IsMatch(line));
        return string.Join('\n', body);
    }

    private static string ExtractBanner(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var bannerLines = lines.TakeWhile(IsBannerLine);
        return string.Join('\n', bannerLines);
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

            // A `using` line matching the whitelist is exempt from the
            // token scan below — design.md §6.4's own carve-out
            // ("matched by suffix rather than by literal text"), since
            // every service's own .Application.Ports namespace necessarily
            // contains that service's name. Whitelist membership is the
            // check that applies to such a line instead.
            var usingMatch = UsingDirectiveRegex().Match(line);
            if (usingMatch.Success)
            {
                var importedNamespace = usingMatch.Groups[1].Value;
                var allowed = _usingWhitelistSuffixes.Any(suffix =>
                    string.Equals(importedNamespace, suffix, StringComparison.Ordinal) ||
                    importedNamespace.EndsWith(suffix, StringComparison.Ordinal));

                Assert.True(allowed, $"{relativePath}:{i + 1} imports '{importedNamespace}', which is outside design.md §6.4's using whitelist.");
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
