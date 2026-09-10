using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure;
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
}
