namespace OrderToCash.Contracts.UnitTests;

/// <summary>
/// Locates a path relative to the repository root by walking up from the
/// test assembly's output directory until <c>OrderToCash.sln</c> is found.
/// Identical in spirit to <c>tests/Architecture.Tests/RepositoryPaths.cs</c>
/// — duplicated rather than shared across test assemblies, since a shared
/// test-only helper project is not part of this feature's scope and this
/// class is a few lines with no logic worth factoring out across a project
/// reference.
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
