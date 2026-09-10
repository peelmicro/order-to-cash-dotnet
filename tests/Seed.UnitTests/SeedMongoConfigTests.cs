using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Seed.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> —
/// <see cref="SeedMongoConfig.Load"/> is reachable from the Seed composition
/// root (<c>src/Seed/Program.cs</c> → <c>SeedRunner.RunAsync</c> →
/// <c>SeedMongoConfig.Load()</c>) and had NO test of its own before this
/// file.
/// </summary>
public sealed class SeedMongoConfigTests
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
    public void Load_BuildsTheDocumentedDefaultUri_WhenNoEnvVarsAreSetExceptTheRequiredPassword()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");
        try
        {
            var (connectionUri, database) = SeedMongoConfig.Load();

            Assert.Equal("otc_read_model", database);
            Assert.Equal("mongodb://otc_mongo_root:dev-password@localhost:27017/?authSource=admin", connectionUri);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Load_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("MONGO_HOST", "mongo-box");
        Environment.SetEnvironmentVariable("MONGO_HOST_PORT", "27099");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME", "custom_user");
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "s3cr3t");
        Environment.SetEnvironmentVariable("MONGO_DB_READMODEL", "custom_read_model");
        try
        {
            var (connectionUri, database) = SeedMongoConfig.Load();

            Assert.Equal("custom_read_model", database);
            Assert.Equal("mongodb://custom_user:s3cr3t@mongo-box:27099/?authSource=admin", connectionUri);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Load_Throws_WhenMongoRootPasswordIsNotSet()
    {
        ClearAll();
        try
        {
            var exception = Record.Exception(() => SeedMongoConfig.Load());

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("MONGO_INITDB_ROOT_PASSWORD", exception!.Message);
        }
        finally
        {
            ClearAll();
        }
    }
}
