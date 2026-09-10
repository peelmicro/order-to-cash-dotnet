using OrderToCash.Gateway;
using OrderToCash.Gateway.Infrastructure;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — drives
/// <see cref="GatewayProgramConfiguration.Configure"/>, the EXACT method
/// <c>Program.cs</c> calls (<c>GatewayHost.Build(args, configure:
/// GatewayProgramConfiguration.Configure)</c>). <c>JwtOptions</c>/
/// <c>GatewaySseOptions</c>/<c>GatewayMongoOptions</c>/
/// <c>LoginThrottleOptions</c>' own <c>FromEnvironment()</c> loaders were
/// already unit-tested — what none of those tests could catch is THIS call
/// site: deleting e.g. <c>options.Jwt = JwtOptions.FromEnvironment();</c>
/// would silently leave <c>GatewayOptions.Jwt</c> at its own
/// property-initialiser default, which happens to equal
/// <c>FromEnvironment()</c>'s own no-env-set default — so every test below
/// sets a NON-default env var for the field it checks and asserts THAT
/// value propagated through <see cref="GatewayProgramConfiguration.Configure"/>,
/// not merely that some default is present.
/// </summary>
[Collection(GatewayEnvironmentVariableTestCollection.Name)]
public sealed class GatewayProgramConfigurationTests
{
    private static readonly string[] _envVars =
    [
        "NATS_URL", "NATS_HOST", "NATS_CLIENT_HOST_PORT",
        "MONGO_HOST", "MONGO_HOST_PORT", "MONGO_INITDB_ROOT_USERNAME", "MONGO_INITDB_ROOT_PASSWORD", "MONGO_DB_READMODEL",
        "GATEWAY_LOGIN_RATE_LIMIT", "GATEWAY_LOGIN_RATE_WINDOW_SECONDS",
        "JWT_SECRET", "JWT_ISSUER", "JWT_EXPIRES_IN",
        "GATEWAY_SSE_BUFFER_CAPACITY", "GATEWAY_SSE_PING_INTERVAL_MS",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>Mongo's password has no fallback (<c>GatewayMongoOptions.FromEnvironment</c> throws without it) — every test needs it set so <see cref="GatewayProgramConfiguration.Configure"/> can complete regardless of which other field it is probing.</summary>
    private static void SetRequiredMongoPassword() =>
        Environment.SetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD", "dev-password");

    [Fact]
    public void Configure_SetsNatsUrlFromTheExplicitUrlEnvVar_WhenSet()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("NATS_URL", "nats://nats-box:5555");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal("nats://nats-box:5555", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_FallsBackToNatsHostAndClientHostPort_WhenTheExplicitUrlIsUnset()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("NATS_HOST", "nats-box");
        Environment.SetEnvironmentVariable("NATS_CLIENT_HOST_PORT", "4999");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal("nats://nats-box:4999", options.Nats.Url);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_SetsMongoFromEnvironment_ReflectingACustomDatabaseName()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("MONGO_DB_READMODEL", "custom_read_model_for_gateway");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal("custom_read_model_for_gateway", options.Mongo.Database);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_SetsLoginThrottleFromEnvironment_ReflectingACustomPermitLimit()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", "3");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal(3, options.LoginThrottle.PermitLimit);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_SetsJwtFromEnvironment_ReflectingACustomSecret()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("JWT_SECRET", "a-genuinely-different-program-configuration-secret");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal("a-genuinely-different-program-configuration-secret", options.Jwt.Secret);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void Configure_SetsSseFromEnvironment_ReflectingACustomBufferCapacity()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", "77");
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.Equal(77, options.Sse.BufferCapacity);
        }
        finally
        {
            ClearAll();
        }
    }
}
