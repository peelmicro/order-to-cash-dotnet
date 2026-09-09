namespace OrderToCash.Gateway.Infrastructure.Auth;

/// <summary>
/// Fix round, review defect D3 — the secret, issuer and lifetime are no
/// longer compile-time constants only: <see cref="FromEnvironment"/> reads
/// <c>JWT_SECRET</c>/<c>JWT_EXPIRES_IN</c>/<c>JWT_ISSUER</c> from the
/// environment, the same three keys #7's own
/// <c>apps/gateway/src/infrastructure/auth/jwt.config.ts</c> reads,
/// falling back to the SAME defaults when unset — this class's own
/// property initialisers stay the single source of truth for those
/// defaults, exactly as <c>LoginThrottleOptions</c>/<c>GatewayMongoOptions</c>
/// already do. <c>JWT_EXPIRES_IN</c> is read as an integer count of
/// seconds (matching this class's own <see cref="ExpiresInSeconds"/> type)
/// rather than #7's duration-string ("1h") — #7's own default, 1 hour,
/// already equals this class's own default of 3600 seconds, so no
/// behaviour is lost, only the string-duration parser #8 has no other use
/// for.
/// </summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = "otc_dev_jwt_secret_change_me";

    public int ExpiresInSeconds { get; set; } = 3_600;

    public string Issuer { get; set; } = "order-to-cash";

    public static JwtOptions FromEnvironment()
    {
        var options = new JwtOptions();

        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (!string.IsNullOrEmpty(secret))
        {
            options.Secret = secret;
        }

        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
        if (!string.IsNullOrEmpty(issuer))
        {
            options.Issuer = issuer;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRES_IN"), out var expiresInSeconds) && expiresInSeconds >= 1)
        {
            options.ExpiresInSeconds = expiresInSeconds;
        }

        return options;
    }
}
