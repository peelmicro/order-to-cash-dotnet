using OrderToCash.Gateway.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> —
/// <see cref="GatewayMongoOptions.FromEnvironment"/>'s five environment
/// reads had NO test of their own before this file, unlike its sibling
/// loaders (<see cref="JwtOptionsTests"/>, <see cref="LoginThrottleOptionsTests"/>,
/// <see cref="GatewaySseOptionsTests"/>) — this closes that specific gap,
/// in the same shape those already established.
/// </summary>
[Collection(GatewayEnvironmentVariableTestCollection.Name)]
public sealed class GatewayMongoOptionsTests
{
    private static readonly string[] _envVars =
    [
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
    public void FromEnvironment_BuildsTheDocumentedDefaultUri_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        try
        {
            var options = GatewayMongoOptions.FromEnvironment();

            Assert.Equal("otc_read_model", options.Database);
            Assert.Equal("mongodb://otc_mongo_root:dev-password@localhost:27017/?authSource=admin", options.ConnectionUri);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void FromEnvironment_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_HOST", "mongo-box");
        Environment.SetEnvironmentVariable("MONGO_HOST_PORT", "27099");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME", "custom_user");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("MONGO_DB_READMODEL", "custom_read_model");
        try
        {
            var options = GatewayMongoOptions.FromEnvironment();

            Assert.Equal("custom_read_model", options.Database);
            Assert.Equal("mongodb://custom_user:s3cr3t@mongo-box:27099/?authSource=admin", options.ConnectionUri);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void FromEnvironment_Throws_WhenMongoRootPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var exception = Record.Exception(() => GatewayMongoOptions.FromEnvironment());

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MONGO_INITDB_ROOT_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }
}
