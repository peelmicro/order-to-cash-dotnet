using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// The missing half of the "no MediatR" pair
/// (progress/review_cqrs_dispatcher.md, D7).
/// <see cref="NoMediatRReferenceTests"/> reads COMPILED metadata via
/// <c>Assembly.GetReferencedAssemblies()</c>, and Roslyn only writes an
/// assembly-reference-table entry for a package whose types the code
/// actually calls into — a package that is referenced but never used
/// produces no entry at all. Proven directly: adding a real
/// <c>PackageVersion Include="MediatR"</c> + <c>PackageReference
/// Include="MediatR"</c> pair, left unused, left that test green (see
/// progress/impl_cqrs_dispatcher.md, D7's arming table). Acceptance item 4
/// is "no MediatR **reference** anywhere in the solution" — a reference,
/// not a usage — so the truth lives in the project files and
/// <c>Directory.Packages.props</c>, not in what the compiler happened to
/// emit.
/// </summary>
/// <remarks>
/// The same split <c>SharedKernelHasNoPackagesTests</c> already makes for
/// <c>SharedKernel.csproj</c>: a project-file check for "declared" (this
/// class) alongside a compiled-metadata check for "actually reached the
/// assembly by any route, including transitively" (<see cref="NoMediatRReferenceTests"/>).
/// <c>SharedKernel</c>'s project-file check can assert "zero
/// <c>PackageReference</c> entries" because it is architecturally barred
/// from having any; <c>Cqrs.csproj</c> legitimately has one
/// (<c>Microsoft.Extensions.DependencyInjection.Abstractions</c>), so this
/// check is narrower — "no package identity matching MediatR" — rather
/// than "zero packages".
/// </remarks>
public sealed partial class NoMediatRPackageReferenceTests
{
    [Theory]
    [InlineData("src/Cqrs/Cqrs.csproj")]
    [InlineData("tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj")]
    [InlineData("Directory.Packages.props")]
    public void ProjectFileDeclaresNoMediatRPackageReferenceOrVersion(string relativePath)
    {
        var path = RepositoryPaths.Find(relativePath);
        var content = File.ReadAllText(path);

        var offendingPackageIds = PackageIdentityRegex()
            .Matches(content)
            .Select(match => match.Groups["id"].Value)
            .Where(id => id.Contains("mediatr", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            offendingPackageIds.Length == 0,
            $"{relativePath} must declare no PackageReference or PackageVersion whose package id " +
            "contains \"MediatR\" (CLAUDE.md: \"no MediatR (v13 is commercially licensed)\"). " +
            $"Offending package ids: {string.Join(", ", offendingPackageIds)}");
    }

    // Matches both <PackageReference Include="..."> and
    // <PackageVersion Include="...">, whichever attribute order — the
    // Include attribute is not always first in this repository's files
    // (see PrivateAssets/IncludeAssets-decorated entries), so the pattern
    // does not assume Include is the first attribute.
    [GeneratedRegex(
        """<Package(?:Reference|Version)\b[^>]*\bInclude\s*=\s*"(?<id>[^"]+)"[^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex PackageIdentityRegex();
}
