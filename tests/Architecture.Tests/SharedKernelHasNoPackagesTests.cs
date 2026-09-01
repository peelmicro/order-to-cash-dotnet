using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// CLAUDE.md — "The only shared runtime code is src/SharedKernel (zero
/// PackageReference) and src/Contracts". NetArchTest cannot see project
/// files, so this rule is a plain xUnit test that parses
/// src/SharedKernel/SharedKernel.csproj directly.
/// </summary>
public sealed partial class SharedKernelHasNoPackagesTests
{
    [Fact]
    public void SharedKernelCsprojDeclaresZeroPackageReferences()
    {
        var csprojPath = RepositoryPaths.Find("src/SharedKernel/SharedKernel.csproj");
        var content = File.ReadAllText(csprojPath);

        var matches = PackageReferenceRegex().Matches(content);

        Assert.True(
            matches.Count == 0,
            $"src/SharedKernel/SharedKernel.csproj must declare zero PackageReference entries, found: " +
            string.Join(", ", matches.Select(m => m.Value)));
    }

    /// <summary>
    /// progress/review_monorepo_scaffold.md defect D3: grepping only
    /// SharedKernel.csproj for &lt;PackageReference misses a package that
    /// arrives via a GlobalPackageReference in Directory.Packages.props or
    /// an ItemGroup in Directory.Build.props — both apply to every project
    /// including SharedKernel, and CentralPackageTransitivePinningEnabled is
    /// on. This test closes that gap by inspecting the *compiled assembly's*
    /// metadata directly: <see cref="System.Reflection.Assembly.GetReferencedAssemblies"/>
    /// lists every AssemblyRef the compiler actually emitted, so a package
    /// that reached SharedKernel by any route — a direct PackageReference, a
    /// GlobalPackageReference, or a stray ItemGroup — shows up here even if
    /// no .csproj text search would have found it, <b>provided some type in
    /// the compiled assembly actually calls into it</b>. A package that is
    /// merely declared (by any route) and never used by any SharedKernel
    /// type emits no AssemblyRef and is invisible to this check — proven by
    /// arming an unused GlobalPackageReference during feature 7's
    /// implementation, recorded in progress/impl_shared_kernel.md and
    /// endorsed as an acceptable residual gap in
    /// progress/review_shared_kernel.md §6 (D3 is closed: the two guards on
    /// this class partition the space by route, and the only case neither
    /// covers — declared but never used — adds no runtime dependency, which
    /// is the thing "zero PackageReference" exists to prevent). Every
    /// assembly the .NET 10 shared framework ships starts with "System."
    /// (or is "netstandard"/"mscorlib"); nothing else may appear.
    /// </summary>
    [Fact]
    public void SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework()
    {
        var sharedKernelAssembly = typeof(OrderToCash.SharedKernel.Money).Assembly;

        var referencedAssemblyNames = sharedKernelAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "<unnamed>")
            .ToArray();

        var offenders = referencedAssemblyNames
            .Where(name => name != "netstandard"
                && name != "mscorlib"
                && !name.StartsWith("System.", StringComparison.Ordinal)
                && name != "System")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "OrderToCash.SharedKernel.dll must reference nothing outside the .NET shared framework " +
            $"(a package reached SharedKernel by some route other than a plain <PackageReference> in " +
            $"its own .csproj — check Directory.Packages.props GlobalPackageReference entries and " +
            $"Directory.Build.props ItemGroups). Offending references: {string.Join(", ", offenders)}. " +
            $"All referenced assemblies: {string.Join(", ", referencedAssemblyNames)}");
    }

    [GeneratedRegex("<PackageReference\\b", RegexOptions.IgnoreCase)]
    private static partial Regex PackageReferenceRegex();
}
