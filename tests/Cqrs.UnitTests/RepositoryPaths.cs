namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// Locates a path relative to the repository root by walking up from the
/// test assembly's output directory until <c>OrderToCash.sln</c> is found.
/// Needed only by the plain-xUnit checks that read a project file directly
/// (<c>NoMediatRPackageReferenceTests</c>) rather than the compiled
/// assembly's metadata. A local copy rather than a reference to
/// <c>tests/Architecture.Tests/RepositoryPaths.cs</c> — the two test
/// projects share no code today, and adding a cross-test-project reference
/// for eight lines would be a bigger change than duplicating them.
/// </summary>
internal static class RepositoryPaths
{
    public static string Find(string relativePath)
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

        return Path.Combine(dir.FullName, relativePath);
    }
}
