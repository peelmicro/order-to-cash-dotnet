using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Feature <c>composition_root_delegation_and_wiring_are_unguarded</c> (id
/// 68, phase 14) — id 56 (<c>composition_root_env_reads_are_unguarded</c>)
/// extracted every service's inline <c>configure</c> lambda into a named,
/// testable <c>*ProgramConfiguration.Configure</c> method, and guarded the
/// METHOD BODY. Its own review (round 2, D2) proved the one line in each
/// <c>Program.cs</c> that HANDS CONTROL to that method was itself guarded
/// by nothing: replacing <c>configure: BillingProgramConfiguration.Configure</c>
/// with a no-op lambda (<c>configure: static _ => { }</c>) left
/// <c>Billing.UnitTests</c> 232/232 green (round-1 probe P17). The same
/// review's round 2 found a second, structurally identical residual one
/// hop further down the Seed composition root: <c>SeedRunner.cs</c> reads
/// each service's connection string under a guarded name (id 56's D1 fix)
/// and then hands it to a DIFFERENT call — <c>OrdersSeedWriter.OpenDb(...)</c>
/// — that nothing checked receives the MATCHING string. Substituting
/// <c>billingConnectionString</c> for <c>ordersConnectionString</c> at
/// <c>SeedRunner.cs:26</c> left <c>Seed.UnitTests</c> 44/44 green (R2-5),
/// reproducing D1's exact consequence — Orders fixtures written into the
/// Billing database — one hop up the call chain from the read id 56
/// already guards.
///
/// <b>The population (re-derived here, not inherited)</b>. The UNIT this
/// file guards is a DELEGATING ARGUMENT — the identifier bound to a
/// <c>configureX:</c> named parameter of a <c>*Host.CreateBuilder</c>/
/// <c>GatewayHost.Build</c> call inside a <c>Program.cs</c> — plus,
/// separately, the two shapes that are not an argument at all: the Seed
/// job's <c>Program.cs → SeedRunner.RunAsync()</c> direct call, and the
/// three connection-string-to-<c>OpenDb</c> pairings inside
/// <c>SeedRunner.cs</c> itself. There are SEVEN <c>Program.cs</c> files;
/// Gateway passes only TWO named <c>configure*:</c> arguments and Orders
/// has no plain <c>configure:</c> at all — it passes five differently-named
/// delegates.
///
/// <b>Fix round 4 — the instrument itself, not another spelling (this
/// file's own re-review, round 3, D12/D13/D14).</b> Rounds 1–3 each
/// hand-rolled a scanner over raw file TEXT
/// (<c>StripCommentsAndLiterals</c>, later guarded by
/// <c>AssertNoUnsupportedConstructs</c>, a denylist of five literal
/// spellings: <c>"#if"</c>, <c>"#elif"</c>, <c>"#else"</c>, <c>"#endif"</c>,
/// <c>"\"\"\""</c>) and was defeated a new way in every one of the three
/// review rounds that followed: round 1 by a wiring-note COMMENT shaped
/// like the live argument, round 2 by an <c>#if false</c> region and by a
/// raw string (<c>"""..."""</c>) whose embedded quote the scanner
/// misparsed as an empty literal followed by a stray quote, round 3 by
/// <c>"# if false"</c> — one space, still a legal C# directive, containing
/// no <c>"#if"</c> substring — and by a quote nested inside an
/// interpolation hole (<c>$"...{"configureHealth: ...".Length}"</c>), a
/// construct the file's own doc comment NAMED as unreadable two paragraphs
/// above a claim that <c>ReadSource</c> "fails ... on any of the constructs
/// above" — which was false, measured, for exactly this one. The reviewer's
/// D14 named the root cause directly: each round armed the two REPORTED
/// exploits rather than their CLASS, because a five-spelling substring
/// denylist is a membership test whose predicate is derived from the thing
/// under test — <c>CLAUDE.md</c>'s own "a sweep must not filter by the
/// property it is testing", now inside a guard.
///
/// This round replaces the hand-rolled scanner and its denylist ENTIRELY
/// with the C# compiler's own lexer/parser (<c>Microsoft.CodeAnalysis.CSharp</c>,
/// the Roslyn package this fix round adds to
/// <c>tests/Architecture.Tests</c> only — see <c>Directory.Packages.props</c>).
/// Every site below now reads a real <see cref="SyntaxTree"/> and asks it
/// for actual <see cref="ArgumentSyntax"/>/<see cref="InvocationExpressionSyntax"/>
/// nodes, never a regex over text. This is not "one more construct taught
/// to the scanner" — it retires the whole class, because the constructs
/// that defeated every prior round are not merely awkward for a hand-rolled
/// scanner to notice, they are STRUCTURALLY INVISIBLE to a real parser as
/// anything other than what they are:
/// <list type="bullet">
/// <item>a raw string (<c>"""..."""</c>), a verbatim string (<c>@"..."</c>)
/// and a quote nested inside an interpolation hole are all LITERAL TOKENS
/// (or, for the interpolation hole, a genuine nested expression tree) —
/// never argument syntax, whatever character sequence they contain;</item>
/// <item>a <c>//</c> or <c>/* */</c> comment is TRIVIA, attached to a
/// token but never itself producing one.</item>
/// </list>
/// A defeat that relies on either of these two shapes cannot pass this
/// guard, because neither is code.
///
/// <b>Fix round 5 (this file's own re-review, round 4, D15/D16/D17/D19) —
/// the original version of this list had a THIRD item here, claiming a
/// disabled <c>#if false</c> region was in the same category. That item is
/// deleted, not corrected in place, because the sentence around it — "a
/// defeat that relies on any of these shapes cannot exist here" — was
/// false, measured, in round 4 (D15): it is true that DISABLED trivia
/// contributes no <see cref="ArgumentSyntax"/>, and irrelevant, because the
/// exposure was never about a disabled region being invisible. It was
/// about WHICH region a parser carrying no preprocessor symbols treats as
/// disabled when the build defines <c>DEBUG</c> — the real call inside
/// <c>#if DEBUG</c>, a decoy in <c>#else</c>, parsed by this test as dead
/// and compiled by <c>dotnet build</c> as the whole program. Passing the
/// build's own symbols would only relocate the disagreement (<c>#if
/// RELEASE</c>, <c>#if !DEBUG</c>, a future <c>DefineConstants</c>), so
/// <see cref="ParseFile"/> now rejects ANY conditional-compilation
/// directive trivia in these files outright, structurally, rather than
/// trying to agree with a symbol set that varies by configuration — a
/// directive-carrying composition root is not a shape this repository has
/// today (confirmed by reading all seven <c>Program.cs</c> files and
/// <c>SeedRunner.cs</c> before relying on this), and this guard now says so
/// by name instead of silently misparsing it (D15, fixed).
///
/// <b>D16 (fixed).</b> <see cref="ExtractNamedArgument"/> collected a
/// <c>configureX:</c> named argument from ANYWHERE in the file — disclosed
/// at the time as an inheritance of the "exactly one match" trade below,
/// but that disclosure was incomplete: satisfying the count from an
/// unrelated local function's argument, rather than from the host call
/// itself, left the real call unwired and this test green. The finder is
/// now anchored to the specific <c>*Host.CreateBuilder</c>/
/// <c>GatewayHost.Build</c> invocation each <c>Program.cs</c> is expected
/// to call (<see cref="IsArgumentOfInvocation"/>); an argument bound
/// anywhere else no longer counts.
///
/// <b>D17 — a stated bound, deliberately NOT closed.</b> A syntax tree
/// carries no symbols. A same-named local type that SHADOWS the real
/// <c>*ProgramConfiguration</c> class makes byte-identical call text mean a
/// no-op, and no syntax-only parse can see that — closing it needs a
/// <c>CSharpCompilation</c> and a <c>SemanticModel</c>, or a host-driving
/// test this feature deliberately declined. What this guard proves is
/// SPELLING — that the right named argument, bound to the right host call,
/// renders as the right dotted identifier path — never MEANING. Both the
/// deleted list item above and this paragraph exist because a claim of
/// "cannot exist" about this mechanism was false, measured, in three
/// consecutive review rounds before this one (D19) — the replacement is
/// written as a bound, not as a strengthened absolute.
///
/// <b>What this buys as a side effect, unasked but free</b>: D3's
/// fully-qualified-target false red (<c>OrderToCash.Billing.BillingProgramConfiguration.Configure</c>
/// failing a regex capture class of <c>[A-Za-z0-9_.]+</c>) no longer
/// applies — a named argument's target is read as a real expression tree
/// and compared by its rendered identifier path, not by what a character
/// class happens to admit.
///
/// <b>What is unchanged, restated for a reader of the diff rather than the
/// history</b>: <see cref="IsUnderBuildOutputDirectory"/> (round 3's D9 fix,
/// excluding by path SEGMENT, not by matching text) is untouched. The
/// "exactly one live call site" trade (round 2's milder, disclosed false
/// red on two legitimate call sites in one file — a shape this repository
/// does not have) is preserved, now expressed as "more than one matching
/// syntax node" rather than "more than one regex match". Within one
/// <c>Program.cs</c>, wiring one service's configuration method into
/// another's slot remains a COMPILE ERROR — every <c>configureX</c>
/// parameter takes a distinct options type, and no service project
/// references another's <c>*ProgramConfiguration</c> type — so the C# type
/// system supplies the "wrong service" substitution family for this
/// population regardless of which instrument reads it; the no-op and
/// dropped-argument families remain the whole of what is compilable and
/// wrong.
/// </summary>
public sealed class CompositionRootDelegationWiringTests
{
    private static readonly CSharpParseOptions _parseOptions = new(LanguageVersion.Latest);

    private static readonly (string ProgramCsRelativePath, (string Argument, string ExpectedTarget)[] Arguments)[] _delegatingArguments =
    [
        ("src/Billing/Program.cs",
        [
            ("configure", "BillingProgramConfiguration.Configure"),
            ("configureTelemetry", "BillingProgramConfiguration.ConfigureTelemetry"),
            ("configureHealth", "BillingProgramConfiguration.ConfigureHealth"),
        ]),
        ("src/Fulfillment/Program.cs",
        [
            ("configure", "FulfillmentProgramConfiguration.Configure"),
            ("configureTelemetry", "FulfillmentProgramConfiguration.ConfigureTelemetry"),
            ("configureHealth", "FulfillmentProgramConfiguration.ConfigureHealth"),
        ]),
        ("src/Gateway/Program.cs",
        [
            ("configure", "GatewayProgramConfiguration.Configure"),
            ("configureTelemetry", "GatewayProgramConfiguration.ConfigureTelemetry"),
        ]),
        ("src/Notifications/Program.cs",
        [
            ("configure", "NotificationsProgramConfiguration.Configure"),
            ("configureTelemetry", "NotificationsProgramConfiguration.ConfigureTelemetry"),
            ("configureHealth", "NotificationsProgramConfiguration.ConfigureHealth"),
        ]),
        ("src/Projector/Program.cs",
        [
            ("configure", "ProjectorProgramConfiguration.Configure"),
            ("configureTelemetry", "ProjectorProgramConfiguration.ConfigureTelemetry"),
            ("configureHealth", "ProjectorProgramConfiguration.ConfigureHealth"),
        ]),
        ("src/Orders/Program.cs",
        [
            ("configureOutbox", "OrdersProgramConfiguration.ConfigureOutbox"),
            ("configureAcceptance", "OrdersProgramConfiguration.ConfigureAcceptance"),
            ("configureSaga", "OrdersProgramConfiguration.ConfigureSaga"),
            ("configureTelemetry", "OrdersProgramConfiguration.ConfigureTelemetry"),
            ("configureHealth", "OrdersProgramConfiguration.ConfigureHealth"),
        ]),
    ];

    /// <summary>
    /// D16 (round 4 re-review, fixed here): which <c>*Host.CreateBuilder</c>/
    /// <c>GatewayHost.Build</c> invocation each <c>Program.cs</c>'s
    /// delegating arguments must be bound TO, so <see cref="ExtractNamedArgument"/>
    /// can reject a same-named argument passed to any OTHER call (a local
    /// function, an unrelated helper) instead of counting it. Read directly
    /// from each host type (<c>grep -n "public static.*Build" src/*/[A-Z]*Host.cs</c>);
    /// Gateway's <c>Program.cs</c> calls <c>GatewayHost.Build</c>, not
    /// <c>GatewayHost.CreateBuilder</c> — both exist on <c>GatewayHost</c>,
    /// so the method name is not inferable from the type alone and is
    /// listed explicitly.
    /// </summary>
    private static readonly Dictionary<string, (string HostType, string HostMethod)> _hostInvocationsByProgramCsPath = new(StringComparer.Ordinal)
    {
        ["src/Billing/Program.cs"] = ("BillingHost", "CreateBuilder"),
        ["src/Fulfillment/Program.cs"] = ("FulfillmentHost", "CreateBuilder"),
        ["src/Gateway/Program.cs"] = ("GatewayHost", "Build"),
        ["src/Notifications/Program.cs"] = ("NotificationsHost", "CreateBuilder"),
        ["src/Projector/Program.cs"] = ("ProjectorHost", "CreateBuilder"),
        ["src/Orders/Program.cs"] = ("OrdersHost", "CreateBuilder"),
    };

    /// <summary>
    /// Population check — re-derived from DISK at test time, not from the
    /// literal table it also checks, via a real <see cref="SyntaxTree"/>
    /// rather than a regex over stripped text. Glob every
    /// <c>src/&lt;Service&gt;/Program.cs</c> on disk, and for each one
    /// collect every <see cref="ArgumentSyntax"/> whose
    /// <see cref="ArgumentSyntax.NameColon"/> names an identifier starting
    /// with <c>configure</c>. Three independent things can fail this test
    /// by name: a service whose <c>Program.cs</c> the literal table does
    /// not list (or vice versa), a service whose discovered argument NAMES
    /// differ from the table's (an addition, a removal, a rename, or a
    /// DUPLICATE — two syntax nodes naming the same argument make the
    /// discovered list longer than the expected one), and the grand total
    /// no longer being 19.
    /// </summary>
    [Fact]
    public void ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk()
    {
        var srcRoot = RepositoryPaths.Find("src");
        var discoveredProgramCsFiles = Directory
            .GetFiles(srcRoot, "Program.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutputDirectory(path))
            .Select(ToRepositoryRelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var expectedProgramCsFiles = _delegatingArguments
            .Select(entry => entry.ProgramCsRelativePath)
            .Append("src/Seed/Program.cs") // zero delegating arguments, but must still exist on disk
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            expectedProgramCsFiles.SequenceEqual(discoveredProgramCsFiles, StringComparer.Ordinal),
            "The set of Program.cs files on disk no longer matches the table's expectation — "
            + $"expected [{string.Join(", ", expectedProgramCsFiles)}], "
            + $"found [{string.Join(", ", discoveredProgramCsFiles)}]. "
            + "A service was added or removed without updating this test's table.");

        var grandTotal = 0;
        foreach (var relativePath in discoveredProgramCsFiles)
        {
            var root = ParseFile(relativePath).GetRoot();
            var discoveredArgumentNames = root
                .DescendantNodes()
                .OfType<ArgumentSyntax>()
                .Where(argument => argument.NameColon is not null
                    && argument.NameColon.Name.Identifier.ValueText.StartsWith("configure", StringComparison.Ordinal))
                .Select(argument => argument.NameColon!.Name.Identifier.ValueText)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var expectedArgumentNames = _delegatingArguments
                .Where(entry => entry.ProgramCsRelativePath == relativePath)
                .SelectMany(entry => entry.Arguments)
                .Select(argument => argument.Argument)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                expectedArgumentNames.SequenceEqual(discoveredArgumentNames, StringComparer.Ordinal),
                $"{relativePath}: the table expects [{string.Join(", ", expectedArgumentNames)}], "
                + $"the source on disk has [{string.Join(", ", discoveredArgumentNames)}] — "
                + "a delegating argument was added, removed, renamed or duplicated without updating this test's table.");

            grandTotal += discoveredArgumentNames.Length;
        }

        Assert.Equal(19, grandTotal);
    }

    [Fact]
    public void BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration()
        => AssertDelegatingArguments("src/Billing/Program.cs");

    [Fact]
    public void FulfillmentProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToFulfillmentProgramConfiguration()
        => AssertDelegatingArguments("src/Fulfillment/Program.cs");

    [Fact]
    public void GatewayProgramCs_DelegatesConfigureAndConfigureTelemetry_ToGatewayProgramConfiguration()
        => AssertDelegatingArguments("src/Gateway/Program.cs");

    [Fact]
    public void NotificationsProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToNotificationsProgramConfiguration()
        => AssertDelegatingArguments("src/Notifications/Program.cs");

    [Fact]
    public void ProjectorProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToProjectorProgramConfiguration()
        => AssertDelegatingArguments("src/Projector/Program.cs");

    /// <summary>
    /// Orders has no plain <c>configure:</c> argument at all — it passes
    /// five distinct delegates. Naming this explicitly is deliberate: the
    /// filed entry's "six Configure calls" implied Orders had one; it has
    /// none, and five differently-named ones instead.
    /// </summary>
    [Fact]
    public void OrdersProgramCs_DelegatesAllFiveConfigureArguments_ToOrdersProgramConfiguration()
        => AssertDelegatingArguments("src/Orders/Program.cs");

    /// <summary>
    /// The one delegating call site that is NOT an argument at all: Seed's
    /// <c>Program.cs</c> hands control to <c>SeedRunner.RunAsync()</c> via
    /// a plain static, zero-argument call, not a named delegate parameter.
    /// Reverting to a no-op (calling nothing, or a different method, or
    /// passing an argument) changes the syntax tree this test reads and
    /// fails it by name.
    /// </summary>
    [Fact]
    public void SeedProgramCs_CallsSeedRunnerRunAsync_TheExtractedOrchestrationMethod()
    {
        var root = ParseFile("src/Seed/Program.cs").GetRoot();

        var found = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == "RunAsync"
                && member.Expression is IdentifierNameSyntax { Identifier.ValueText: "SeedRunner" }
                && invocation.ArgumentList.Arguments.Count == 0);

        Assert.True(found, "Could not find a zero-argument SeedRunner.RunAsync() invocation in src/Seed/Program.cs.");
    }

    /// <summary>
    /// R2-5's fix: <c>SeedRunner.cs</c> reads three GUARDED connection
    /// strings (id 56's D1) and then must hand EACH ONE to its OWN
    /// writer's <c>OpenDb</c> call — a pairing nothing previously checked.
    /// Substituting <c>billingConnectionString</c> for
    /// <c>ordersConnectionString</c> at <c>SeedRunner.cs:26</c> (R2-5's
    /// exact mutation) changes the discovered argument identifier from
    /// <c>ordersConnectionString</c> to <c>billingConnectionString</c> and
    /// fails this test's per-writer assertion by name.
    ///
    /// D18 (round 4 re-review, closed here, in this same test): the pairing
    /// check above (<see cref="AssertOpenDbPairing"/>) reads the
    /// IDENTIFIER handed to <c>OpenDb</c> and never where that identifier's
    /// VALUE came from, so it is silent about the line immediately above
    /// it — <c>var ordersConnectionString = BillingSeedWriter.ConnectionString();</c>
    /// keeps the guarded NAME while the VALUE is a sibling's. Measured:
    /// that mutation left this test and <c>Seed.UnitTests</c> both green,
    /// with Orders fixtures written into the Billing database (id 56's D1
    /// consequence, reached one hop up the call chain).
    /// <see cref="AssertConnectionStringProvenance"/> closes that half: it
    /// requires each <c>&lt;Service&gt;ConnectionString</c> local's
    /// initializer to literally be
    /// <c>&lt;Service&gt;SeedWriter.ConnectionString()</c> — the read and
    /// the write are now both checked, by name and by provenance.
    /// </summary>
    [Fact]
    public void SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings()
    {
        var root = ParseFile("src/Seed/Presentation/SeedRunner.cs").GetRoot();

        AssertConnectionStringProvenance(root, "Orders", "ordersConnectionString");
        AssertConnectionStringProvenance(root, "Fulfillment", "fulfillmentConnectionString");
        AssertConnectionStringProvenance(root, "Billing", "billingConnectionString");

        AssertOpenDbPairing(root, "Orders", "ordersConnectionString");
        AssertOpenDbPairing(root, "Fulfillment", "fulfillmentConnectionString");
        AssertOpenDbPairing(root, "Billing", "billingConnectionString");
    }

    private static void AssertDelegatingArguments(string relativePath)
    {
        var entry = _delegatingArguments.Single(e => e.ProgramCsRelativePath == relativePath);
        var (hostType, hostMethod) = _hostInvocationsByProgramCsPath[relativePath];
        var root = ParseFile(relativePath).GetRoot();

        foreach (var (argument, expectedTarget) in entry.Arguments)
        {
            var actual = ExtractNamedArgument(root, argument, relativePath, hostType, hostMethod);
            Assert.True(
                TargetMatches(actual, expectedTarget),
                $"{relativePath}'s '{argument}:' argument is '{actual}', expected '{expectedTarget}' — the delegating site was repointed or replaced with a no-op.");
        }
    }

    private static void AssertOpenDbPairing(SyntaxNode root, string writerService, string expectedConnectionStringVariable)
    {
        var typeName = writerService + "SeedWriter";
        var matches = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == "OpenDb"
                && member.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == typeName)
            .ToArray();

        Assert.True(matches.Length > 0, $"Could not find '{typeName}.OpenDb(...)' in SeedRunner.cs.");
        Assert.True(
            matches.Length == 1,
            $"Found {matches.Length} occurrences of '{typeName}.OpenDb(...)' in SeedRunner.cs — expected exactly one live call site.");

        var argument = matches[0].ArgumentList.Arguments.Single();
        var actualVariable = argument.Expression.ToString();

        Assert.True(
            expectedConnectionStringVariable == actualVariable,
            $"{typeName}.OpenDb(...) is called with '{actualVariable}', expected its OWN '{expectedConnectionStringVariable}' — a sibling's connection string was substituted.");
    }

    /// <summary>
    /// D18 (round 4 re-review, closed here): <see cref="AssertOpenDbPairing"/>
    /// above checks the IDENTIFIER passed to <c>OpenDb</c>, never where
    /// that identifier's VALUE was assigned from. This closes the missing
    /// half — the local variable named <paramref name="variableName"/>
    /// must be initialized by a zero-argument call to
    /// <c>&lt;writerService&gt;SeedWriter.ConnectionString()</c>, the SAME
    /// service, read as provenance rather than as a name: repointing the
    /// initializer to a sibling's <c>SeedWriter.ConnectionString()</c>
    /// while leaving the variable's own name untouched fails this by name.
    /// </summary>
    private static void AssertConnectionStringProvenance(SyntaxNode root, string writerService, string variableName)
    {
        var expectedTypeName = writerService + "SeedWriter";

        var declarator = root
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .SingleOrDefault(candidate => candidate.Identifier.ValueText == variableName);

        Assert.True(declarator is not null, $"Could not find a '{variableName}' local variable declaration in SeedRunner.cs.");

        var initializer = declarator!.Initializer?.Value;

        var isExpectedProvenance = initializer is InvocationExpressionSyntax invocation
            && invocation.ArgumentList.Arguments.Count == 0
            && invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.ValueText == "ConnectionString"
            && member.Expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == expectedTypeName;

        Assert.True(
            isExpectedProvenance,
            $"'{variableName}' is initialized from '{initializer}', expected '{expectedTypeName}.ConnectionString()' — "
            + "a sibling writer's connection string was assigned under this writer's own variable name.");
    }

    /// <summary>
    /// Reads every <c>configureX:</c>-named <see cref="ArgumentSyntax"/> in
    /// the tree whose enclosing invocation is the EXPECTED host call
    /// (<paramref name="hostType"/>.<paramref name="hostMethod"/> —
    /// <see cref="IsArgumentOfInvocation"/>, D16 fix) and requires EXACTLY
    /// one such match. Zero means the argument is genuinely missing from
    /// the host call — whether because it was dropped, or because it was
    /// passed to a different call entirely; more than one means either a
    /// duplicate or a second legitimate host call in one file (round 2's
    /// disclosed, milder false red, preserved deliberately) — both fail
    /// loudly by name rather than one silently outvoting the other. A
    /// comment, a raw string, or a quote nested inside an interpolation
    /// hole can never produce an <see cref="ArgumentSyntax"/> node here,
    /// because none of them is code.
    /// </summary>
    /// <remarks>
    /// D16 (round 4 re-review, fixed here): before the
    /// <paramref name="hostType"/>/<paramref name="hostMethod"/> anchor
    /// existed, this method counted a <c>configureX:</c> argument found
    /// ANYWHERE in the file — disclosed at the time as an inheritance of
    /// the "exactly one match" trade, but the disclosure never tested
    /// whether that trade could become a false GREEN rather than a false
    /// red. Measured: dropping <c>configureHealth:</c> from the real
    /// <c>BillingHost.CreateBuilder</c> call and passing it to an unrelated
    /// local function instead still satisfied "exactly one match", with
    /// Billing's health probes unwired and every fact green.
    /// </remarks>
    private static string ExtractNamedArgument(SyntaxNode root, string argumentName, string relativePath, string hostType, string hostMethod)
    {
        var matches = root
            .DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Where(argument => argument.NameColon?.Name.Identifier.ValueText == argumentName)
            .Where(argument => IsArgumentOfInvocation(argument, hostType, hostMethod))
            .ToArray();

        Assert.True(
            matches.Length > 0,
            $"Could not find a '{argumentName}:' named argument passed to {hostType}.{hostMethod}(...) in {relativePath}.");
        Assert.True(
            matches.Length == 1,
            $"Found {matches.Length} occurrences of a '{argumentName}:' named argument passed to {hostType}.{hostMethod}(...) in {relativePath} — expected exactly one live call site.");

        return NormalizeExpressionText(matches[0].Expression);
    }

    /// <summary>
    /// True when <paramref name="argument"/> is a direct argument of an
    /// invocation shaped <c>hostType.hostMethod(...)</c> — i.e. its
    /// enclosing <see cref="BaseArgumentListSyntax"/>'s parent IS that
    /// invocation, not some ancestor further out. D16's fix: this is the
    /// anchor <see cref="ExtractNamedArgument"/> was missing.
    /// </summary>
    private static bool IsArgumentOfInvocation(ArgumentSyntax argument, string hostType, string hostMethod)
        => argument.Parent is BaseArgumentListSyntax { Parent: InvocationExpressionSyntax invocation }
            && invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: var typeName },
                Name.Identifier.ValueText: var methodName,
            }
            && typeName == hostType
            && methodName == hostMethod;

    /// <summary>
    /// Renders an argument's expression to a whitespace-collapsed string
    /// for comparison — formatting-insensitive, never text-scanning: the
    /// input is already a real <see cref="ExpressionSyntax"/> the parser
    /// produced, so there is no comment, dead region or literal content
    /// left to misread. A lambda (<c>static _ =&gt; { }</c>) renders as
    /// <c>static_=&gt;{}</c>, visibly distinct from any
    /// <c>Type.Method</c> target this file expects.
    /// </summary>
    private static string NormalizeExpressionText(ExpressionSyntax expression)
        => string.Concat(expression.ToString().Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// A target matches if the rendered expression IS the expected
    /// dotted path, or ENDS WITH it on a member boundary (preceded by a
    /// '.', never a bare substring match) — so a fully-qualified target
    /// (<c>OrderToCash.Billing.BillingProgramConfiguration.Configure</c>)
    /// is accepted as correct wiring rather than rejected by a regex
    /// capture class that happened not to admit it (round 2's disclosed
    /// D3 false red). This is a side effect of reading a real expression
    /// tree, not a change requested by any review round.
    /// </summary>
    private static bool TargetMatches(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return true;
        }

        var suffix = "." + expected;
        return actual.EndsWith(suffix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Single choke point every call site in this class reads a
    /// <c>Program.cs</c>/<c>SeedRunner.cs</c> through: parses it with the
    /// C# compiler's own lexer/parser (<see cref="CSharpSyntaxTree.ParseText"/>,
    /// <see cref="LanguageVersion.Latest"/> — this repository's own
    /// <c>Directory.Build.props</c> pins <c>LangVersion</c> 14.0, and
    /// <c>Latest</c> tracks it forward rather than needing its own update
    /// whenever that pin moves) rather than reading raw text with a
    /// hand-rolled scanner. Fails loudly, by name, if the file does not
    /// parse without error — a defeat attempt that broke compilation would
    /// never reach <c>dotnet build</c> either, but this makes the failure
    /// specific to this test rather than an opaque downstream one.
    /// </summary>
    /// <remarks>
    /// D15 (round 4 re-review, fixed here): <see cref="_parseOptions"/>
    /// defines no preprocessor symbols, while the actual build defines
    /// <c>DEBUG</c> — so a parser reading this source with no symbols and a
    /// compiler building it with <c>DEBUG</c> can disagree about which
    /// <c>#if</c>/<c>#else</c> branch is live. Measured: a real call inside
    /// <c>#if DEBUG</c> and a decoy in <c>#else</c> parsed as dead code by
    /// this test and as the whole program by <c>dotnet build</c>, killing
    /// even a REQUIRED <c>configure:</c> argument with every fact below
    /// still green. Passing the build's own symbols would only relocate
    /// the disagreement (<c>#if RELEASE</c>, <c>#if !DEBUG</c>, a future
    /// <c>DefineConstants</c>), so the fix is structural rather than
    /// symbol-matching: these composition-root files may not carry
    /// conditional-compilation directives at all, checked directly against
    /// the syntax tree's own trivia rather than inferred from build
    /// configuration.
    /// </remarks>
    private static SyntaxTree ParseFile(string relativePath)
    {
        var source = File.ReadAllText(RepositoryPaths.Find(relativePath));
        var tree = CSharpSyntaxTree.ParseText(source, _parseOptions, path: relativePath);

        var errors = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            $"{relativePath} did not parse cleanly: {string.Join("; ", errors.Select(e => e.ToString()))}");

        var directives = tree.GetRoot()
            .DescendantTrivia()
            .Where(trivia => trivia.IsDirective)
            .Select(trivia => trivia.ToString().Trim())
            .ToArray();

        Assert.True(
            directives.Length == 0,
            $"{relativePath} contains conditional-compilation directive(s) [{string.Join(", ", directives)}] — "
            + "this parser is not fed the build's preprocessor symbols (D15: it would only relocate the "
            + "disagreement to a symbol nobody passed), so a composition root under this guard may not carry "
            + "conditional compilation of any kind. Remove the directive(s).");

        return tree;
    }

    /// <summary>
    /// <c>Directory.GetFiles(..., SearchOption.AllDirectories)</c>
    /// discovers a build/publish output copy of <c>Program.cs</c> just as
    /// readily as the real source file — measured at round-2 re-review by
    /// placing one under <c>src/Billing/bin/Debug/net10.0/publish/Program.cs</c>,
    /// which added a phantom entry to the discovered set and failed the
    /// population test with a false red. A build directory can only ADD an
    /// entry — it can never remove a real file or mask a wrong argument in
    /// one — so this is a false-red hazard only, never a false-green one,
    /// but it is excluded BY PATH SEGMENT here rather than left as a
    /// hazard: <c>CLAUDE.md</c> requires exclusion at the source, and a
    /// content-based filter (e.g. dropping any line whose text happens to
    /// contain "/bin/") is the exact mistake this repository has already
    /// paid for elsewhere.
    /// </summary>
    private static bool IsUnderBuildOutputDirectory(string absolutePath)
    {
        var segments = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }

    private static string ToRepositoryRelativePath(string absolutePath)
    {
        var relative = Path.GetRelativePath(FindRepositoryRoot(), absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Duplicates <see cref="RepositoryPaths.Find"/>'s own walk-up rather
    /// than extending that shared helper, because this fix round's bounds
    /// restrict changes to this file alone.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderToCash.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate OrderToCash.sln walking up from {AppContext.BaseDirectory}");
        }

        return dir.FullName;
    }
}
