using OrderToCash.Gateway.Infrastructure.Auth;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Fix round, review defect D3 — <c>JwtOptions.FromEnvironment()</c> in the
/// shape of <see cref="LoginThrottleOptionsTests"/>. Before this class, the
/// JWT secret/issuer/lifetime were compile-time constants only: this
/// repository's own <c>JWT_SECRET</c>/<c>JWT_EXPIRES_IN</c>/<c>JWT_ISSUER</c>
/// keys — the same three names #7's <c>jwt.config.ts</c> reads — appeared
/// nowhere in code, <c>.env.example</c> or <c>docker-compose.infra.yml</c>.
/// </summary>
public sealed class JwtOptionsTests
{
    [Fact]
    public void FromEnvironment_DefaultsToTheDocumentedSecretIssuerAndOneHourLifetime_WhenNoEnvVarsAreSet()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", null);
        Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        Environment.SetEnvironmentVariable("JWT_EXPIRES_IN", null);

        var options = JwtOptions.FromEnvironment();

        Assert.Equal("otc_dev_jwt_secret_change_me", options.Secret);
        Assert.Equal("order-to-cash", options.Issuer);
        Assert.Equal(3_600, options.ExpiresInSeconds);
    }

    [Fact]
    public void FromEnvironment_ReadsAllThreeVariables_WhenSet()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "a-genuinely-different-secret");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "otc-test-issuer");
        Environment.SetEnvironmentVariable("JWT_EXPIRES_IN", "900");
        try
        {
            var options = JwtOptions.FromEnvironment();

            Assert.Equal("a-genuinely-different-secret", options.Secret);
            Assert.Equal("otc-test-issuer", options.Issuer);
            Assert.Equal(900, options.ExpiresInSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_SECRET", null);
            Environment.SetEnvironmentVariable("JWT_ISSUER", null);
            Environment.SetEnvironmentVariable("JWT_EXPIRES_IN", null);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void FromEnvironment_FallsBackToTheDocumentedDefaultLifetime_ForAMalformedOrNonPositiveValue(string malformed)
    {
        Environment.SetEnvironmentVariable("JWT_EXPIRES_IN", malformed);
        try
        {
            var options = JwtOptions.FromEnvironment();

            Assert.Equal(3_600, options.ExpiresInSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_EXPIRES_IN", null);
        }
    }

    [Fact]
    public void FromEnvironment_IgnoresAnEmptySecretOrIssuer_FallingBackToTheDocumentedDefault()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "");
        try
        {
            var options = JwtOptions.FromEnvironment();

            Assert.Equal("otc_dev_jwt_secret_change_me", options.Secret);
            Assert.Equal("order-to-cash", options.Issuer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_SECRET", null);
            Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        }
    }
}
