using System.Xml.Linq;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Backlog id 74, bullet 7 — the PER-PROJECT escape from root
/// <c>test.runsettings</c>, named by advisory A1 of id 77's review
/// (<c>progress/review_test_hosts_exhaust_the_per_user_inotify_limit.md</c>).
///
/// <para>
/// The reload-on-change setting
/// (<c>DOTNET_hostBuilder__reloadConfigOnChange=false</c>) reaches a test
/// project only through root <c>Directory.Build.props</c>'
/// <c>RunSettingsFilePath</c>. <c>HostInotifyReloadGuardTests</c>
/// (<c>tests/Orders.UnitTests</c>) proves the setting reaches a REAL host —
/// but it proves it for ONE project, in ONE assembly. A project that sets its
/// own <c>RunSettingsFilePath</c>, sets <c>ImportDirectoryBuildProps=false</c>,
/// drops its own <c>Directory.Build.props</c> between itself and the root, or
/// carries its own <c>*.runsettings</c> file loses the setting SILENTLY,
/// because the one behavioural guard lives in a different assembly and would
/// stay green.
/// </para>
///
/// <para>
/// The population is a LITERAL list (<see cref="_testProjects"/>), never
/// discovered by a predicate a violating project could escape: a project that
/// removed itself from a discovery predicate would remove itself from the
/// sweep instead of failing it. The literal list is reconciled against the
/// tree in both directions, so adding a nineteenth test project fails this
/// guard until the list names it.
/// </para>
/// </summary>
public sealed class TestRunSettingsDeliveryTests
{
    /// <summary>
    /// The EXPECTED set, as a literal. Derived by subtraction from the tree in
    /// <see cref="TheLiteralProjectList_MatchesTheTreeInBothDirections"/> — a
    /// new project is an unclassified line there, never an invisible absence
    /// here.
    /// </summary>
    private static readonly string[] _testProjects =
    [
        "Architecture.Tests",
        "Billing.IntegrationTests",
        "Billing.UnitTests",
        "Contracts.UnitTests",
        "Cqrs.UnitTests",
        "Fulfillment.IntegrationTests",
        "Fulfillment.UnitTests",
        "Gateway.IntegrationTests",
        "Gateway.UnitTests",
        "Notifications.IntegrationTests",
        "Notifications.UnitTests",
        "Orders.IntegrationTests",
        "Orders.UnitTests",
        "Projector.IntegrationTests",
        "Projector.UnitTests",
        "Seed.IntegrationTests",
        "Seed.UnitTests",
        "SharedKernel.UnitTests",
    ];

    /// <summary>
    /// Attack 8 ("compare a literal to a literal") closed: the actual set is
    /// READ OFF THE TREE, with <c>bin/</c> and <c>obj/</c> excluded BY PATH
    /// (attack 10 — a build-output copy of a <c>.csproj</c> must not join the
    /// population), and compared with the literal in both directions.
    /// </summary>
    [Fact]
    public void TheLiteralProjectList_MatchesTheTreeInBothDirections()
    {
        var actual = DiscoverTestProjectDirectories();

        var missingFromTree = _testProjects.Except(actual, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var missingFromList = actual.Except(_testProjects, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.True(
            missingFromTree.Count == 0 && missingFromList.Count == 0,
            $"the literal test-project list in {nameof(TestRunSettingsDeliveryTests)} no longer matches tests/. "
            + $"Named in the list but absent from the tree: [{string.Join(", ", missingFromTree)}]. "
            + $"Present in the tree but absent from the list: [{string.Join(", ", missingFromList)}]. "
            + "Every test project must be named here, or the per-project test.runsettings escape check below silently stops covering it.");
    }

    /// <summary>The delivery mechanism itself — the one line every project below depends on.</summary>
    [Fact]
    public void RootDirectoryBuildProps_PointsRunSettingsFilePathAtRootTestRunsettings()
    {
        var propsPath = RepositoryPaths.Find("Directory.Build.props");
        var document = XDocument.Load(propsPath);

        var values = document.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "RunSettingsFilePath", StringComparison.Ordinal))
            .Select(e => e.Value.Trim())
            .ToList();

        Assert.True(
            values.Count == 1,
            $"root Directory.Build.props must declare exactly ONE RunSettingsFilePath; it declares {values.Count} ([{string.Join(", ", values)}]). "
            + "Without it no test project receives test.runsettings at all.");

        Assert.True(
            values[0].EndsWith("test.runsettings", StringComparison.Ordinal),
            $"root Directory.Build.props' RunSettingsFilePath is \"{values[0]}\", which does not end in \"test.runsettings\" — "
            + "the reload-on-change setting would be read from some other file, or from none.");
    }

    /// <summary>The payload — the setting the whole delivery chain exists to carry.</summary>
    [Fact]
    public void RootTestRunsettings_DisablesHostConfigurationReloadOnChange()
    {
        var runSettingsPath = RepositoryPaths.Find("test.runsettings");
        var document = XDocument.Load(runSettingsPath);

        var entries = document
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "DOTNET_hostBuilder__reloadConfigOnChange", StringComparison.Ordinal))
            .Select(e => e.Value.Trim())
            .ToList();

        Assert.True(
            entries.Count == 1 && string.Equals(entries[0], "false", StringComparison.OrdinalIgnoreCase),
            $"test.runsettings must carry exactly one DOTNET_hostBuilder__reloadConfigOnChange entry set to \"false\"; "
            + $"it carries {entries.Count} ([{string.Join(", ", entries)}]). Every real host a test builds then opens one "
            + "inotify instance per appsettings*.json watcher, and a parallel quality.sh run exhausts fs.inotify.max_user_instances.");
    }

    /// <summary>
    /// Escape shape 1 — a project that sets its own
    /// <c>RunSettingsFilePath</c> overrides the root value and loses the
    /// setting. Read from the PARSED project file, not from its text, so a
    /// commented-out property cannot shadow a real one and a real one inside a
    /// conditioned <c>PropertyGroup</c> cannot hide (attacks 4 and 5).
    /// </summary>
    [Fact]
    public void NoTestProject_OverridesRunSettingsFilePath()
    {
        var offenders = new List<string>();

        foreach (var project in _testProjects)
        {
            var document = XDocument.Load(ProjectFilePath(project));
            var declared = document.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "RunSettingsFilePath", StringComparison.Ordinal))
                .Select(e => e.Value.Trim())
                .ToList();

            if (declared.Count > 0)
            {
                offenders.Add($"{project} (declares RunSettingsFilePath = [{string.Join(", ", declared)}])");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} test project(s) override root Directory.Build.props' RunSettingsFilePath and therefore no longer "
            + $"receive DOTNET_hostBuilder__reloadConfigOnChange=false: {string.Join("; ", offenders)}. "
            + "HostInotifyReloadGuardTests lives in Orders.UnitTests only and would stay green while this project's hosts each opened an inotify instance.");
    }

    /// <summary>
    /// Escape shape 2 — a project that sets
    /// <c>ImportDirectoryBuildProps=false</c> never imports the root file at
    /// all, so <c>RunSettingsFilePath</c> is never set for it.
    /// </summary>
    [Fact]
    public void NoTestProject_OptsOutOfImportingDirectoryBuildProps()
    {
        var offenders = new List<string>();

        foreach (var project in _testProjects)
        {
            var document = XDocument.Load(ProjectFilePath(project));
            var declared = document.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "ImportDirectoryBuildProps", StringComparison.Ordinal))
                .Select(e => e.Value.Trim())
                .Where(v => !string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (declared.Count > 0)
            {
                offenders.Add($"{project} (ImportDirectoryBuildProps = [{string.Join(", ", declared)}])");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} test project(s) opt out of importing root Directory.Build.props and therefore never receive "
            + $"RunSettingsFilePath at all: {string.Join("; ", offenders)}.");
    }

    /// <summary>
    /// Escape shape 3 — a <c>Directory.Build.props</c> (or
    /// <c>.targets</c>) placed under <c>tests/</c> or inside a project
    /// directory. MSBuild stops at the FIRST one it finds walking upward, so
    /// such a file silently replaces the root one unless it imports it.
    /// </summary>
    [Fact]
    public void NoDirectoryBuildPropsShadowsTheRootOne_UnderTests()
    {
        var repositoryRoot = Path.GetDirectoryName(RepositoryPaths.Find("Directory.Build.props"))!;
        var testsRoot = Path.Combine(repositoryRoot, "tests");

        var shadows = Directory
            .EnumerateFiles(testsRoot, "Directory.Build.*", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(p))
            .Select(p => Path.GetRelativePath(repositoryRoot, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            shadows.Count == 0,
            $"{shadows.Count} Directory.Build.* file(s) exist under tests/ and shadow the root one MSBuild would otherwise reach: "
            + $"[{string.Join(", ", shadows)}]. RunSettingsFilePath is set in the ROOT file only, so every project beneath a shadowing "
            + "file loses test.runsettings unless that file imports the root explicitly.");
    }

    /// <summary>
    /// Escape shape 4 — a <c>*.runsettings</c> file inside a test project's
    /// own directory. <c>dotnet test --settings</c> and Visual Studio's own
    /// auto-detection both prefer it, so the project would run under settings
    /// that are not the root file's.
    /// </summary>
    [Fact]
    public void NoTestProjectDirectory_CarriesItsOwnRunsettingsFile()
    {
        var repositoryRoot = Path.GetDirectoryName(RepositoryPaths.Find("Directory.Build.props"))!;
        var offenders = new List<string>();

        foreach (var project in _testProjects)
        {
            var projectDirectory = Path.Combine(repositoryRoot, "tests", project);
            var found = Directory
                .EnumerateFiles(projectDirectory, "*.runsettings", SearchOption.AllDirectories)
                .Where(p => !IsUnderBuildOutput(p))
                .Select(p => Path.GetRelativePath(repositoryRoot, p))
                .ToList();

            if (found.Count > 0)
            {
                offenders.Add($"{project} ([{string.Join(", ", found)}])");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} test project(s) carry their own *.runsettings file, which `dotnet test --settings` or an IDE will "
            + $"prefer over the root one: {string.Join("; ", offenders)}. The reload-on-change setting is declared in the ROOT "
            + "test.runsettings only.");
    }

    private static string ProjectFilePath(string project)
    {
        var path = RepositoryPaths.Find(Path.Combine("tests", project, project + ".csproj"));

        Assert.True(
            File.Exists(path),
            $"test project '{project}' is named in this guard's literal list but its project file does not exist at {path}. "
            + "A renamed or deleted project must be reflected in the list, or the escape checks below silently stop covering it.");

        return path;
    }

    private static List<string> DiscoverTestProjectDirectories()
    {
        var repositoryRoot = Path.GetDirectoryName(RepositoryPaths.Find("Directory.Build.props"))!;
        var testsRoot = Path.Combine(repositoryRoot, "tests");

        return Directory
            .EnumerateFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(p))
            .Select(p => Path.GetFileName(Path.GetDirectoryName(p))!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Attack 10 — excluded BY PATH SEGMENT, never by matching the candidate line's content.</summary>
    private static bool IsUnderBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.Ordinal) ||
                string.Equals(segment, "obj", StringComparison.Ordinal) ||
                string.Equals(segment, "publish", StringComparison.Ordinal));
}
