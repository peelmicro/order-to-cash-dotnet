namespace OrderToCash.Seed.Infrastructure.Persistence;

/// <summary>
/// Reads the MS-SQL connection settings from the environment — the same
/// variable names <c>.env</c> already declares
/// (<c>MSSQL_APP_USER</c>/<c>MSSQL_APP_PASSWORD</c>/<c>MSSQL_HOST</c>/
/// <c>MSSQL_HOST_PORT</c>), matching the pattern each service's own
/// <c>*DbContextFactory</c> already uses (feature db_orders et al). This
/// job is run by a developer's shell against the composed stack, never a
/// deployed process, so failing loudly on a missing password is strictly
/// better than silently pointing at one that may no longer match.
/// </summary>
public static class SeedDbConfig
{
    public static string BuildConnectionString(string databaseEnvVar, string defaultDatabase)
    {
        var host = Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MSSQL_HOST_PORT") ?? "1433";
        var database = Environment.GetEnvironmentVariable(databaseEnvVar) ?? defaultDatabase;
        var user = Environment.GetEnvironmentVariable("MSSQL_APP_USER") ?? "otc_app";
        var password = Environment.GetEnvironmentVariable("MSSQL_APP_PASSWORD")
            ?? throw new InvalidOperationException(
                "MSSQL_APP_PASSWORD is not set. Export the value from .env before running the seed, " +
                "e.g.: export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|HOST_PORT)=' .env | xargs)");

        return $"Server={host},{port};Database={database};User Id={user};Password={password};" +
            "TrustServerCertificate=True;";
    }
}
