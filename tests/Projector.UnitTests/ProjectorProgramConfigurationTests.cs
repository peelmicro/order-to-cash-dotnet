using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure;
using OrderToCash.Projector.Infrastructure.Health;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="ProjectorProgramConfiguration.Configure"/>, the EXACT method
/// <c>Program.cs</c> calls, so deleting any of its four direct environment
/// reads — or the <c>options.Mongo = ProjectorMongoOptions.FromEnvironment()</c>
/// call site itself — fails a named test instead of leaving the suite
/// green. <c>ProjectorMongoOptions.FromEnvironment</c>'s own five reads had
/// no test at all before this file (unlike Gateway's <c>*Options</c>
/// classes) — <see cref="Configure_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues"/>
/// and <see cref="Configure_Throws_WhenMongoRootPasswordIsNotSet"/> close
/// that gap too.
/// </summary>
public sealed class ProjectorProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "KAFKA_BOOTSTRAP_SERVERS", "KAFKA_HOST_PORT",
        "NATS_URL", "NATS_CLIENT_HOST_PORT",
        "MONGO_HOST", "MONGO_HOST_PORT", "MONGO_INITDB_ROOT_USERNAME", "MONGO_INITDB_ROOT_PASSWORD", "MONGO_DB_READMODEL",
        "ORDERS_HEALTH_PORT", "FULFILLMENT_HEALTH_PORT", "BILLING_HEALTH_PORT", "NOTIFICATIONS_HEALTH_PORT", "PROJECTOR_HEALTH_PORT",
        "FACT_RETRY_MAX_ATTEMPTS", "FACT_RETRY_BACKOFF_MS",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void Configure_BuildsEveryDocumentedDefault_WhenNoEnvVarsAreSetExceptTheRequiredMongoPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        try
        {
            var options = new ProjectorOptions();
            ProjectorProgramConfiguration.Configure(options);

            Assert.Equal("localhost:9092", options.Kafka.BootstrapServers);
            Assert.Equal("nats://localhost:4222", options.Nats.Url);
            Assert.Equal("otc_read_model", options.Mongo.Database);
            Assert.Equal(
                $"mongodb://otc_mongo_root:dev-password@localhost:27017/?authSource=admin",
                options.Mongo.ConnectionUri);
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
        Environment.SetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS", "kafka-box:9999");
        Environment.SetEnvironmentVariable("NATS_URL", "nats://nats-box:5555");
        Environment.SetEnvironmentVariable("MONGO_HOST", "mongo-box");
        Environment.SetEnvironmentVariable("MONGO_HOST_PORT", "27099");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME", "custom_user");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("MONGO_DB_READMODEL", "custom_read_model");
        try
        {
            var options = new ProjectorOptions();
            ProjectorProgramConfiguration.Configure(options);

            Assert.Equal("kafka-box:9999", options.Kafka.BootstrapServers);
            Assert.Equal("nats://nats-box:5555", options.Nats.Url);
            Assert.Equal("custom_read_model", options.Mongo.Database);
            Assert.Equal(
                "mongodb://custom_user:s3cr3t@mongo-box:27099/?authSource=admin",
                options.Mongo.ConnectionUri);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// Review round 2, D5 rows 4–5 — #7's <c>fact-retry-dispatcher.spec</c>
    /// asserts BOTH halves of the family: the substitution below, and the
    /// DEFAULTS (<c>3</c>/<c>500</c>) when neither env var is set. Only
    /// <c>OrdersProgramConfigurationTests</c> carried this half; a changed
    /// fallback in <c>ProjectorProgramConfiguration.cs</c> went unnoticed
    /// by the whole suite.
    /// </summary>
    [Fact]
    public void Configure_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        try
        {
            var options = new ProjectorOptions();
            ProjectorProgramConfiguration.Configure(options);

            Assert.Equal(3, options.FactRetry.MaxAttempts);
            Assert.Equal(500, options.FactRetry.BackoffMs);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// D3 (review round 1) — design.md §3.5's two-variable retry policy
    /// family, `FACT_RETRY_MAX_ATTEMPTS`/`FACT_RETRY_BACKOFF_MS`, was
    /// unguarded here: <c>OrdersProgramConfigurationTests</c> already
    /// applies this SUBSTITUTION convention (CLAUDE.md's sibling-family
    /// rule) but Projector's own read had no test at all. Set to distinct,
    /// non-default, mutually non-interchangeable values, so repointing
    /// either read at the other's variable name fails THIS assertion
    /// naming the wrong value — never merely "a value was read".
    /// </summary>
    [Fact]
    public void Configure_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("FACT_RETRY_MAX_ATTEMPTS", "7");
        Environment.SetEnvironmentVariable("FACT_RETRY_BACKOFF_MS", "250");
        try
        {
            var options = new ProjectorOptions();
            ProjectorProgramConfiguration.Configure(options);

            Assert.Equal(7, options.FactRetry.MaxAttempts);
            Assert.Equal(250, options.FactRetry.BackoffMs);
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
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("KAFKA_HOST_PORT", "9099");
        Environment.SetEnvironmentVariable("NATS_CLIENT_HOST_PORT", "4999");
        try
        {
            var options = new ProjectorOptions();
            ProjectorProgramConfiguration.Configure(options);

            Assert.Equal("localhost:9099", options.Kafka.BootstrapServers);
            Assert.Equal("nats://localhost:4999", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_Throws_WhenMongoRootPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var options = new ProjectorOptions();
            var exception = Record.Exception(() => ProjectorProgramConfiguration.Configure(options));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MONGO_INITDB_ROOT_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>design.md §8.1/§9.2, group A4 — PROJECTOR_HEALTH_PORT defaults to 3006 when unset.</summary>
    [Fact]
    public void ConfigureHealth_DefaultsPortTo3006_WhenNoEnvVarIsSet()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        try
        {
            var options = new HealthOptions();
            ProjectorProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(3006, options.Port);
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
    public void ConfigureHealth_ReadsProjectorHealthPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        Environment.SetEnvironmentVariable("ORDERS_HEALTH_PORT", "23002");
        Environment.SetEnvironmentVariable("FULFILLMENT_HEALTH_PORT", "23003");
        Environment.SetEnvironmentVariable("BILLING_HEALTH_PORT", "23004");
        Environment.SetEnvironmentVariable("NOTIFICATIONS_HEALTH_PORT", "23005");
        Environment.SetEnvironmentVariable("PROJECTOR_HEALTH_PORT", "13006");
        try
        {
            var options = new HealthOptions();
            ProjectorProgramConfiguration.ConfigureHealth(options);

            Assert.Equal(13006, options.Port);
        }
        finally
        {
            ClearAll();
        }
    }
}
