using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Backlog id 85 — a test fixture may not choose its own HOST port. The
/// retired shape opened a <see cref="System.Net.Sockets.TcpListener"/> on
/// port 0, read the port the OS had assigned, CLOSED the listener and
/// handed the number to <c>WithPortBinding(hostPort, containerPort)</c>.
/// Nothing held the port between that check and Docker's bind, so anything
/// on the machine could take it, and because the loss happens at container
/// START the whole collection dies at 1 ms — measured twice, once costing
/// 57 tests and once costing 1.
///
/// <para>
/// The adopted idiom is the one the five <c>NatsContainerFixture</c> files
/// already used and never produced the error with:
/// <c>WithPortBinding(containerPort, true)</c>, letting Docker assign and
/// HOLD the host port, then <c>GetMappedPublicPort</c> to read it back.
/// </para>
///
/// <para>
/// <b>Why this reads the syntax tree rather than the text.</b> Six of the
/// eight converted fixtures now describe the retired shape in a doc
/// comment, verbatim — so a text scanner would have to special-case
/// comments (defeat-list attack 4) and would still be beaten by a verbatim
/// string (6) or a <c>#if false</c> region (5). Roslyn sees invocations and
/// object creations, never prose. The one assumption a parser carries that
/// a scanner does not is which preprocessor branch is live, which
/// <see cref="NoConditionalCompilationHidesAPortBindingOrAListener"/>
/// removes structurally rather than by guessing the build's symbols.
/// </para>
/// </summary>
public sealed class ContainerFixtureHostPortAssignmentTests
{
    private static readonly CSharpParseOptions _parseOptions = new(LanguageVersion.Latest);

    /// <summary>
    /// Every file that legitimately binds a container port, as a LITERAL.
    /// A sweep that pointed at the wrong tree, or whose exclusion filter
    /// swallowed the population, would find none of these and fail here
    /// rather than reporting a clean run over nothing. Violations are NOT
    /// detected by this list — they are detected per invocation below, so a
    /// new fixture that self-assigns shows up as a failure rather than
    /// removing itself from the candidate set.
    /// </summary>
    private static readonly string[] _filesExpectedToBindAContainerPort =
    [
        "tests/Billing.IntegrationTests/KafkaContainerFixture.cs",
        "tests/Billing.IntegrationTests/NatsContainerFixture.cs",
        "tests/Fulfillment.IntegrationTests/KafkaContainerFixture.cs",
        "tests/Fulfillment.IntegrationTests/NatsContainerFixture.cs",
        "tests/Gateway.IntegrationTests/KafkaContainerFixture.cs",
        "tests/Gateway.IntegrationTests/NatsContainerFixture.cs",
        "tests/Notifications.IntegrationTests/KafkaContainerFixture.cs",
        "tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs",
        "tests/Notifications.IntegrationTests/MailpitContainerFixture.cs",
        "tests/Orders.IntegrationTests/KafkaContainerFixture.cs",
        "tests/Orders.IntegrationTests/NatsContainerFixture.cs",
        "tests/Projector.IntegrationTests/TestSupport/KafkaContainerFixture.cs",
        "tests/Projector.IntegrationTests/TestSupport/NatsContainerFixture.cs",
    ];

    /// <summary>
    /// Bullet 6 of backlog id 85: <c>GetFreeTcpPort</c>'s eight copies are
    /// deleted rather than left dead, and "if any caller genuinely still
    /// needs one, exactly one copy survives, in one home, with its callers
    /// stating why". This is that one home, and the reason is that its
    /// caller wants the OPPOSITE of a bindable port: a port with nothing
    /// listening on it, so that a real <c>SmtpClient.ConnectAsync</c>
    /// produces a genuine refused connection. It hands its port to no
    /// container, so it has no time-of-check-to-time-of-use window with
    /// Docker at all.
    ///
    /// <para>
    /// Keyed by shape (a method that constructs a listener), not by name —
    /// renaming <c>GetFreeTcpPort</c> to anything else is defeat-list
    /// attack 3 and would escape a name-based ban.
    /// </para>
    /// </summary>
    private static readonly string[] _methodsAllowedToConstructATcpListener =
    [
        "tests/Notifications.IntegrationTests/SendFailureClassifierRealSmtpTests.cs::GetUnboundPort",

        // Backlog id 85's own change-of-kind proof. These two methods
        // REPRODUCE the retired shape in order to demonstrate that it loses
        // the race every time, and hold the port that beats it. They hand
        // nothing to a shared fixture, and the invocation that uses them is
        // the single allowed self-assigned binding below.
        "tests/Projector.IntegrationTests/ContainerHostPortAssignmentRaceTests.cs::ChoosePortTheRetiredWay",
        "tests/Projector.IntegrationTests/ContainerHostPortAssignmentRaceTests.cs::HoldThePortForTheWholeOfTheWindow",
    ];

    /// <summary>
    /// The single self-assigned binding this repository still contains, and
    /// the only one: the RETIRED arm of backlog id 85's change-of-kind
    /// proof, whose entire purpose is to show that the shape fails. It is
    /// keyed by file AND enclosing method, so the exemption cannot be reused
    /// by another call site in the same file, and
    /// <see cref="TheOneAllowedSelfAssignedBindingStillExists"/> asserts it
    /// has not silently disappeared, which would leave the exemption as a
    /// standing hole with nothing behind it.
    /// </summary>
    private const string TheOneSelfAssignedBindingAllowed =
        "tests/Projector.IntegrationTests/ContainerHostPortAssignmentRaceTests.cs::BuildWithASelfAssignedHostPort";

    [Fact]
    public void EveryWithPortBindingLetsDockerAssignTheHostPort()
    {
        var offenders = new List<string>();

        foreach (var (relativePath, tree) in ParseEveryCSharpFile())
        {
            foreach (var invocation in FindInvocations(tree, "WithPortBinding"))
            {
                var arguments = invocation.ArgumentList.Arguments;
                var isDockerAssigned =
                    arguments.Count == 2
                    && arguments[1].Expression.IsKind(SyntaxKind.TrueLiteralExpression);

                if (isDockerAssigned || DescribeSite(relativePath, invocation) == TheOneSelfAssignedBindingAllowed)
                {
                    continue;
                }

                // The span of the METHOD NAME, not of the whole invocation —
                // these are fluent chains, so the invocation node starts at
                // `new ContainerBuilder(...)` several lines above the call
                // actually at fault.
                var nameNode = invocation.Expression is MemberAccessExpressionSyntax member ? member.Name : invocation.Expression;
                var line = tree.GetLineSpan(nameNode.Span).StartLinePosition.Line + 1;
                var hostPortArgument = arguments.Count > 0 ? arguments[0].ToString() : "(no arguments)";
                offenders.Add(
                    $"{relativePath}:{line} — WithPortBinding({invocation.ArgumentList.Arguments}) binds host port '{hostPortArgument}', "
                    + "which this fixture chose for itself");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Backlog id 85 — a container fixture may not choose its own HOST port; Docker chooses it, holds it, and "
            + "GetMappedPublicPort reads it back. The only accepted form is WithPortBinding(containerPort, true) — note "
            + "that the `true` is not optional here even though the C# overload gives it a default, because "
            + "WithPortBinding(9092) binds host port 9092 fixed. Offending call site(s):"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The exemption above is only safe while the thing it exempts is still
    /// the change-of-kind proof. If that call site is deleted or renamed,
    /// the exemption stops naming anything and becomes a standing hole —
    /// so its continued existence is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void TheOneAllowedSelfAssignedBindingStillExists()
    {
        var sites = new List<string>();

        foreach (var (relativePath, tree) in ParseEveryCSharpFile())
        {
            foreach (var invocation in FindInvocations(tree, "WithPortBinding"))
            {
                sites.Add(DescribeSite(relativePath, invocation));
            }
        }

        Assert.True(
            sites.Contains(TheOneSelfAssignedBindingAllowed, StringComparer.Ordinal),
            $"Backlog id 85 — '{TheOneSelfAssignedBindingAllowed}' is exempted from "
            + $"{nameof(EveryWithPortBindingLetsDockerAssignTheHostPort)} because it is the retired arm of the "
            + "change-of-kind proof, and it no longer exists. Either restore it or delete the exemption; leaving the "
            + "exemption in place leaves a named hole with nothing behind it. WithPortBinding call sites found: "
            + string.Join(", ", sites.OrderBy(site => site, StringComparer.Ordinal)));
    }

    [Fact]
    public void ExactlyOneMethodInTheRepositoryStillConstructsATcpListener()
    {
        var found = new List<string>();

        foreach (var (relativePath, tree) in ParseEveryCSharpFile())
        {
            foreach (var creation in tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (!NamesTcpListener(creation.Type))
                {
                    continue;
                }

                found.Add(DescribeSite(relativePath, creation));
            }
        }

        var unexpected = found.Except(_methodsAllowedToConstructATcpListener, StringComparer.Ordinal).ToArray();
        var missing = _methodsAllowedToConstructATcpListener.Except(found, StringComparer.Ordinal).ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Backlog id 85 — GetFreeTcpPort's eight copies were deleted; a method that opens a TcpListener on port 0, "
            + "reads the port and closes the listener may not come back, whatever it is called, because the port it "
            + "returns is held by nobody by the time Docker tries to bind it. Unexpected listener-constructing "
            + $"method(s): {string.Join(", ", unexpected)}. Allowed: {string.Join(", ", _methodsAllowedToConstructATcpListener)}.");

        Assert.True(
            missing.Length == 0,
            "This guard's own population went empty — the allowed listener-constructing method(s) "
            + $"{string.Join(", ", missing)} were not found at all, so the sweep is reading the wrong tree and would "
            + "report a clean run over nothing. Found: "
            + (found.Count == 0 ? "(nothing)" : string.Join(", ", found)));
    }

    [Fact]
    public void EveryFixtureExpectedToBindAContainerPortWasActuallyRead()
    {
        var filesWithABinding = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (relativePath, tree) in ParseEveryCSharpFile())
        {
            if (FindInvocations(tree, "WithPortBinding").Any())
            {
                filesWithABinding.Add(relativePath);
            }
        }

        var missing = _filesExpectedToBindAContainerPort.Except(filesWithABinding, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            "This guard's own population is wrong — file(s) known to bind a container port produced no WithPortBinding "
            + $"invocation: {string.Join(", ", missing)}. Either the sweep is reading the wrong tree (in which case "
            + "EveryWithPortBindingLetsDockerAssignTheHostPort is passing over nothing) or a fixture was removed and this "
            + $"literal needs updating deliberately. Files the sweep DID find a binding in: {string.Join(", ", filesWithABinding.OrderBy(f => f, StringComparer.Ordinal))}.");
    }

    /// <summary>
    /// The one premise this instrument carries that a text scanner does not:
    /// <see cref="_parseOptions"/> defines no preprocessor symbols while the
    /// build defines <c>DEBUG</c>, so a real <c>WithPortBinding</c> inside
    /// <c>#if DEBUG</c> with a compliant decoy in <c>#else</c> would be dead
    /// code to this parser and the whole program to the compiler. Passing
    /// the build's symbols would only move the disagreement to a symbol
    /// nobody passed, so the premise is removed instead: a file that binds a
    /// port or constructs a listener may not carry conditional compilation
    /// at all. The candidate set is the RAW TEXT mention, which is a strict
    /// superset of what the parser sees — a directive cannot hide a file
    /// from this check by hiding its own contents.
    /// </summary>
    [Fact]
    public void NoConditionalCompilationHidesAPortBindingOrAListener()
    {
        var offenders = new List<string>();

        foreach (var absolutePath in EnumerateCSharpFiles())
        {
            var source = File.ReadAllText(absolutePath);
            if (!source.Contains("WithPortBinding", StringComparison.Ordinal)
                && !source.Contains("TcpListener", StringComparison.Ordinal))
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(source, _parseOptions, path: absolutePath);
            var directives = tree.GetRoot()
                .DescendantTrivia()
                .Where(trivia => trivia.IsKind(SyntaxKind.IfDirectiveTrivia)
                    || trivia.IsKind(SyntaxKind.ElifDirectiveTrivia)
                    || trivia.IsKind(SyntaxKind.ElseDirectiveTrivia)
                    || trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia)
                    || trivia.IsKind(SyntaxKind.DefineDirectiveTrivia)
                    || trivia.IsKind(SyntaxKind.UndefDirectiveTrivia))
                .Select(trivia => trivia.ToString().Trim())
                .ToArray();

            if (directives.Length > 0)
            {
                offenders.Add($"{ToRelativePath(absolutePath)} carries [{string.Join(", ", directives)}]");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Backlog id 85 — a file that binds a container port or constructs a TcpListener may not carry conditional "
            + "compilation: this guard's parser is given no preprocessor symbols while the build defines DEBUG, so the "
            + "two would disagree about which branch is live and a real self-assigned binding could sit in a branch the "
            + "guard treats as dead. Offender(s): " + string.Join("; ", offenders));
    }

    /// <summary><c>path::EnclosingMethodName</c> — the key both allow-lists use, so an exemption cannot be reused by a different call site in the same file.</summary>
    private static string DescribeSite(string relativePath, SyntaxNode node)
    {
        var owner = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return owner is null
            ? $"{relativePath}::(not inside a method declaration)"
            : $"{relativePath}::{owner.Identifier.ValueText}";
    }

    private static bool NamesTcpListener(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "TcpListener",
        QualifiedNameSyntax qualified => NamesTcpListener(qualified.Right),
        _ => false,
    };

    private static IEnumerable<InvocationExpressionSyntax> FindInvocations(SyntaxTree tree, string methodName) =>
        tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == methodName,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == methodName,
                _ => false,
            });

    private static IEnumerable<(string RelativePath, SyntaxTree Tree)> ParseEveryCSharpFile()
    {
        foreach (var absolutePath in EnumerateCSharpFiles())
        {
            var relativePath = ToRelativePath(absolutePath);
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(absolutePath), _parseOptions, path: relativePath);

            var errors = tree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(
                errors.Length == 0,
                $"{relativePath} did not parse cleanly, so this guard cannot see what it contains: "
                + string.Join("; ", errors.Select(e => e.ToString())));

            yield return (relativePath, tree);
        }
    }

    /// <summary>
    /// Excluded BY PATH SEGMENT, never by matching the text of a result line
    /// — a build-output copy of a fixture can only ADD a phantom entry, but
    /// a content filter that drops any line mentioning <c>bin</c> silently
    /// drops real hits, which is the mistake this repository has already
    /// paid for.
    /// </summary>
    private static IEnumerable<string> EnumerateCSharpFiles() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutputDirectory(path))
            .OrderBy(path => path, StringComparer.Ordinal);

    private static bool IsUnderBuildOutputDirectory(string absolutePath)
    {
        var segments = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }

    private static string RepositoryRoot { get; } = Path.GetFullPath(RepositoryPaths.Find("."));

    private static string ToRelativePath(string absolutePath) =>
        Path.GetRelativePath(RepositoryRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
}
