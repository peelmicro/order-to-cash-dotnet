using OrderToCash.Seed.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Seed.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> —
/// <see cref="SeedDbConfig.BuildConnectionString"/> is reachable from the
/// Seed composition root (<c>src/Seed/Program.cs</c> → <c>SeedRunner.RunAsync</c>
/// → <c>OrdersSeedWriter.ConnectionString()</c> /
/// <c>FulfillmentSeedWriter.ConnectionString()</c> /
/// <c>BillingSeedWriter.ConnectionString()</c>, each calling
/// <see cref="SeedDbConfig.BuildConnectionString"/> with its own
/// <c>databaseEnvVar</c>/<c>defaultDatabase</c> pair) and had NO test of
/// its own before this file.
/// </summary>
public sealed class SeedDbConfigTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD", "MSSQL_DB_ORDERS",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void BuildConnectionString_BuildsTheDocumentedDefault_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var connectionString = SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders");

            Assert.Equal(
                "Server=localhost,1433;Database=otc_orders;User Id=otc_app;Password=dev-password;TrustServerCertificate=True;",
                connectionString);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void BuildConnectionString_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_HOST", "sql-box");
        Environment.SetEnvironmentVariable("MSSQL_HOST_PORT", "14330");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", "custom_orders_db");
        try
        {
            var connectionString = SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders");

            Assert.Equal(
                "Server=sql-box,14330;Database=custom_orders_db;User Id=custom_user;Password=s3cr3t;TrustServerCertificate=True;",
                connectionString);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void BuildConnectionString_FallsBackToTheCallersDefaultDatabase_WhenItsOwnEnvVarIsUnset()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var connectionString = SeedDbConfig.BuildConnectionString("MSSQL_DB_FULFILLMENT", "otc_fulfillment");

            Assert.Contains("Database=otc_fulfillment;", connectionString);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void BuildConnectionString_Throws_WhenMsSqlAppPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var exception = Record.Exception(() => SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders"));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    // --- review defect D1 --------------------------------------------------
    //
    // The three tests above prove SeedDbConfig.BuildConnectionString reads
    // whatever databaseEnvVar name the TEST hands it — never the name a
    // production caller hands it. Each of OrdersSeedWriter/
    // FulfillmentSeedWriter/BillingSeedWriter binds a distinct MSSQL_DB_*
    // name as a literal at its own call site
    // (OrdersSeedWriter.ConnectionString():23,
    // FulfillmentSeedWriter.ConnectionString():23,
    // BillingSeedWriter.ConnectionString():24), and nothing asserted that
    // binding. Swapping OrdersSeedWriter's literal for MSSQL_DB_BILLING
    // left Seed.UnitTests 41/41 green (review P18) — the seed job would
    // silently write Orders fixtures into the Billing database. These three
    // tests call the writers themselves, not SeedDbConfig directly, so each
    // one pins its writer's own choice of variable name AND its own default
    // database name.

    private static void ClearWriterDbVars()
    {
        Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", null);
        Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", null);
        Environment.SetEnvironmentVariable("MSSQL_DB_BILLING", null);
    }

    [Fact]
    public void OrdersSeedWriter_ConnectionString_ReadsMsSqlDbOrders_AndFallsBackToOtcOrders()
    {
        ClearAll();
        ClearWriterDbVars();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", "custom_orders_db");
            Assert.Contains("Database=custom_orders_db;", OrdersSeedWriter.ConnectionString());

            Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", null);
            Assert.Contains("Database=otc_orders;", OrdersSeedWriter.ConnectionString());
        }
        finally
        {
            ClearAll();
            ClearWriterDbVars();
        }
    }

    [Fact]
    public void FulfillmentSeedWriter_ConnectionString_ReadsMsSqlDbFulfillment_AndFallsBackToOtcFulfillment()
    {
        ClearAll();
        ClearWriterDbVars();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", "custom_fulfillment_db");
            Assert.Contains("Database=custom_fulfillment_db;", FulfillmentSeedWriter.ConnectionString());

            Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", null);
            Assert.Contains("Database=otc_fulfillment;", FulfillmentSeedWriter.ConnectionString());
        }
        finally
        {
            ClearAll();
            ClearWriterDbVars();
        }
    }

    [Fact]
    public void BillingSeedWriter_ConnectionString_ReadsMsSqlDbBilling_AndFallsBackToOtcBilling()
    {
        ClearAll();
        ClearWriterDbVars();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            Environment.SetEnvironmentVariable("MSSQL_DB_BILLING", "custom_billing_db");
            Assert.Contains("Database=custom_billing_db;", BillingSeedWriter.ConnectionString());

            Environment.SetEnvironmentVariable("MSSQL_DB_BILLING", null);
            Assert.Contains("Database=otc_billing;", BillingSeedWriter.ConnectionString());
        }
        finally
        {
            ClearAll();
            ClearWriterDbVars();
        }
    }
}
