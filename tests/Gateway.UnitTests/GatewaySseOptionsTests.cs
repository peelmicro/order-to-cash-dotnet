using OrderToCash.Gateway.Infrastructure.Messaging;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// <b>Not a port</b> — #7's own <c>infrastructure/messaging/sse.config.ts</c>
/// (<c>loadSseConfig</c>) has no <c>.spec.ts</c> file of its own at all
/// (confirmed: <c>find apps/gateway/src -iname "sse.config*"</c> returns
/// only the source file), so there is nothing to port here. This test
/// exists purely for parity with THIS repository's own established
/// <c>*Options.FromEnvironment()</c> testing convention
/// (<c>LoginThrottleOptionsTests</c>, <c>JwtOptionsTests</c>): defaults,
/// valid override, malformed/non-positive → fallback — over the SAME two
/// env var names and defaults #7's <c>loadSseConfig</c> reads (500 / 15000),
/// which <c>GatewaySseOptions</c>'s own doc comment cites.
/// </summary>
public sealed class GatewaySseOptionsTests
{
    [Fact]
    public void FromEnvironment_DefaultsToFiveHundredAndFifteenThousandMs_WhenNoEnvVarsAreSet()
    {
        Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", null);
        Environment.SetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS", null);

        var options = GatewaySseOptions.FromEnvironment();

        Assert.Equal(500, options.BufferCapacity);
        Assert.Equal(15_000, options.PingIntervalMs);
    }

    [Fact]
    public void FromEnvironment_ReadsBothVariables_WhenSetToValidPositiveIntegers()
    {
        Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", "50");
        Environment.SetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS", "200");
        try
        {
            var options = GatewaySseOptions.FromEnvironment();

            Assert.Equal(50, options.BufferCapacity);
            Assert.Equal(200, options.PingIntervalMs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", null);
            Environment.SetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS", null);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void FromEnvironment_FallsBackToTheDocumentedDefault_ForAMalformedOrNonPositiveBufferCapacity(string malformed)
    {
        Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", malformed);
        try
        {
            var options = GatewaySseOptions.FromEnvironment();

            Assert.Equal(500, options.BufferCapacity);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_SSE_BUFFER_CAPACITY", null);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void FromEnvironment_FallsBackToTheDocumentedDefault_ForAMalformedOrNonPositivePingInterval(string malformed)
    {
        Environment.SetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS", malformed);
        try
        {
            var options = GatewaySseOptions.FromEnvironment();

            Assert.Equal(15_000, options.PingIntervalMs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_SSE_PING_INTERVAL_MS", null);
        }
    }
}
