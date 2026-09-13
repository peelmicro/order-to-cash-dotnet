using OrderToCash.Orders;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="OrdersProgramConfiguration"/>'s three methods, the EXACT
/// methods <c>Program.cs</c> calls
/// (<c>OrdersHost.CreateBuilder(args, configureOutbox:
/// OrdersProgramConfiguration.ConfigureOutbox, configureAcceptance:
/// OrdersProgramConfiguration.ConfigureAcceptance, configureSaga:
/// OrdersProgramConfiguration.ConfigureSaga)</c>), so deleting any of the
/// eleven environment reads spread across them fails a named test instead
/// of leaving the suite green.
/// </summary>
[Collection(OrdersEnvironmentVariableTestCollection.Name)]
public sealed class OrdersProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "MSSQL_HOST", "MSSQL_HOST_PORT", "MSSQL_DB_ORDERS", "MSSQL_APP_USER", "MSSQL_APP_PASSWORD",
        "NATS_URL", "NATS_CLIENT_HOST_PORT",
        "KAFKA_BOOTSTRAP_SERVERS", "KAFKA_HOST_PORT",
        "FACT_RETRY_MAX_ATTEMPTS", "FACT_RETRY_BACKOFF_MS",
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
    public void ConfigureOutbox_BuildsEveryDocumentedDefault_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new OrdersOutboxOptions();
            OrdersProgramConfiguration.ConfigureOutbox(options);

            Assert.Equal(
                "Server=localhost,1433;Database=otc_orders;User Id=otc_app;Password=dev-password;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureOutbox_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_HOST", "sql-box");
        Environment.SetEnvironmentVariable("MSSQL_HOST_PORT", "14330");
        Environment.SetEnvironmentVariable("MSSQL_DB_ORDERS", "custom_orders_db");
        Environment.SetEnvironmentVariable("MSSQL_APP_USER", "custom_user");
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        try
        {
            var options = new OrdersOutboxOptions();
            OrdersProgramConfiguration.ConfigureOutbox(options);

            Assert.Equal(
                "Server=sql-box,14330;Database=custom_orders_db;User Id=custom_user;Password=s3cr3t;TrustServerCertificate=True;",
                options.ConnectionString);
            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureOutbox_FallsBackToKafkaHostPortVariable_WhenTheExplicitBootstrapServersIsUnset()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("KAFKA_HOST_PORT", "9099");
        try
        {
            var options = new OrdersOutboxOptions();
            OrdersProgramConfiguration.ConfigureOutbox(options);

            Assert.Equal("localhost:9099", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureOutbox_Throws_WhenMsSqlAppPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var options = new OrdersOutboxOptions();
            var exception = Record.Exception(() => OrdersProgramConfiguration.ConfigureOutbox(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MSSQL_APP_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureAcceptance_DefaultsToLocalhost_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        try
        {
            var options = new OrdersAcceptanceOptions();
            OrdersProgramConfiguration.ConfigureAcceptance(options);

            Assert.Equal("nats://localhost:4222", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureAcceptance_ReadsNatsUrl_WhenSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("NATS_URL", "nats://nats-box:5555");
        try
        {
            var options = new OrdersAcceptanceOptions();
            OrdersProgramConfiguration.ConfigureAcceptance(options);

            Assert.Equal("nats://nats-box:5555", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureAcceptance_FallsBackToNatsClientHostPortVariable_WhenTheExplicitUrlIsUnset()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("NATS_CLIENT_HOST_PORT", "4999");
        try
        {
            var options = new OrdersAcceptanceOptions();
            OrdersProgramConfiguration.ConfigureAcceptance(options);

            Assert.Equal("nats://localhost:4999", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureSaga_DefaultsToLocalhost_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        try
        {
            var options = new OrdersSagaOptions();
            OrdersProgramConfiguration.ConfigureSaga(options);

            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void ConfigureSaga_ReadsKafkaBootstrapServers_WhenSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        try
        {
            var options = new OrdersSagaOptions();
            OrdersProgramConfiguration.ConfigureSaga(options);

            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>OR1 — design.md §3.5's two-variable retry policy family, defaults.</summary>
    [Fact]
    public void ConfigureSaga_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        try
        {
            var options = new OrdersSagaOptions();
            OrdersProgramConfiguration.ConfigureSaga(options);

            Assert.Equal(3, options.FactRetry.MaxAttempts);
            Assert.Equal(500, options.FactRetry.BackoffMs);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// OR1 — tasks.md A1a's substitution arming target: <c>FACT_RETRY_MAX_ATTEMPTS</c>
    /// and <c>FACT_RETRY_BACKOFF_MS</c> are set to distinct, non-default,
    /// mutually non-interchangeable values, so repointing either read at
    /// the other's variable name fails THIS assertion naming the wrong
    /// value — never merely "a value was read".
    /// </summary>
    [Fact]
    public void ConfigureSaga_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS", "7");
        Environment.SetEnvironmentVariable("FACT_RETRY_BACKOFF_MS", "250");
        try
        {
            var options = new OrdersSagaOptions();
            OrdersProgramConfiguration.ConfigureSaga(options);

            Assert.Equal(7, options.FactRetry.MaxAttempts);
            Assert.Equal(250, options.FactRetry.BackoffMs);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>design.md §8.1/§9.2, group A4 — ORDERS_HEALTH_PORT defaults to 3002 when unset.</summary>
    [Fact]
    public void ConfigureHealth_DefaultsPortTo3002_WhenNoEnvVarIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        try
        {
            var options = new HealthOptions();
            OrdersProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(3002, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — substitution (tasks.md A4b, ledger L28): the five
    /// <c>*_HEALTH_PORT</c> variables are a sibling family. This test sets
    /// ORDERS_HEALTH_PORT AND every sibling to distinct, non-default
    /// values BEFORE asserting — so repointing this read at a sibling's key
    /// fails on the WRONG VALUE this assertion names, never on the
    /// fallback-to-default reason (a swap that fails only because the
    /// sibling is unset would prove nothing).
    /// </summary>
    [Fact]
    public void ConfigureHealth_ReadsOrdersHealthPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MSSQL_APP_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("ORDERS_HEALTH_PORT", "13002");
        Environment.SetEnvironmentVariable("FULFILLMENT_HEALTH_PORT", "23003");
        Environment.SetEnvironmentVariable("BILLING_HEALTH_PORT", "23004");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT", "23005");
        Environment.SetEnvironmentVariable("PROJECTOR_HEALTH_PORT", "23006");
        try
        {
            var options = new HealthOptions();
            OrdersProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(13002, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }
}
