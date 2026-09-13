using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// Feature <c>design_time_dbcontext_factory_env_reads_are_unguarded</c> (id
/// 67, phase 14) — <see cref="FulfillmentDbContextFactory"/> is reached only
/// by <c>dotnet ef</c>'s reflection over <see
/// cref="IDesignTimeDbContextFactory{TContext}"/>, never by a composition
/// root, so id 56 correctly excluded it. That made its five environment
/// reads (<c>MSSQL_HOST</c>, <c>MSSQL_HOST_PORT</c>,
/// <c>MSSQL_DB_FULFILLMENT</c>, <c>MSSQL_APP_USER</c>,
/// <c>MSSQL_APP_PASSWORD</c>) live and unguarded — <c>dotnet ef migrations
/// add</c>/<c>database update</c> is tooling this team runs, so a wrong or
/// missing read here points a migration at the wrong database.
///
/// Every test here drives the factory through the SAME interface
/// <c>dotnet ef</c> uses — <see cref="IDesignTimeDbContextFactory{TContext}.CreateDbContext(string[])"/>
/// — rather than a shared helper the factory happens to call, so the cast
/// on <see cref="Factory"/> is deliberate and load-bearing, not incidental.
/// </summary>
[Collection(FulfillmentEnvironmentVariableTestCollection.Name)]
public sealed class FulfillmentDbContextFactoryTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_DB_FULFILLMENT", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD",
        "MSSQL_DB_ORDERS", "MSSQL_DB_BILLING", "MSSQL_DB_NOTIFICATIONS",
    ];

    private static IDesignTimeDbContextFactory<FulfillmentDbContext> Factory => new FulfillmentDbContextFactory();

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static string BuildConnectionString(string[] args)
    {
        using var db = Factory.CreateDbContext(args);
        return db.Database.GetConnectionString()!;
    }

    [Fact]
    public void CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_HOST", "sql-box");
        Environment.SetEnvironmentVariable("MSSQL_HOST_PORT", "14330");
        Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", "custom_fulfillment_db");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        try
        {
            var connectionString = BuildConnectionString([]);

            Assert.Contains("Data Source=sql-box,14330", connectionString, StringComparison.Ordinal);
            Assert.Contains("Initial Catalog=custom_fulfillment_db", connectionString, StringComparison.Ordinal);
            Assert.Contains("User ID=custom_user", connectionString, StringComparison.Ordinal);
            Assert.Contains("Password=s3cr3t", connectionString, StringComparison.Ordinal);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void CreateDbContext_BuildsEveryDocumentedDefault_WhenOnlyTheRequiredPasswordIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var connectionString = BuildConnectionString([]);

            Assert.Contains("Data Source=localhost,1433", connectionString, StringComparison.Ordinal);
            Assert.Contains("Initial Catalog=otc_fulfillment", connectionString, StringComparison.Ordinal);
            Assert.Contains("User ID=otc_app", connectionString, StringComparison.Ordinal);
            Assert.Contains("Password=dev-password", connectionString, StringComparison.Ordinal);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void CreateDbContext_Throws_WhenMsSqlAppPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var exception = Record.Exception(() => BuildConnectionString([]));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message, StringComparison.Ordinal);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — substitution (CLAUDE.md's sibling-family discriminator):
    /// <c>MSSQL_DB_FULFILLMENT</c> has three siblings (<c>MSSQL_DB_ORDERS</c>,
    /// <c>MSSQL_DB_BILLING</c>, <c>MSSQL_DB_NOTIFICATIONS</c>) read by the
    /// three sibling <c>*DbContextFactory</c> classes. This test sets
    /// <c>MSSQL_DB_FULFILLMENT</c> AND every sibling to distinct, non-default
    /// values before asserting, so repointing this factory's literal at a
    /// sibling's key fails on the WRONG DATABASE NAME this assertion names,
    /// never on a fallback-to-default reason (a swap that fails only
    /// because the sibling is unset would prove nothing — CLAUDE.md's named
    /// false-negative mode).
    /// </summary>
    [Fact]
    public void CreateDbContext_ReadsMsSqlDbFulfillment_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", "custom_fulfillment_db");
        Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", "custom_orders_db");
        Environment.SetEnvironmentVariable("MSSQL_DB_BILLING", "custom_billing_db");
        Environment.SetEnvironmentVariable("MSSQL_DB_NOTIFICATIONS", "custom_notifications_db");
        try
        {
            var connectionString = BuildConnectionString([]);

            Assert.Contains("Initial Catalog=custom_fulfillment_db", connectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("custom_orders_db", connectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("custom_billing_db", connectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("custom_notifications_db", connectionString, StringComparison.Ordinal);
        }
        finally
        {
            ClearAll();
        }
    }
}
