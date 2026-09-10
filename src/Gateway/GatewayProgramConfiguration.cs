using OrderToCash.Gateway.Infrastructure;
using OrderToCash.Gateway.Infrastructure.Auth;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Persistence;
using OrderToCash.Gateway.Infrastructure.RateLimiting;

namespace OrderToCash.Gateway;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — the shape
/// <c>BillingProgramConfiguration</c> establishes, applied here: the Gateway
/// host's environment-reading half of composition, extracted out of
/// <c>Program.cs</c>'s inline <c>configure</c> delegate (and its
/// <c>BuildNatsUrl</c> local function) so
/// <c>GatewayProgramConfigurationTests</c> can call the EXACT method
/// <c>Program.cs</c> calls. <c>JwtOptions</c>/<c>GatewaySseOptions</c>/
/// <c>GatewayMongoOptions</c>/<c>LoginThrottleOptions</c>' own
/// <c>FromEnvironment()</c> loaders were already unit-tested
/// (<c>JwtOptionsTests</c> etc.) — what was NOT tested is this call site:
/// deleting e.g. <c>options.Jwt = JwtOptions.FromEnvironment();</c> would
/// silently leave <see cref="Infrastructure.GatewayOptions.Jwt"/> at its own
/// property-initialiser default, which happens to equal
/// <c>FromEnvironment()</c>'s own no-env-set default — so only a test that
/// sets a NON-default env var and asserts it propagated through THIS method
/// can catch that deletion.
/// </summary>
public static class GatewayProgramConfiguration
{
    public static void Configure(GatewayOptions options)
    {
        options.Nats.Url = BuildNatsUrl();
        options.Mongo = GatewayMongoOptions.FromEnvironment();
        options.LoginThrottle = LoginThrottleOptions.FromEnvironment();
        options.Jwt = JwtOptions.FromEnvironment();
        options.Sse = GatewaySseOptions.FromEnvironment();
    }

    // Mirrors ProjectorNatsOptions' own reading of the environment — the same
    // NATS_CLIENT_HOST_PORT/NATS_URL variable names every other service reads.
    public static string BuildNatsUrl()
    {
        var explicitUrl = Environment.GetEnvironmentVariable("NATS_URL");
        if (!string.IsNullOrEmpty(explicitUrl))
        {
            return explicitUrl;
        }

        var host = Environment.GetEnvironmentVariable("NATS_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222";
        return $"nats://{host}:{port}";
    }
}
