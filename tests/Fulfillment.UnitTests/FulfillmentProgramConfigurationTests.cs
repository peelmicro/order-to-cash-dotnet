using OrderToCash.Fulfillment;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="FulfillmentProgramConfiguration.Configure"/>, the EXACT method
/// <c>Program.cs</c> calls (<c>FulfillmentHost.CreateBuilder(args, configure:
/// FulfillmentProgramConfiguration.Configure)</c>), so deleting any of its
/// eleven environment reads fails a named test instead of leaving the suite
/// green.
/// </summary>
[Collection(FulfillmentEnvironmentVariableTestCollection.Name)]
public sealed class FulfillmentProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_DB_FULFILLMENT", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD",
        "NATS_URL", "NATS_CLIENT_HOST_PORT",
        "KAFKA_BOOTSTRAP_SERVERS", "KAFKA_HOST_PORT",
        "FULFILLMENT_KAFKA_CLIENT_ID", "FULFILLMENT_MAX_CONCURRENT_REQUESTS",
        "ORDERS_HEALTH_PORT", "FULFILLMENT_HEALTH_PORT", "BILLING_HEALTH_PORT", "NOTIFICATIONS_HEALTH_PORT", "PROJECTOR_HEALTH_PORT",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Configure_BuildsEveryDocumentedDefault_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new FulfillmentOptions();
            FulfillmentProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=localhost,1433;Database=otc_fulfillment;User Id=otc_app;Password=dev-password;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("nats://localhost:4222", options.Nats.Url);
            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
            Assert.Equal("otc-fulfillment", options.Kafka.ClientId);
            Assert.Equal(32, options.Responder.MaxConcurrentRequests);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_HOST", "sql-box");
        Environment.SetEnvironmentVariable("MSSQL_HOST_PORT", "14330");
        Environment.SetEnvironmentVariable("MSSQL_DB_FULFILLMENT", "custom_fulfillment_db");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("NATS_URL", "nats://nats-box:5555");
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        Environment.SetEnvironmentVariable("FULFILLMENT_KAFKA_CLIENT_ID", "otc-fulfillment-custom");
        Environment.SetEnvironmentVariable("FULFILLMENT_MAX_CONCURRENT_REQUESTS", "9");
        try
        {
            var options = new FulfillmentOptions();
            FulfillmentProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=sql-box,14330;Database=custom_fulfillment_db;User Id=custom_user;Password=s3cr3t;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("nats://nats-box:5555", options.Nats.Url);
            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
            Assert.Equal("otc-fulfillment-custom", options.Kafka.ClientId);
            Assert.Equal(9, options.Responder.MaxConcurrentRequests);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToTheKafkaAndNatsHostPortVariables_WhenTheExplicitUrlIsUnset()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("NATS_CLIENT_HOST_PORT", "4999");
        Environment.SetEnvironmentVariable("KAFKA_HOST_PORT", "9099");
        try
        {
            var options = new FulfillmentOptions();
            FulfillmentProgramConfiguration.Configure(options);

            Assert.Equal("nats://localhost:4999", options.Nats.Url);
            Assert.Equal("localhost:9099", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToThirtyTwo_WhenFulfillmentMaxConcurrentRequestsIsMalformed()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("FULFILLMENT_MAX_CONCURRENT_REQUESTS", "not-a-number");
        try
        {
            var options = new FulfillmentOptions();
            FulfillmentProgramConfiguration.Configure(options);

            Assert.Equal(32, options.Responder.MaxConcurrentRequests);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_Throws_WhenMsSqlAppPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var options = new FulfillmentOptions();
            var exception = Record.Exception(() => FulfillmentProgramConfiguration.Configure(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>design.md §8.1/§9.2, group A4 — FULFILLMENT_HEALTH_PORT defaults to 3003 when unset.</summary>
    [Fact]
    public void ConfigureHealth_DefaultsPortTo3003_WhenNoEnvVarIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new HealthOptions();
            FulfillmentProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(3003, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — substitution (tasks.md A4b, ledger L28): every sibling
    /// <c>*_HEALTH_PORT</c> is set to a distinct, non-default value before
    /// asserting, so a read repointed at a sibling's key fails on the
    /// WRONG VALUE this assertion names, never on the fallback-to-default
    /// reason.
    /// </summary>
    [Fact]
    public void ConfigureHealth_ReadsFulfillmentHealthPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("ORDERS_HEALTH_PORT", "23002");
        Environment.SetEnvironmentVariable("FULFILLMENT_HEALTH_PORT", "13003");
        Environment.SetEnvironmentVariable("BILLING_HEALTH_PORT", "23004");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT", "23005");
        Environment.SetEnvironmentVariable("PROJECTOR_HEALTH_PORT", "23006");
        try
        {
            var options = new HealthOptions();
            FulfillmentProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(13003, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }
}
