using OrderToCash.Gateway.Infrastructure.RateLimiting;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Ported from #7's <c>login-throttle.config.spec.ts</c>, minus the
/// structured-logging half of review finding F1 — <c>#8's</c> equivalent
/// static <c>*Options.FromEnvironment()</c> loaders elsewhere in this
/// repository (e.g. <c>ProjectorMongoOptions.FromEnvironment</c>) take no
/// <c>ILogger</c> and none of them log a rejected raw value either; this
/// loader follows that established, narrower precedent rather than
/// introducing a first exception to it. Recorded as a genuine, deliberate
/// gap against #7's own review finding, not silently closed.
/// </summary>
public sealed class LoginThrottleOptionsTests
{
    [Fact]
    public void FromEnvironment_DefaultsToTenAttemptsPerSixtySecondWindow_WhenNoEnvVarsAreSet()
    {
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", null);
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_WINDOW_SECONDS", null);

        var options = LoginThrottleOptions.FromEnvironment();

        Assert.Equal(10, options.PermitLimit);
        Assert.Equal(60, options.WindowSeconds);
    }

    [Fact]
    public void FromEnvironment_ReadsBothVariables_WhenSetToValidPositiveIntegers()
    {
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", "5");
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_WINDOW_SECONDS", "30");
        try
        {
            var options = LoginThrottleOptions.FromEnvironment();

            Assert.Equal(5, options.PermitLimit);
            Assert.Equal(30, options.WindowSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", null);
            Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_WINDOW_SECONDS", null);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void FromEnvironment_FallsBackToTheDocumentedDefault_ForAMalformedOrNonPositiveValue(string malformed)
    {
        Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", malformed);
        try
        {
            var options = LoginThrottleOptions.FromEnvironment();

            Assert.Equal(10, options.PermitLimit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT", null);
        }
    }
}
