namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// Locates a path relative to the repository root by walking up from the
/// test assembly's output directory until OrderToCash.sln is found. Same
/// pattern as tests/Architecture.Tests/RepositoryPaths.cs and its siblings —
/// needed here (backlog id 103, fix round 1) so
/// CurrencyExponentWebParityTests can find
/// apps/web/src/lib/currency-exponents.json regardless of the working
/// directory `dotnet test` is invoked from.
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
