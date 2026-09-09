using Microsoft.Extensions.Options;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Auth;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class JwtTokenServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    }

    private static JwtTokenService Build(FakeClock clock, string? secret = null, string? issuer = null) =>
        new(Options.Create(new JwtOptions { Secret = secret ?? "test-secret-value", Issuer = issuer ?? "order-to-cash", ExpiresInSeconds = 3600 }), clock);

    [Fact]
    public void Verify_ReturnsTheOriginalClaims_ForATokenThisServiceJustIssued()
    {
        var clock = new FakeClock();
        var service = Build(clock);

        var issued = service.Issue(new TokenClaims("operator", ["operator"]));
        var claims = service.Verify(issued.AccessToken);

        Assert.Equal("operator", claims.Sub);
        Assert.Equal(["operator"], claims.Roles);
        Assert.Equal(3600, issued.ExpiresIn);
    }

    [Fact]
    public void Verify_Throws_WhenTheTokenHasExpired()
    {
        var clock = new FakeClock();
        var service = Build(clock);
        var issued = service.Issue(new TokenClaims("operator", ["operator"]));

        clock.UtcNow = clock.UtcNow.AddSeconds(3601);

        Assert.Throws<InvalidTokenError>(() => service.Verify(issued.AccessToken));
    }

    [Fact]
    public void Verify_Throws_WhenTheSignatureWasTamperedWith()
    {
        var clock = new FakeClock();
        var service = Build(clock);
        var issued = service.Issue(new TokenClaims("operator", ["operator"]));

        var parts = issued.AccessToken.Split('.');
        var tamperedSignature = parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A');
        var tampered = $"{parts[0]}.{parts[1]}.{tamperedSignature}";

        Assert.Throws<InvalidTokenError>(() => service.Verify(tampered));
    }

    [Fact]
    public void Verify_Throws_WhenSignedByADifferentSecret()
    {
        var clock = new FakeClock();
        var issuer = Build(clock, secret: "secret-one");
        var verifier = Build(clock, secret: "secret-two");

        var issued = issuer.Issue(new TokenClaims("operator", ["operator"]));

        Assert.Throws<InvalidTokenError>(() => verifier.Verify(issued.AccessToken));
    }

    [Fact]
    public void Verify_Throws_WhenTheIssuerDiffers()
    {
        var clock = new FakeClock();
        var issuer = Build(clock, issuer: "issuer-one");
        var verifier = Build(clock, issuer: "issuer-two");

        var issued = issuer.Issue(new TokenClaims("operator", ["operator"]));

        Assert.Throws<InvalidTokenError>(() => verifier.Verify(issued.AccessToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    public void Verify_Throws_ForAStructurallyMalformedToken(string malformed)
    {
        var service = Build(new FakeClock());

        Assert.Throws<InvalidTokenError>(() => service.Verify(malformed));
    }

    /// <summary>
    /// Fix round, review defect D9 — the classic <c>alg: none</c> algorithm-
    /// confusion attack: a forged token whose header declares no algorithm
    /// and whose signature segment is EMPTY. <see cref="JwtTokenService.Verify"/>
    /// never parses the header at all (Sign is unconditionally HMACSHA256),
    /// so the empty provided signature simply fails the length check
    /// against the real 32-byte HMACSHA256 signature before
    /// <c>FixedTimeEquals</c> ever runs — algorithm confusion is
    /// structurally impossible here, not merely rejected by a header check.
    /// </summary>
    [Fact]
    public void Verify_Throws_ForAnAlgNoneForgedToken_WithAnEmptySignatureSegment()
    {
        var service = Build(new FakeClock());
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}"""u8.ToArray());
        var payload = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(
            $$"""{"sub":"operator","roles":["operator"],"iss":"order-to-cash","iat":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},"exp":{{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"""));
        var forged = $"{header}.{payload}.";

        Assert.Throws<InvalidTokenError>(() => service.Verify(forged));
    }

    /// <summary>Fix round, review defect D9 — the signature is valid for the ORIGINAL payload only; substituting a different <c>sub</c> after issuance (e.g. privilege escalation to "admin") must fail verification even though the signature bytes are untouched.</summary>
    [Fact]
    public void Verify_Throws_WhenThePayloadSubjectIsTamperedWith_EvenIfTheSignatureBytesAreUntouched()
    {
        var clock = new FakeClock();
        var service = Build(clock);
        var issued = service.Issue(new TokenClaims("operator", ["operator"]));

        var parts = issued.AccessToken.Split('.');
        var forgedPayload = Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(
            $$"""{"sub":"admin","roles":["operator"],"iss":"order-to-cash","iat":{{clock.UtcNow.ToUnixTimeSeconds()}},"exp":{{clock.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"""));
        var tampered = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        Assert.Throws<InvalidTokenError>(() => service.Verify(tampered));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
