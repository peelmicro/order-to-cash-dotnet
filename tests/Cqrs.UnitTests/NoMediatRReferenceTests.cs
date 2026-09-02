using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// D4, progress/review_cqrs_dispatcher.md: acceptance item 4 ("no MediatR
/// reference anywhere in the solution") was proven only by a one-off
/// <c>grep</c> at implementation time — true then, but nothing kept it true.
/// This asserts against the COMPILED assembly's actual metadata — the same
/// technique
/// <c>SharedKernelHasNoPackagesTests.SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework</c>
/// uses.
/// </summary>
/// <remarks>
/// <b>What this test actually covers, precisely — corrected after D7
/// (progress/review_cqrs_dispatcher.md).</b> <c>Assembly.GetReferencedAssemblies()</c>
/// reads the assembly-reference TABLE IN THE EMITTED METADATA, and Roslyn
/// only writes an entry for an assembly whose types the code actually
/// calls into. A package that is referenced but never used produces no
/// entry and is therefore INVISIBLE to this test — proven directly: a real
/// <c>PackageVersion Include="MediatR"</c> + <c>PackageReference
/// Include="MediatR"</c> pair, added and left unused, left this test green
/// (progress/impl_cqrs_dispatcher.md, D7's arming table). This test is the
/// TRANSITIVE-OR-ACTUALLY-USED half of the "no MediatR" pair, not the
/// whole of it — <see cref="NoMediatRPackageReferenceTests"/> is the other
/// half, reading the project files and <c>Directory.Packages.props</c>
/// directly, which is what acceptance item 4's actual wording ("no MediatR
/// <i>reference</i>", not "no MediatR usage") needs. The two together are
/// the same pairing <c>SharedKernelHasNoPackagesTests</c> already makes for
/// <c>SharedKernel.csproj</c> — a project-file check and a compiled-
/// metadata check, because either one alone misses a case the other one
/// catches.
/// </remarks>
/// <remarks>
/// Scoped to <c>OrderToCash.Cqrs</c> and <c>OrderToCash.Cqrs.UnitTests</c> —
/// the two assemblies this feature owns and the two touched by this review
/// cycle. A solution-wide equivalent belongs in <c>tests/Architecture.Tests</c>,
/// which is outside this feature's touch list for this review round (the
/// coordinator's scope for this pass is <c>src/Cqrs/**</c> and
/// <c>tests/Cqrs.UnitTests/**</c> only); noted in
/// <c>progress/impl_cqrs_dispatcher.md</c> as a follow-up rather than done
/// here.
/// </remarks>
public sealed class NoMediatRReferenceTests
{
    [Theory]
    [InlineData(typeof(IDispatcher))]
    [InlineData(typeof(NoMediatRReferenceTests))]
    public void CompiledAssemblyReferencesNoMediatRAssembly(Type typeFromAssemblyUnderTest)
    {
        var assembly = typeFromAssemblyUnderTest.Assembly;

        var offenders = assembly
            .GetReferencedAssemblies()
            .Where(referenced => (referenced.Name ?? string.Empty).Contains("mediatr", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"{assembly.GetName().Name} must not reference MediatR (CLAUDE.md: \"no MediatR (v13 is " +
            $"commercially licensed)\"). Offending references: {string.Join(", ", offenders.Select(o => o.FullName))}");
    }
}
