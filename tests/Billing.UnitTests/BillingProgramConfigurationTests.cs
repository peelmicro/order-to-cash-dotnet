using OrderToCash.Billing;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="BillingProgramConfiguration.Configure"/>, the EXACT method
/// <c>Program.cs</c> calls (<c>BillingHost.CreateBuilder(args, configure:
/// BillingProgramConfiguration.Configure)</c>), so deleting any of its
/// twelve environment reads — or the <c>BuildMsSqlConnectionString</c> local
/// function it used to be inline with — now fails a named test instead of
/// leaving the suite green (feature 20's review finding, reproduced against
/// <c>src/Billing/Program.cs:23</c>).
/// </summary>
public sealed class BillingProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_DB_BILLING", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD",
        "NATS_URL", "NATS_CLIENT_HOST_PORT",
        "KAFKA_BOOTSTRAP_SERVERS", "KAFKA_HOST_PORT",
        "BILLING_KAFKA_CLIENT_ID", "BILLING_MAX_CONCURRENT_REQUESTS", "CREDIT_FAILURE_RATE",
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
            var options = new BillingOptions();
            BillingProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=localhost,1433;Database=otc_billing;User Id=otc_app;Password=dev-password;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("nats://localhost:4222", options.Nats.Url);
            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
            Assert.Equal("otc-billing", options.Kafka.ClientId);
            Assert.Equal(32, options.Responder.MaxConcurrentRequests);
            Assert.Equal(0, options.CreditFailureRate);
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
        Environment.SetEnvironmentVariable("MSSQL_DB_BILLING", "custom_billing_db");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("NATS_URL", "nats://nats-box:5555");
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        Environment.SetEnvironmentVariable("BILLING_KAFKA_CLIENT_ID", "otc-billing-custom");
        Environment.SetEnvironmentVariable("BILLING_MAX_CONCURRENT_REQUESTS", "7");
        Environment.SetEnvironmentVariable("CREDIT_FAILURE_RATE", "0.25");
        try
        {
            var options = new BillingOptions();
            BillingProgramConfiguration.Configure(options);

            Assert.Equal(
                "Server=sql-box,14330;Database=custom_billing_db;User Id=custom_user;Password=s3cr3t;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("nats://nats-box:5555", options.Nats.Url);
            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
            Assert.Equal("otc-billing-custom", options.Kafka.ClientId);
            Assert.Equal(7, options.Responder.MaxConcurrentRequests);
            Assert.Equal(0.25, options.CreditFailureRate);
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
            var options = new BillingOptions();
            BillingProgramConfiguration.Configure(options);

            Assert.Equal("nats://localhost:4999", options.Nats.Url);
            Assert.Equal("localhost:9099", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToThirtyTwo_WhenBillingMaxConcurrentRequestsIsMalformed()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("BILLING_MAX_CONCURRENT_REQUESTS", "not-a-number");
        try
        {
            var options = new BillingOptions();
            BillingProgramConfiguration.Configure(options);

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
            var options = new BillingOptions();
            var exception = Record.Exception(() => BillingProgramConfiguration.Configure(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_Throws_WhenCreditFailureRateIsOutOfRange()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("CREDIT_FAILURE_RATE", "1.5");
        try
        {
            var options = new BillingOptions();
            var exception = Record.Exception(() => BillingProgramConfiguration.Configure(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("CREDIT_FAILURE_RATE", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>design.md §8.1/§9.2, group A4 — BILLING_HEALTH_PORT defaults to 3004 when unset.</summary>
    [Fact]
    public void ConfigureHealth_DefaultsPortTo3004_WhenNoEnvVarIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new HealthOptions();
            BillingProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(3004, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — substitution (tasks.md A4b, ledger L28): every sibling
    /// <c>*_HEALTH_PORT</c> is set to a distinct, non-default value before
    /// asserting.
    /// </summary>
    [Fact]
    public void ConfigureHealth_ReadsBillingHealthPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("ORDERS_HEALTH_PORT", "23002");
        Environment.SetEnvironmentVariable("FULFILLMENT_HEALTH_PORT", "23003");
        Environment.SetEnvironmentVariable("BILLING_HEALTH_PORT", "13004");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT", "23005");
        Environment.SetEnvironmentVariable("PROJECTOR_HEALTH_PORT", "23006");
        try
        {
            var options = new HealthOptions();
            BillingProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(13004, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }
}
