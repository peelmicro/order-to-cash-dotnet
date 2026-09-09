using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrderToCash.Gateway.Application.Ports;

namespace OrderToCash.Gateway.Infrastructure.Auth;

/// <summary>
/// <see cref="ITokenService"/> — a hand-rolled HS256 JWT, over
/// <see cref="System.Security.Cryptography.HMACSHA256"/> only, no JWT
/// library. #7's own equivalent (<c>apps/gateway/src/infrastructure/auth/jwt-token.adapter.ts</c>)
/// is the "one adapter, one library import" shape (a plain
/// <c>jsonwebtoken</c> sign/verify pair, no <c>@nestjs/jwt</c>/
/// <c>@nestjs/passport</c> indirection, reasoned there as: "there is
/// exactly one statically-configured identity to authenticate, so a
/// strategy/guard framework buys nothing"). .NET's equivalent off-the-shelf
/// choice, <c>Microsoft.AspNetCore.Authentication.JwtBearer</c>, is
/// genuinely NOT part of the ASP.NET Core shared framework (verified:
/// absent from <c>/usr/lib/dotnet/shared/Microsoft.AspNetCore.App/10.0.11/</c>)
/// — adopting it would be a NEW <c>PackageVersion</c> for a single demo
/// identity that needs no discovery document, no key rotation and no
/// multi-issuer support. This class is the narrower equivalent: the
/// framework's own <see cref="HMACSHA256"/> plus RFC 7519's plain
/// three-segment structure, nothing else, so the feature needs zero new
/// NuGet packages.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public IssuedToken Issue(TokenClaims claims)
    {
        var config = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.AddSeconds(config.ExpiresInSeconds);

        var header = new Dictionary<string, object?> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object?>
        {
            ["sub"] = claims.Sub,
            ["roles"] = claims.Roles,
            ["iss"] = config.Issuer,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
        };

        var signingInput = $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload))}";
        var token = $"{signingInput}.{Sign(signingInput, config.Secret)}";

        return new IssuedToken(token, config.ExpiresInSeconds);
    }

    public TokenClaims Verify(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidTokenError("token does not have the three required segments.");
        }

        var signingInput = $"{parts[0]}.{parts[1]}";

        byte[] providedSignature;
        byte[] expectedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[2]);
            expectedSignature = Base64UrlDecode(Sign(signingInput, options.Value.Secret));
        }
        catch (FormatException ex)
        {
            throw new InvalidTokenError($"token is not well-formed base64url: {ex.Message}");
        }

        // CryptographicOperations.FixedTimeEquals throws on mismatched
        // lengths rather than comparing — the length check itself leaks
        // nothing secret (the secret-dependent comparison never runs on a
        // signature of the wrong size regardless), so it is safe to do
        // before the constant-time comparison.
        if (providedSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
        {
            throw new InvalidTokenError("token signature verification failed.");
        }

        Dictionary<string, JsonElement> payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64UrlDecode(parts[1]))
                ?? throw new InvalidTokenError("token payload deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidTokenError($"token payload is not valid JSON: {ex.Message}");
        }

        if (!payload.TryGetValue("sub", out var subElement) || subElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidTokenError("token carries no subject.");
        }

        if (!payload.TryGetValue("exp", out var expElement) || !expElement.TryGetInt64(out var expUnixSeconds))
        {
            throw new InvalidTokenError("token carries no expiry.");
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds) <= clock.UtcNow)
        {
            throw new InvalidTokenError("token has expired.");
        }

        if (payload.TryGetValue("iss", out var issuerElement)
            && issuerElement.ValueKind == JsonValueKind.String
            && issuerElement.GetString() != options.Value.Issuer)
        {
            throw new InvalidTokenError("token issuer does not match.");
        }

        var roles = payload.TryGetValue("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array
            ? rolesElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : [];

        return new TokenClaims(subElement.GetString()!, roles);
    }

    private static string Sign(string signingInput, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(padded);
    }
}
