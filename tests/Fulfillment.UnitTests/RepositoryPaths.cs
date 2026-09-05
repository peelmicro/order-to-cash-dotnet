namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// Locates a path relative to the repository root by walking up from the
/// test assembly's output directory until <c>OrderToCash.sln</c> is found.
/// Duplicated rather than shared across test assemblies — same shape as
/// <c>tests/Orders.UnitTests/RepositoryPaths.cs</c>.
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
