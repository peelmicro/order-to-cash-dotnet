using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> — every test
/// class that mutates process-wide environment variables through
/// <see cref="Environment.SetEnvironmentVariable(string, string?)"/> shares
/// this collection, so xUnit runs them SEQUENTIALLY relative to each other
/// (xUnit parallelises across collections, never within one). Without this,
/// <see cref="GatewayProgramConfigurationTests"/> — which drives the SAME
/// <c>JWT_SECRET</c>/<c>GATEWAY_SSE_BUFFER_CAPACITY</c>/
/// <c>GATEWAY_LOGIN_RATE_LIMIT</c> variable names <see cref="JwtOptionsTests"/>,
/// <see cref="GatewaySseOptionsTests"/> and <see cref="LoginThrottleOptionsTests"/>
/// already mutate — would race against them on a shared process resource,
/// on every parallel test run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GatewayEnvironmentVariableTestCollection
{
    public const string Name = "Gateway environment variable tests";
}
