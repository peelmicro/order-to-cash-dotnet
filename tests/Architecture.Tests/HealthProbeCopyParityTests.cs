using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// A4e addendum round 2, item 3 — makes the four <c>MsSqlHealthCheck</c>
/// copies and the three <c>KafkaHealthCheck</c> copies (A4e addendum round
/// 1's <c>Pooling=false</c> and <c>TaskCreationOptions.LongRunning</c>
/// production fixes) a PARITY FAMILY, modelled on
/// <c>tests/Orders.UnitTests/FactRetryDispatcherParityTests.cs</c>
/// (design.md §3.4's own shape).
///
/// Discovery is by FILENAME under <c>src/</c>, excluding <c>bin</c>/
/// <c>obj</c> BY PATH — never "has a copy", the self-selecting filter
/// CLAUDE.md's ledger-enumeration rule warns about. The expected set is a
/// LITERAL, and the rest is derived by subtraction. Byte-identity is
/// asserted after normalising ONLY the namespace line and the own-service
/// <c>using</c> line — comments are NOT exempted (CLAUDE.md: "A3 already
/// left two parity families whose banner exemption hides comment drift").
/// Writing this test found REAL, pre-existing comment drift in BOTH
/// families (the Kafka copies' summaries, and — beyond what the round-2
/// brief named — the MS-SQL copies' summary wording/line-wrap, present
/// since A4c), and the round-2 record documents the comment-only src edits
/// this test's own writing required, and why.
/// </summary>
public sealed partial class HealthProbeCopyParityTests
{
    private static readonly string[] _expectedMsSqlHealthCheckPaths =
    [
        "src/Billing/Infrastructure/Health/MsSqlHealthCheck.cs",
        "src/Fulfillment/Infrastructure/Health/MsSqlHealthCheck.cs",
        "src/Notifications/Infrastructure/Health/MsSqlHealthCheck.cs",
        "src/Orders/Infrastructure/Health/MsSqlHealthCheck.cs",
    ];

    private const string MsSqlHealthCheckCanonicalPath = "src/Orders/Infrastructure/Health/MsSqlHealthCheck.cs";

    private static readonly string[] _expectedKafkaHealthCheckPaths =
    [
        "src/Notifications/Infrastructure/Health/KafkaHealthCheck.cs",
        "src/Orders/Infrastructure/Health/KafkaHealthCheck.cs",
        "src/Projector/Infrastructure/Health/KafkaHealthCheck.cs",
    ];

    private const string KafkaHealthCheckCanonicalPath = "src/Orders/Infrastructure/Health/KafkaHealthCheck.cs";

    /// <summary>
    /// D1/health-parity extension (review round 1, advisory A2 — "the five
    /// <c>NatsHealthCheck.cs</c> copies are code-identical ... but their
    /// comments differ"). Comment drift was real (Orders/Gateway carried
    /// the retired "its reviewer found it" wording, R2; the other three
    /// carried a shorter, differently-worded summary) — synced to one
    /// canonical text in this round so byte-identity, comments included,
    /// genuinely holds.
    /// </summary>
    private static readonly string[] _expectedNatsHealthCheckPaths =
    [
        "src/Billing/Infrastructure/Health/NatsHealthCheck.cs",
        "src/Fulfillment/Infrastructure/Health/NatsHealthCheck.cs",
        "src/Gateway/Infrastructure/Health/NatsHealthCheck.cs",
        "src/Orders/Infrastructure/Health/NatsHealthCheck.cs",
        "src/Projector/Infrastructure/Health/NatsHealthCheck.cs",
    ];

    private const string NatsHealthCheckCanonicalPath = "src/Orders/Infrastructure/Health/NatsHealthCheck.cs";

    [Fact]
    public void DiscoversExactlyTheFourMsSqlHealthCheckCopies_ByFilenameUnderSrc_NeverBySelfSelection()
    {
        AssertDiscoveredSetMatches("MsSqlHealthCheck.cs", _expectedMsSqlHealthCheckPaths);
    }

    [Fact]
    public void DiscoversExactlyTheThreeKafkaHealthCheckCopies_ByFilenameUnderSrc_NeverBySelfSelection()
    {
        AssertDiscoveredSetMatches("KafkaHealthCheck.cs", _expectedKafkaHealthCheckPaths);
    }

    [Fact]
    public void DiscoversExactlyTheFiveNatsHealthCheckCopies_ByFilenameUnderSrc_NeverBySelfSelection()
    {
        AssertDiscoveredSetMatches("NatsHealthCheck.cs", _expectedNatsHealthCheckPaths);
    }

    [Fact]
    public void HoldsEveryMsSqlHealthCheckCopyByteIdenticalToTheCanonicalOutsideTheNamespaceAndOwnServiceUsingLine()
    {
        AssertCopiesMatchCanonical(MsSqlHealthCheckCanonicalPath, _expectedMsSqlHealthCheckPaths);
    }

    [Fact]
    public void HoldsEveryKafkaHealthCheckCopyByteIdenticalToTheCanonicalOutsideTheNamespaceAndOwnServiceUsingLine()
    {
        AssertCopiesMatchCanonical(KafkaHealthCheckCanonicalPath, _expectedKafkaHealthCheckPaths);
    }

    /// <summary>
    /// Armed by the review's own suggestion (advisory A2): reverting ONE
    /// copy's timeout (e.g. <c>Billing</c>'s <c>_timeout</c> field, or its
    /// <c>cts.CancelAfter(_timeout)</c> call) makes this test fail, naming
    /// THAT file — matching the arming shape already established for the
    /// MS-SQL and Kafka families above.
    /// </summary>
    [Fact]
    public void HoldsEveryNatsHealthCheckCopyByteIdenticalToTheCanonicalOutsideTheNamespaceAndOwnServiceUsingLine()
    {
        AssertCopiesMatchCanonical(NatsHealthCheckCanonicalPath, _expectedNatsHealthCheckPaths);
    }

    /// <summary>
    /// D1/health-parity extension — the TWO <c>MongoHealthCheck.cs</c>
    /// copies. Discovered and enumerated the SAME way as the three families
    /// above, but NOT asserted byte-identical: unlike NATS/MS-SQL/Kafka,
    /// this pair is NOT a "<c>// COPY OF —</c>" banner family and genuinely
    /// diverges in HOW the Mongo database name reaches the check —
    /// <c>Gateway</c>'s takes <c>GatewayMongoOptions</c> directly (the same
    /// options <c>MongoOrderReadModel</c> already shares), <c>Projector</c>'s
    /// takes <c>IOptions&lt;HealthOptions&gt;</c> (a type <c>Gateway</c> does
    /// not have). Forcing byte-identity would mean inventing a shared
    /// options type for one service purely to satisfy this test — a
    /// production change this round's scope does not license (CLAUDE.md:
    /// "otherwise touch no production code except temporary mutations") and
    /// which is not a proven defect, only a different, independently
    /// correct wiring choice. So this guards what genuinely IS common
    /// between the two — the same check name, the same explicit bounded
    /// timeout, the same real Mongo <c>ping</c> command, the same exception
    /// set — rather than asserting a false byte-parity.
    /// </summary>
    private static readonly string[] _expectedMongoHealthCheckPaths =
    [
        "src/Gateway/Infrastructure/Health/MongoHealthCheck.cs",
        "src/Projector/Infrastructure/Health/MongoHealthCheck.cs",
    ];

    [Fact]
    public void DiscoversExactlyTheTwoMongoHealthCheckCopies_ByFilenameUnderSrc_NeverBySelfSelection()
    {
        AssertDiscoveredSetMatches("MongoHealthCheck.cs", _expectedMongoHealthCheckPaths);
    }

    [Fact]
    public void EveryMongoHealthCheckCopyNamesReadModel_UsesTheSameTwoSecondBoundedTimeout_AndCatchesTheSameExceptionSet()
    {
        var root = RepositoryPaths.Find(string.Empty);

        foreach (var relativePath in _expectedMongoHealthCheckPaths)
        {
            var content = ReadFile(root, relativePath);

            Assert.True(content.Contains("Name => \"readModel\"", StringComparison.Ordinal), $"{relativePath} does not report the 'readModel' check name.");
            Assert.True(content.Contains("TimeSpan.FromSeconds(2)", StringComparison.Ordinal), $"{relativePath} does not bound its probe to the SAME 2-second timeout as its sibling.");
            Assert.True(content.Contains("CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)", StringComparison.Ordinal), $"{relativePath} does not derive its bound from a linked token source.");

            // Review round 2, D7 — the four assertions around this one only
            // proved a TimeSpan named "2 seconds" EXISTS somewhere in the
            // file; none proved it is ever APPLIED to the probe. Deleting
            // `cts.CancelAfter(_timeout);` left the whole suite green.
            // This line is the actual bound, byte-identical in both copies.
            Assert.True(content.Contains("cts.CancelAfter(_timeout);", StringComparison.Ordinal), $"{relativePath} builds a linked CancellationTokenSource but never applies its timeout with CancelAfter — the bound is declared, never enforced.");
            Assert.True(content.Contains("RunCommandAsync<BsonDocument>(new BsonDocument(\"ping\", 1)", StringComparison.Ordinal), $"{relativePath} does not issue a real Mongo 'ping' command.");
            Assert.True(content.Contains("catch (Exception ex) when (ex is OperationCanceledException or MongoException or TimeoutException)", StringComparison.Ordinal), $"{relativePath} does not catch the SAME exception set as its sibling.");
        }
    }

    /// <summary>Enumerates by FILENAME under <c>src/</c>, excluding <c>bin</c>/<c>obj</c> BY PATH, and asserts the discovered set equal to the LITERAL expected set — the rest derived by subtraction, never the other way round.</summary>
    private static void AssertDiscoveredSetMatches(string fileName, string[] expectedRelativePaths)
    {
        var root = RepositoryPaths.Find(string.Empty);
        var srcRoot = Path.Combine(root, "src");

        var discovered = Directory.EnumerateFiles(srcRoot, fileName, SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToHashSet(StringComparer.Ordinal);

        var expected = expectedRelativePaths.ToHashSet(StringComparer.Ordinal);

        var missing = expected.Except(discovered).ToList();
        var unexpected = discovered.Except(expected).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"{fileName}'s copy set drifted from the literal.{Environment.NewLine}" +
            $"Declared in the literal but not found: {string.Join(", ", missing)}{Environment.NewLine}" +
            $"Found but not in the literal: {string.Join(", ", unexpected)}");
    }

    private static void AssertCopiesMatchCanonical(string canonicalRelativePath, string[] copyRelativePaths)
    {
        var root = RepositoryPaths.Find(string.Empty);
        var canonical = Normalize(ReadFile(root, canonicalRelativePath));

        foreach (var copyPath in copyRelativePaths)
        {
            if (copyPath == canonicalRelativePath)
            {
                continue; // the canonical itself.
            }

            var copy = Normalize(ReadFile(root, copyPath));

            Assert.True(
                canonical == copy,
                $"{copyPath} diverges from the canonical {canonicalRelativePath} outside the namespace and own-service using line.");
        }
    }

    private static string ReadFile(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    /// <summary>Removes the namespace line entirely and normalises the own-service <c>using ...Application.Ports;</c> line to a service-neutral placeholder — nothing else, so comments and every other line participate in the byte comparison.</summary>
    private static string Normalize(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var service = ExtractNamespaceService(lines);

        var body = lines
            .Where(line => !NamespaceLineRegex().IsMatch(line))
            .Select(line => NormalizeOwnServiceUsingLine(line, service));
        return string.Join('\n', body);
    }

    private static string ExtractNamespaceService(string[] lines)
    {
        foreach (var line in lines)
        {
            var match = OwnNamespaceRegex().Match(line);
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
        return suffix == ".Application.Ports" ? $"using <Service>{suffix};" : line;
    }

    [GeneratedRegex(@"^\s*namespace\s+OrderToCash\.([A-Za-z0-9_]+)\.")]
    private static partial Regex OwnNamespaceRegex();

    [GeneratedRegex(@"^\s*namespace\s")]
    private static partial Regex NamespaceLineRegex();

    [GeneratedRegex(@"^\s*using\s+([A-Za-z0-9_.]+)\s*;")]
    private static partial Regex UsingDirectiveRegex();
}
