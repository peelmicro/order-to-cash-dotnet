using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Backlog id 76 (<c>application_layer_depends_on_infrastructure_unguarded</c>)
/// — CLAUDE.md's Clean Architecture section: "Dependencies point inwards:
/// presentation → application → domain. Infrastructure implements the ports
/// the application declares." Twenty-seven files across Billing, Fulfillment,
/// Orders and Notifications were found textually referencing an
/// <c>OrderToCash.&lt;Service&gt;.Infrastructure</c> namespace from
/// Application/ (see
/// progress/impl_application_layer_depends_on_infrastructure_unguarded.md
/// for the full enumeration and its classification): most were real IL-level
/// dependencies (a <c>using</c> directive, or a partially-qualified name C#
/// resolves via its enclosing-namespace lookup — two files, both fixed here,
/// that the population's own text-search definition could not even see);
/// three were doc-comment-only text that compiles to nothing NetArchTest can
/// observe. This rule guards the former: no type under an <c>*.Application</c>
/// namespace segment, in any of the six services, may depend on any of the
/// six services' own <c>*.Infrastructure</c> namespace.
///
/// <b>Backlog id 83 (<c>architecture_rule_cannot_see_references_inside_async_lambdas</c>,
/// phase 14) — the NetArchTest/Cecil scan above has a real blind spot, and
/// this file's second half closes it.</b> Id 76's own review (round 1,
/// advisory A2, probes P1–P8) found that a reference confined to an
/// <c>async ct =&gt; { ... }</c> lambda passed to
/// <c>unitOfWork.ExecuteAsync(...)</c> is invisible to the Cecil-based rule,
/// while the same reference in a sync lambda, an async METHOD, an interface
/// member, a method signature or a generic type argument is caught. This
/// file's own re-derivation (see
/// <c>progress/impl_architecture_rule_cannot_see_references_inside_async_lambdas.md</c>
/// §2 for the full probe table) found the review's framing ("async lambda
/// specifically") to be close but not the actual boundary: the real
/// mechanism is <b>nesting depth</b>. Compiling a minimal reproduction and
/// reading the emitted IL with Mono.Cecil directly (the same library
/// NetArchTest uses) shows that <c>Types.InAssemblies(...).That()
/// .ResideInNamespaceMatching(...)</c> only matches types whose OWN
/// <c>Namespace</c> is non-empty — and a compiler-generated nested type
/// (a closure display class, an async state machine, or the <c>&lt;&gt;c</c>
/// static lambda cache) always has <c>Namespace == ""</c>. NetArchTest's
/// dependency walk nonetheless sees ONE level of such nested types (their
/// members are attributed to the declaring type's own dependency surface),
/// but not two. A sync lambda that only touches <c>this</c>/primary-
/// constructor fields, or an async METHOD, needs no intermediate closure
/// class, so its generated type sits exactly one level below the outer
/// type — caught. An async LAMBDA that captures a local variable or a
/// method parameter (the dominant shape in this repository —
/// <c>unitOfWork.ExecuteAsync(async ct => { ... command ... })</c> always
/// captures the command parameter) needs a <c>&lt;&gt;c__DisplayClassN_M</c>
/// at depth one and its own async state machine at depth two — invisible. A
/// NON-capturing async lambda is cached in the compiler's shared <c>&lt;&gt;c</c>
/// type at depth one, with its state machine at depth two — also invisible,
/// for a different reason than the capturing case but the same measured
/// depth. This is closed below with a Roslyn <see cref="SemanticModel"/>
/// scan over the real source text: a lambda body is not a separate type at
/// the SYNTAX level, whatever it becomes after closure conversion, so a
/// symbol reference inside it is exactly as visible to
/// <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
/// as one on the outer type's own body, at any nesting depth.
/// </summary>
public sealed class ApplicationInfrastructureLayeringTests
{
    /// <summary>
    /// The six SERVICE assemblies (CLAUDE.md's Clean Architecture section:
    /// "One .csproj per service ... Presentation/ ... Application/ ...
    /// Domain/ ... Infrastructure/") — a literal list, never discovered by a
    /// predicate a violating assembly could escape (id 76's own acceptance
    /// bullet). Deliberately excludes <see cref="OrderToCash.Seed"/>: it is
    /// a seeding utility, not one of the six services CLAUDE.md enumerates
    /// throughout ("ratified across all six services", "database per
    /// service", etc.) — swept separately below and found already clean,
    /// see progress/impl_application_layer_depends_on_infrastructure_unguarded.md.
    /// </summary>
    private static readonly Assembly[] _serviceAssemblies =
    [
        typeof(OrderToCash.Gateway.Domain.GatewayDomainPlaceholder).Assembly,
        typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder).Assembly,
        typeof(OrderToCash.Fulfillment.Domain.FulfillmentDomainPlaceholder).Assembly,
        typeof(OrderToCash.Billing.Domain.BillingDomainPlaceholder).Assembly,
        typeof(OrderToCash.Notifications.Domain.NotificationsDomainPlaceholder).Assembly,
        typeof(OrderToCash.Projector.Domain.ProjectorDomainPlaceholder).Assembly,
    ];

    /// <summary>
    /// One namespace segment, matching <c>OrderToCash.Orders.Application</c>,
    /// <c>OrderToCash.Orders.Application.Ports</c>,
    /// <c>OrderToCash.Orders.Application.Sagas</c>, etc. — the same
    /// "segment, not substring or suffix" shape
    /// <see cref="DomainAssemblies.DomainNamespacePattern"/> already uses for
    /// <c>.Domain</c>, applied to <c>.Application</c> instead.
    /// </summary>
    private const string ApplicationNamespacePattern = @"(^|\.)Application(\.|$)";

    /// <summary>
    /// The six services' own <c>Infrastructure</c> namespace roots — an
    /// EXPLICIT list rather than a single <c>"Infrastructure"</c> substring,
    /// so this rule can never be satisfied by accident (NetArchTest's
    /// <c>HaveDependencyOnAny</c> is a namespace-PREFIX match; a bare
    /// <c>"Infrastructure"</c> would also match an unrelated
    /// <c>OrderToCash.SomeService.SomeFeature.InfrastructureLike</c>
    /// namespace, which does not exist today but is exactly the kind of
    /// vacuous-by-construction check CLAUDE.md's guard-hardening lessons
    /// warn against).
    /// </summary>
    private static readonly string[] _infrastructureNamespaceRoots =
    [
        "OrderToCash.Gateway.Infrastructure",
        "OrderToCash.Orders.Infrastructure",
        "OrderToCash.Fulfillment.Infrastructure",
        "OrderToCash.Billing.Infrastructure",
        "OrderToCash.Notifications.Infrastructure",
        "OrderToCash.Projector.Infrastructure",
    ];

    /// <summary>
    /// The six services' <c>src/</c> folder names paired with their own
    /// Infrastructure namespace root — id 83's closure-aware scan reads
    /// SOURCE, not a compiled assembly, so it needs a folder to enumerate
    /// <c>.cs</c> files from, one entry per <see cref="_infrastructureNamespaceRoots"/>
    /// entry above (same six, same order, checked 1:1 in
    /// <see cref="ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable"/>).
    /// </summary>
    private static readonly (string ServiceFolder, string InfrastructureRoot)[] _closureProbeServices =
    [
        ("Gateway", "OrderToCash.Gateway.Infrastructure"),
        ("Orders", "OrderToCash.Orders.Infrastructure"),
        ("Fulfillment", "OrderToCash.Fulfillment.Infrastructure"),
        ("Billing", "OrderToCash.Billing.Infrastructure"),
        ("Notifications", "OrderToCash.Notifications.Infrastructure"),
        ("Projector", "OrderToCash.Projector.Infrastructure"),
    ];

    private static readonly CSharpParseOptions _closureProbeParseOptions = new(LanguageVersion.Latest);

    /// <summary>
    /// Computed once per test process, not once per service: gathering and
    /// loading ~260 <see cref="MetadataReference"/>s is the dominant fixed
    /// cost of the closure-aware scan (measured in
    /// progress/impl_architecture_rule_cannot_see_references_inside_async_lambdas.md
    /// §4), and all six services' probe compilations need the identical set
    /// — every NuGet package any of the six services depends on is already
    /// copied into THIS test project's own output directory by MSBuild,
    /// because <c>Architecture.Tests.csproj</c> holds a
    /// <c>ProjectReference</c> to all six (plus Seed, SharedKernel,
    /// Contracts). <see cref="AppContext.GetData"/>'s
    /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c> supplies the shared-framework
    /// assemblies neither MSBuild nor NuGet copies locally (
    /// <c>Microsoft.Extensions.Hosting</c>, <c>Microsoft.AspNetCore.*</c> —
    /// Billing's own <c>FrameworkReference Include="Microsoft.AspNetCore.App"</c>
    /// is exactly this shape) — present here only because this TEST HOST's
    /// own <c>OrderToCash.Architecture.Tests.runtimeconfig.json</c> already
    /// lists <c>Microsoft.AspNetCore.App</c> as a framework, itself only
    /// because a <c>ProjectReference</c> to Gateway (a
    /// <c>Microsoft.NET.Sdk.Web</c> project) pulls it in transitively.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> _metadataReferences =
        new(GatherMetadataReferences);

    /// <summary>
    /// Advisory A1 (id 83 review round 1): the closure-aware scan below
    /// resolves symbols via <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
    /// against a compilation built directly from syntax trees, and that
    /// premise — "the compiler sees what this scan sees" — was previously
    /// observed to hold, never asserted. A silently unresolved symbol is a
    /// false green in a guard whose entire purpose is that a violation
    /// cannot hide, so <see cref="AssertCompilationPremiseHolds"/> makes it
    /// fail loudly instead. This is the only error-diagnostic id allowed
    /// through: CS8795 ("Partial method must have an implementation part")
    /// fires on the <c>[GeneratedRegex]</c> partial methods declared in
    /// <c>Presentation/Rpc/*RequestValidator.cs</c> (measured: 8 in
    /// Billing, 2 in Fulfillment) because constructing a bare
    /// <see cref="CSharpCompilation"/> from syntax trees never runs source
    /// generators — this scan reports only on Application-namespace types,
    /// never Presentation, so the missing generated partial cannot hide an
    /// Application-layer violation. Any OTHER error diagnostic id fails the
    /// test by name and location.
    /// </summary>
    private static readonly string[] _allowedErrorDiagnosticIds = ["CS8795"];

    [Fact]
    public void ApplicationMustNotDependOnInfrastructure()
    {
        var netArchResult = Types.InAssemblies(_serviceAssemblies)
            .That().ResideInNamespaceMatching(ApplicationNamespacePattern)
            .ShouldNot().HaveDependencyOnAny(_infrastructureNamespaceRoots)
            .GetResult();

        var cecilOffenders = netArchResult.FailingTypeNames ?? [];
        var closureOffenders = FindClosureConfinedInfrastructureReferences();

        var offendingTypeNames = cecilOffenders
            .Concat(closureOffenders)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            netArchResult.IsSuccessful && closureOffenders.Count == 0,
            "Application types must not depend on any service's Infrastructure namespace — Infrastructure " +
            "implements the ports Application declares, never the reverse (CLAUDE.md). RPC/Kafka wire payload " +
            "records belong in src/Contracts; anything else crosses through a port. A reference confined to a " +
            "lambda closure or async state machine nested two or more levels below its declaring type is " +
            "invisible to the NetArchTest/Cecil scan above, which checks against ALL SIX services' " +
            "Infrastructure roots (id 83's own finding); it is caught here instead, by a Roslyn semantic model " +
            "reading each service's own real source against its OWN Infrastructure root only (advisory A3, id " +
            "83 review round 1 — unreachable today because no service's .csproj references another service's " +
            "project, so a cross-service Infrastructure reference cannot compile). Offending types: " +
            $"{string.Join(", ", offendingTypeNames)}");
    }

    /// <summary>
    /// A population guard for <see cref="_closureProbeServices"/> itself:
    /// the six folder names must be exactly the six services
    /// <see cref="_infrastructureNamespaceRoots"/> already names, same
    /// order, so a future edit to one table cannot silently drift from the
    /// other and leave a service's closures unscanned while its Cecil
    /// coverage looks unchanged.
    /// </summary>
    [Fact]
    public void ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable()
    {
        var expected = _infrastructureNamespaceRoots;
        var actual = _closureProbeServices.Select(s => s.InfrastructureRoot).ToArray();

        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"_closureProbeServices' infrastructure roots [{string.Join(", ", actual)}] no longer match " +
            $"_infrastructureNamespaceRoots [{string.Join(", ", expected)}] — the two tables drifted apart.");

        foreach (var (serviceFolder, infrastructureRoot) in _closureProbeServices)
        {
            Assert.True(
                infrastructureRoot == $"OrderToCash.{serviceFolder}.Infrastructure",
                $"'{serviceFolder}' is paired with '{infrastructureRoot}', expected 'OrderToCash.{serviceFolder}.Infrastructure'.");
        }
    }

    /// <summary>
    /// The closure-aware half of the guard: for each service, parses every
    /// <c>.cs</c> file under its <c>src/&lt;Service&gt;/</c> folder (all
    /// layers, not just Application — a full compilation needs Domain,
    /// Infrastructure and Presentation source too, for symbol resolution to
    /// succeed; only types DECLARED in an Application-namespace are ever
    /// reported), builds one <see cref="CSharpCompilation"/> per service and
    /// asks its <see cref="SemanticModel"/> whether ANY <see cref="SimpleNameSyntax"/>
    /// found anywhere inside an Application-namespace type declaration
    /// resolves to a symbol declared under that service's own Infrastructure
    /// namespace root. Unlike the Cecil scan above, this walks the SYNTAX
    /// tree — a lambda body's statements are ordinary child nodes of the
    /// lambda expression, at whatever depth, so a reference inside one is
    /// exactly as visible as a reference in the outer method's own body.
    /// </summary>
    private static IReadOnlyList<string> FindClosureConfinedInfrastructureReferences()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (serviceFolder, infrastructureRoot) in _closureProbeServices)
        {
            offenders.UnionWith(FindOffendingApplicationTypes(serviceFolder, infrastructureRoot));
        }

        return offenders.ToArray();
    }

    private static IReadOnlyCollection<string> FindOffendingApplicationTypes(string serviceFolder, string infrastructureRoot)
    {
        var srcRoot = RepositoryPaths.Find(Path.Combine("src", serviceFolder));

        var sourceFilePaths = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutputDirectory(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var trees = sourceFilePaths.Select(ParseSourceFile).ToList();

        // Fresh source parsed here carries no compiler-synthesised implicit
        // usings (Directory.Build.props' <ImplicitUsings>enable</ImplicitUsings>
        // — those are generated by the SDK into an obj/ file at build time,
        // never written by hand in any src/ file). Reading that SAME
        // generated file back is more faithful than hand-duplicating the
        // SDK's implicit-usings list, and — because this test cannot run
        // without dotnet having already built every ProjectReference it
        // holds, including this one — it is guaranteed to exist.
        trees.Add(ParseSourceFile(FindGeneratedGlobalUsingsFile(srcRoot, serviceFolder)));

        // Excludes THIS service's own already-built "OrderToCash.<Service>.dll"
        // from the reference set: it is otherwise present (copied into this
        // test project's own output directory) alongside the fresh source
        // recompiled above, and for an ordinary static extension-method
        // call (e.g. Gateway's `app.MapHealthEndpoints()`) that duplication
        // is a genuine CS0121 ambiguity — two structurally identical
        // extension-method candidates, one from source and one from
        // metadata, with neither taking precedence the way an ordinary
        // member/type lookup would. No service references another
        // service's assembly (CLAUDE.md: "database per service ... never
        // FKs" — the same boundary holds for the compiled assemblies), so
        // nothing else in the reference set needs this exclusion.
        var ownAssemblyFileName = $"OrderToCash.{serviceFolder}.dll";
        var references = _metadataReferences.Value
            .Where(reference => Path.GetFileName(reference.Display) != ownAssemblyFileName)
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: $"{serviceFolder}.ClosureProbe",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        AssertCompilationPremiseHolds(compilation, serviceFolder);

        var offenders = new SortedSet<string>(StringComparer.Ordinal);
        var unresolvedSymbols = new List<string>();

        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);

            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var declaredSymbol = model.GetDeclaredSymbol(typeDeclaration);
                var containingNamespace = declaredSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                if (declaredSymbol is null || !IsApplicationNamespace(containingNamespace))
                {
                    continue;
                }

                var nameNodes = typeDeclaration.DescendantNodes().OfType<SimpleNameSyntax>().ToArray();

                var typeReferencesInfrastructure = nameNodes
                    .Any(nameNode => ReferencesInfrastructureRoot(model, nameNode, infrastructureRoot));

                if (typeReferencesInfrastructure)
                {
                    offenders.Add(declaredSymbol.ToDisplayString());
                }

                // Advisory A1: `nameof` is a contextual keyword whose own
                // identifier token never resolves to a symbol (it names its
                // argument's symbol, not itself) — the one node this scan
                // expects to be unresolved by construction, excluded here
                // rather than counted as a false-green candidate.
                unresolvedSymbols.AddRange(nameNodes
                    .Where(nameNode => nameNode.Identifier.Text != "nameof")
                    .Where(nameNode => IsUnresolvedSymbol(model, nameNode))
                    .Select(nameNode =>
                        $"{declaredSymbol.ToDisplayString()} — '{nameNode}' at {nameNode.GetLocation().GetLineSpan()}"));
            }
        }

        Assert.True(
            unresolvedSymbols.Count == 0,
            $"{serviceFolder}'s closure-probe compilation left {unresolvedSymbols.Count} SimpleNameSyntax " +
            "node(s) inside an Application-namespace type unresolved (GetSymbolInfo returned neither a Symbol " +
            "nor a CandidateSymbols entry, excluding the 'nameof' contextual keyword) — a silently unresolved " +
            "symbol is a false green for the guard above, whose whole purpose is that a violation cannot hide " +
            "(advisory A1, id 83 review round 1). " + string.Join("; ", unresolvedSymbols));

        return offenders;
    }

    /// <summary>
    /// Advisory A1 (id 83 review round 1) — asserts, rather than merely
    /// observes, that this scan's <see cref="CSharpCompilation"/> is
    /// error-free apart from the one diagnostic id known and explained on
    /// <see cref="_allowedErrorDiagnosticIds"/>. An unexpected error
    /// diagnostic can mean the compilation is missing types entirely (a
    /// broken reference, a parse failure elsewhere), which would silently
    /// widen the set of symbols <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
    /// cannot resolve.
    /// </summary>
    private static void AssertCompilationPremiseHolds(CSharpCompilation compilation, string serviceFolder)
    {
        var unexpectedErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Where(diagnostic => !_allowedErrorDiagnosticIds.Contains(diagnostic.Id, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            unexpectedErrors.Length == 0,
            $"{serviceFolder}'s closure-probe compilation carries {unexpectedErrors.Length} error diagnostic(s) " +
            $"not on the allow-list [{string.Join(", ", _allowedErrorDiagnosticIds)}] — an unexpected " +
            "compilation error can hide a symbol from GetSymbolInfo, which is a false green for the guard " +
            "above (advisory A1, id 83 review round 1). " +
            string.Join("; ", unexpectedErrors.Select(d => $"{d.Id} at {d.Location.GetLineSpan()}: {d.GetMessage()}")));
    }

    private static bool IsUnresolvedSymbol(SemanticModel model, SimpleNameSyntax nameNode)
    {
        var symbolInfo = model.GetSymbolInfo(nameNode);
        return symbolInfo.Symbol is null && symbolInfo.CandidateSymbols.IsEmpty;
    }

    /// <summary>
    /// Defeat-list row 7 ("drop an optional element entirely") applied to
    /// this guard's own mechanism: an earlier version took only
    /// <c>symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()</c>
    /// — for a genuinely AMBIGUOUS reference (overload resolution failure,
    /// multiple candidates), that silently checks ONE candidate and ignores
    /// the rest, so a violation could hide behind a candidate list whose
    /// first entry is not the Infrastructure symbol. Every candidate is
    /// checked now, not just the first.
    /// </summary>
    private static bool ReferencesInfrastructureRoot(SemanticModel model, SimpleNameSyntax nameNode, string infrastructureRoot)
    {
        var symbolInfo = model.GetSymbolInfo(nameNode);
        IEnumerable<ISymbol> candidates = symbolInfo.Symbol is { } resolved
            ? [resolved]
            : symbolInfo.CandidateSymbols;

        foreach (var symbol in candidates)
        {
            var relevantNamespace = symbol is INamespaceSymbol namespaceSymbol
                ? namespaceSymbol.ToDisplayString()
                : symbol.ContainingNamespace?.ToDisplayString();

            if (relevantNamespace is not null && MatchesInfrastructureRoot(relevantNamespace, infrastructureRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesInfrastructureRoot(string relevantNamespace, string infrastructureRoot)
    {
        return relevantNamespace == infrastructureRoot
            || relevantNamespace.StartsWith(infrastructureRoot + ".", StringComparison.Ordinal);
    }

    private static bool IsApplicationNamespace(string namespaceDisplayString)
        => System.Text.RegularExpressions.Regex.IsMatch(namespaceDisplayString, ApplicationNamespacePattern);

    /// <summary>
    /// D15's structural rejection (<c>CompositionRootDelegationWiringTests.ParseFile</c>)
    /// applies here too, for the same reason: <see cref="_closureProbeParseOptions"/>
    /// defines no preprocessor symbols, so a real build's <c>DEBUG</c> (or
    /// any other) symbol and this parse could disagree about which branch of
    /// a REGION-LIVENESS directive (<c>#if</c>/<c>#elif</c>/<c>#else</c>/
    /// <c>#endif</c>, and <c>#define</c>/<c>#undef</c> which change symbol
    /// state read by those) is live. Narrower than D15's own version:
    /// scanning the six services' actual source (see
    /// progress/impl_architecture_rule_cannot_see_references_inside_async_lambdas.md
    /// §1) found no <c>#if</c> anywhere, but DOES find <c>#pragma warning
    /// disable/restore</c> in production code (e.g.
    /// <c>Orders/Infrastructure/Outbox/OutboxRelay.cs</c>) — a directive
    /// that changes nothing about which region a parser or the real
    /// compiler considers live, so rejecting it outright was a false red,
    /// not a defence. Only the region-liveness directive KINDS are
    /// rejected; <c>#pragma</c>, <c>#region</c>/<c>#endregion</c>,
    /// <c>#nullable</c> and <c>#line</c> are left alone.
    /// </summary>
    private static SyntaxTree ParseSourceFile(string path)
    {
        var source = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(source, _closureProbeParseOptions, path: path);

        var regionLivenessDirectives = tree.GetRoot()
            .DescendantTrivia()
            .Where(trivia => trivia.Kind() is SyntaxKind.IfDirectiveTrivia
                or SyntaxKind.ElifDirectiveTrivia
                or SyntaxKind.ElseDirectiveTrivia
                or SyntaxKind.EndIfDirectiveTrivia
                or SyntaxKind.DefineDirectiveTrivia
                or SyntaxKind.UndefDirectiveTrivia)
            .Select(trivia => trivia.ToString().Trim())
            .ToArray();

        Assert.True(
            regionLivenessDirectives.Length == 0,
            $"{path} contains region-liveness directive(s) [{string.Join(", ", regionLivenessDirectives)}] — " +
            "this parser is not fed the build's preprocessor symbols, so a service source file under this " +
            "guard may not carry conditional compilation of this kind. Remove the directive(s).");

        return tree;
    }

    /// <summary>
    /// The SDK writes exactly one <c>&lt;AssemblyName&gt;.GlobalUsings.g.cs</c>
    /// per build under <c>obj/&lt;Configuration&gt;/&lt;TFM&gt;/</c> — its
    /// content differs by SDK (Gateway's <c>Microsoft.NET.Sdk.Web</c> adds
    /// seven ASP.NET Core/hosting usings the other five services' plain
    /// <c>Microsoft.NET.Sdk</c> does not carry), so this reads the file
    /// rather than hard-coding either list.
    ///
    /// Advisory A2 (id 83 review round 1): a service built in more than one
    /// configuration (Notifications currently carries both <c>obj/Debug/</c>
    /// and <c>obj/Release/</c> copies) has more than one candidate, and
    /// <see cref="Directory.GetFiles(string, string, SearchOption)"/> makes
    /// no ordering guarantee, so picking <c>candidates[0]</c> unordered was
    /// arbitrary. Ordered deterministically (ordinal path order) AND
    /// asserted to agree in content — if a future configuration-specific
    /// SDK difference ever makes two candidates disagree, picking either one
    /// silently would be wrong in a way this guard's whole reference set
    /// depends on, so that case fails loudly instead.
    /// </summary>
    private static string FindGeneratedGlobalUsingsFile(string srcRoot, string serviceFolder)
    {
        var objRoot = Path.Combine(srcRoot, "obj");
        var candidates = Directory.Exists(objRoot)
            ? Directory.GetFiles(objRoot, "*.GlobalUsings.g.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [];

        Assert.True(
            candidates.Length > 0,
            $"No *.GlobalUsings.g.cs found under {objRoot} — build src/{serviceFolder} (dotnet build, which " +
            "this test's own ProjectReference to it already requires) before running this test.");

        var chosen = candidates[0];
        var chosenContent = File.ReadAllText(chosen);
        var disagreeing = candidates
            .Skip(1)
            .Where(candidate => File.ReadAllText(candidate) != chosenContent)
            .ToArray();

        Assert.True(
            disagreeing.Length == 0,
            $"{serviceFolder} has {candidates.Length} *.GlobalUsings.g.cs candidates under {objRoot} whose " +
            $"content disagrees — chosen '{chosen}' differs from [{string.Join(", ", disagreeing)}], so " +
            "picking one arbitrarily would silently change this guard's own reference set depending on which " +
            "configuration was last built (advisory A2, id 83 review round 1).");

        return chosen;
    }

    private static IReadOnlyList<MetadataReference> GatherMetadataReferences()
    {
        var localDllPaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll", SearchOption.TopDirectoryOnly);

        var trustedPlatformAssemblyPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
            .Split(Path.PathSeparator) ?? [];

        var references = new List<MetadataReference>();

        foreach (var path in localDllPaths.Concat(trustedPlatformAssemblyPaths).Distinct(StringComparer.Ordinal))
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
            {
                // Not every *.dll on the trusted-platform-assembly list or in
                // this test's own output directory is a managed assembly
                // with CLI metadata (satellite/resource DLLs, native
                // interop shims) — skipped rather than failing the whole
                // reference gather over a file this scan was never going to
                // resolve a symbol from.
            }
        }

        return references;
    }

    private static bool IsUnderBuildOutputDirectory(string absolutePath)
    {
        var segments = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }
}
