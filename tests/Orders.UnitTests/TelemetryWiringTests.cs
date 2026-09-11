using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// A3a/A3b, design.md §5.1, §9.1, ledger L20. Pure text through
/// <see cref="System.IO"/> — no host, no container — the same discipline
/// <c>OutboxRelayParityTests</c>/<c>FactRetryDispatcherParityTests</c>
/// already establish for a repository-wide enumeration.
/// </summary>
public sealed partial class TelemetryWiringTests
{
    /// <summary>
    /// A3a — the four OTel packages (the pre-existing
    /// <c>OpenTelemetry.Extensions.Hosting</c> plus the three this feature
    /// adds) resolve to ONE version. Read from <c>Directory.Packages.props</c>
    /// itself, never re-typed, so a future re-pin cannot silently desync
    /// this test from the file it is about.
    /// </summary>
    [Fact]
    public void A3a_AllFourOpenTelemetryPackagesResolveToTheSameVersion()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var propsPath = Path.Combine(root, "Directory.Packages.props");
        var doc = XDocument.Load(propsPath);

        var otelPackageNames = new[]
        {
            "OpenTelemetry.Extensions.Hosting",
            "OpenTelemetry",
            "OpenTelemetry.Exporter.OpenTelemetryProtocol",
            "OpenTelemetry.Instrumentation.AspNetCore",
        };

        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in doc.Descendants("PackageVersion"))
        {
            var include = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value;
            if (include is not null && version is not null && otelPackageNames.Contains(include, StringComparer.Ordinal))
            {
                versions[include] = version;
            }
        }

        Assert.Equal(4, versions.Count);
        var distinctVersions = versions.Values.Distinct().ToList();
        Assert.True(
            distinctVersions.Count == 1,
            $"Expected all four OTel packages pinned to ONE version; found: {string.Join(", ", versions.Select(kv => $"{kv.Key}={kv.Value}"))}.");

        // The resolved version, recorded here so the implementation record
        // quotes the SAME value a reader can reproduce with this test.
        Assert.Equal("1.18.0", distinctVersions[0]);
    }

    /// <summary>
    /// A3b/OR4, ledger L20 — every <c>new ActivitySource(...)</c>
    /// construction under <c>src/</c> is registered on its OWN service's
    /// <c>AddSource</c> list. <c>AddSource</c> is EXACT-MATCH opt-in: a
    /// constructed-but-unregistered source silently exports nothing.
    /// </summary>
    [Fact]
    public void OR4_EveryActivitySourceNameConstructedUnderSrcIsRegisteredOnItsOwnHostsTracerProvider()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var srcRoot = Path.Combine(root, "src");
        var files = SourceFiles(srcRoot);

        var constructionSites = new List<(string File, string Service, string SourceName)>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            // Matches BOTH `= new ActivitySource(...)` and the target-typed
            // `ActivitySource X = new(...)` this codebase actually uses —
            // a scan that only matched the former would find none of them.
            if (!ActivitySourceConstructionRegex().IsMatch(text))
            {
                continue;
            }

            var nameMatch = SourceNameConstRegex().Match(text);
            Assert.True(nameMatch.Success, $"{file} constructs an ActivitySource but declares no 'public const string SourceName = \"...\";' this test can read.");

            constructionSites.Add((file, ServiceOf(file, srcRoot), nameMatch.Groups["name"].Value));
        }

        // Non-vacuity: exactly one construction site per service that has
        // one today (Gateway, Orders, Fulfillment, Billing, Notifications,
        // Projector) — a literal count, not "at least one".
        Assert.Equal(6, constructionSites.Count);

        foreach (var (file, service, sourceName) in constructionSites)
        {
            var text = File.ReadAllText(file);
            var registered = text.Contains(".AddSource(OtcActivity.SourceName)", StringComparison.Ordinal);

            Assert.True(
                registered,
                $"{service}'s ActivitySource '{sourceName}' (constructed in {file}) is never passed to .AddSource(...) in the same file — " +
                $"AddSource is exact-match opt-in, so spans from this source would never be sampled or exported.");
        }
    }

    /// <summary>OR5 — no service registers a Prometheus-format scrape endpoint or exporter anywhere.</summary>
    [Fact]
    public void OR5_NoServiceRegistersAPrometheusScrapeEndpointOrExporter()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var srcRoot = Path.Combine(root, "src");
        var files = SourceFiles(srcRoot);

        var forbidden = new[] { "AddPrometheusExporter", "MapPrometheusScrapingEndpoint", "OpenTelemetry.Exporter.Prometheus" };
        var hits = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var needle in forbidden)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add($"{file}: contains '{needle}'");
                }
            }
        }

        Assert.Empty(hits);

        // Non-vacuity — the same "prove the sweep can find a hit" discipline
        // OutboxRelayParityTests/BillingConsumesNoFactsTests already use.
        Assert.True(files.Count > 0, "the scan found no .cs files under src/ at all — it cannot be trusted to have looked.");
    }

    /// <summary>Also OR5 — the pinned package set itself never gained the forbidden exporter.</summary>
    [Fact]
    public void OR5_DirectoryPackagesPropsNeverPinsThePrometheusExporterPackage()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var propsText = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));

        Assert.DoesNotContain("OpenTelemetry.Exporter.Prometheus", propsText, StringComparison.Ordinal);
    }

    private static List<string> SourceFiles(string srcRoot) =>
        Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

    private static string ServiceOf(string file, string srcRoot) =>
        Path.GetRelativePath(srcRoot, file).Split(Path.DirectorySeparatorChar)[0];

    [GeneratedRegex("public const string SourceName = \"(?<name>[^\"]+)\";")]
    private static partial Regex SourceNameConstRegex();

    [GeneratedRegex(@"ActivitySource\s+\w+\s*=\s*new(\s+ActivitySource)?\(")]
    private static partial Regex ActivitySourceConstructionRegex();
}
