using OrderToCash.Gateway;
using OrderToCash.Gateway.Infrastructure;
using OrderToCash.Gateway.Infrastructure.Auth;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Persistence;
using OrderToCash.Gateway.Infrastructure.RateLimiting;

var app = GatewayHost.Build(
    args,
    configure: options =>
    {
        options.Nats.Url = BuildNatsUrl();
        options.Mongo = GatewayMongoOptions.FromEnvironment();
        options.LoginThrottle = LoginThrottleOptions.FromEnvironment();
        options.Jwt = JwtOptions.FromEnvironment();
        options.Sse = GatewaySseOptions.FromEnvironment();
    });

await app.RunAsync().ConfigureAwait(false);

// Mirrors ProjectorNatsOptions' own reading of the environment — the same
// NATS_CLIENT_HOST_PORT/NATS_URL variable names every other service reads.
static string BuildNatsUrl()
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
