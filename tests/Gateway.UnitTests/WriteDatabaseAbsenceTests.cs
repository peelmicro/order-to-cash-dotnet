using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Acceptance bullet 3 — "list/detail served from MongoDB, never from a
/// write DB". An ABSENCE claim, so it is proved by enumeration rather than
/// prose (CLAUDE.md: "a claim of absence is reportable only as (a) the
/// exact command that enumerates the candidate set, (b) its complete
/// output, (c) one classification line per hit"). Two independent
/// enumerations: (1) a NetArchTest scan of every type in the Gateway
/// assembly for a dependency on the two write-DB client namespaces, and
/// (2) a text scan of every Gateway source file for a write-DB
/// connection-string environment-variable read. Both are complete-output
/// commands a reviewer can re-run — see progress/impl_gateway_rest_auth.md
/// for the literal command and output this test's own assertions mirror.
/// </summary>
public sealed class WriteDatabaseAbsenceTests
{
    [Fact]
    public void NoTypeInTheGatewayAssembly_DependsOnEntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(GatewayHost).Assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void NoTypeInTheGatewayAssembly_DependsOnMicrosoftDataSqlClient()
    {
        var result = Types.InAssembly(typeof(GatewayHost).Assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.Data.SqlClient")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// The <c>.csproj</c> itself never references either package — a
    /// dependency NetArchTest's assembly scan cannot see if the package
    /// were referenced but genuinely unused (dead but present). Read as
    /// plain text, not compiled, so it also catches a reference that would
    /// only bite a future feature.
    /// </summary>
    [Fact]
    public void GatewayCsproj_NeverReferencesEitherWriteDatabasePackage()
    {
        var csprojPath = FindGatewayCsproj();
        var text = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>No Gateway source file reads an MSSQL_*-style write-database connection-string environment variable — the enumeration command is `grep -rn "MSSQL_" src/Gateway --include=*.cs`, complete output recorded in progress/impl_gateway_rest_auth.md.</summary>
    [Fact]
    public void NoGatewaySourceFile_ReadsAWriteDatabaseConnectionStringEnvironmentVariable()
    {
        var srcGateway = Path.Combine(FindRepositoryRoot(), "src", "Gateway");
        var hits = Directory.EnumerateFiles(srcGateway, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((line, index) => (File: f, Line: index + 1, Text: line)))
            .Where(l => l.Text.Contains("MSSQL_", StringComparison.Ordinal))
            .ToList();

        Assert.True(hits.Count == 0, $"Write-DB connection-string reads found: {string.Join("; ", hits.Select(h => $"{h.File}:{h.Line}"))}");
    }

    private static string FindGatewayCsproj() => Path.Combine(FindRepositoryRoot(), "src", "Gateway", "Gateway.csproj");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderToCash.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate OrderToCash.sln walking up from " + AppContext.BaseDirectory);
    }
}
