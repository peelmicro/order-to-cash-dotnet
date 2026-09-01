namespace OrderToCash.Architecture.Tests;

/// <summary>
/// Locates a path relative to the repository root by walking up from the
/// test assembly's output directory until OrderToCash.sln is found. Needed
/// only by the plain-xUnit checks that read a file NetArchTest cannot see
/// (project files, not compiled types).
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
