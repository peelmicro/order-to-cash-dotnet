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
        "GATEWAY_PORT", "ORDERS_HEALTH_PORT", "FULFILLMENT_HEALTH_PORT", "BILLING_HEALTH_PORT", "NOTIFICATIONS_HEALTH_PORT", "PROJECTOR_HEALTH_PORT", "WEB_PORT",
    ];

    /// <summary>Backlog id 96 — GATEWAY_PORT's sibling family: every other <c>*_PORT</c> a service process of this stack reads for its own listener, each given a DISTINCT non-default value so a repointed read fails naming the key it read.</summary>
    private static readonly (string Name, int Value)[] _siblingPorts =
    [
        ("ORDERS_HEALTH_PORT", 23002),
        ("FULFILLMENT_HEALTH_PORT", 23003),
        ("BILLING_HEALTH_PORT", 23004),
        ("NOTIFICATIONS_HEALTH_PORT", 23005),
        ("PROJECTOR_HEALTH_PORT", 23006),
        ("WEB_PORT", 23010),
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

    /// <summary>Backlog id 96 — GATEWAY_PORT defaults to 3001 when unset, exactly as #7's <c>Number(process.env.GATEWAY_PORT ?? 3001)</c> (apps/gateway/src/main.ts:30).</summary>
    [Fact]
    public void Configure_DefaultsGatewayPortTo3001_WhenGatewayPortIsUnset()
    {
        ClearAll();
        SetRequiredMongoPassword();
        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.True(
                options.Port == 3001,
                $"GATEWAY_PORT unset must default GatewayOptions.Port to 3001 (#7 main.ts:30); got {Describe(options.Port)}.");
        }
        finally
        {
            ClearAll();
        }
    }

    /// <summary>
    /// ⚑ARM — backlog id 96, substitution: GATEWAY_PORT and every sibling
    /// port variable are set to DISTINCT non-default values before the
    /// assertion, so repointing the read at a sibling fails on that
    /// sibling's value — and the message names the key that was read —
    /// never on the fallback-to-default reason. Deleting the read fails
    /// here too (the port stays unset).
    /// </summary>
    [Fact]
    public void Configure_ReadsGatewayPort_FromItsOwnDistinctVariableName_NeverASiblingsKey()
    {
        ClearAll();
        SetRequiredMongoPassword();
        Environment.SetEnvironmentVariable("GATEWAY_PORT", "13001");
        foreach (var (name, value) in _siblingPorts)
        {
            Environment.SetEnvironmentVariable(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        try
        {
            var options = new GatewayOptions();
            GatewayProgramConfiguration.Configure(options);

            Assert.True(
                options.Port == 13001,
                $"GatewayOptions.Port must come from GATEWAY_PORT (=13001); got {Describe(options.Port)}.");
        }
        finally
        {
            ClearAll();
        }
    }

    private static string Describe(int? port)
    {
        if (port is null)
        {
            return "null — GATEWAY_PORT was never read";
        }

        foreach (var (name, value) in _siblingPorts)
        {
            if (value == port)
            {
                return $"{port} — the value of {name}, so the read is repointed at {name}";
            }
        }

        return port == 3001 ? "3001 — the default, so GATEWAY_PORT was not read" : port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
