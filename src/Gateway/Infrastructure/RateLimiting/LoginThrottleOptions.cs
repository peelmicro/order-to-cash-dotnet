namespace OrderToCash.Gateway.Infrastructure.RateLimiting;

/// <summary>
/// Rate-limit policy for <c>POST /auth/login</c> ONLY (R63,
/// openapi.yaml <c>components.responses.TooManyRequests</c>). Applied as a
/// route-scoped ASP.NET Core RateLimiting policy
/// (<c>Microsoft.AspNetCore.RateLimiting</c>, part of the shared framework
/// — no new package), never a global middleware: every other route stays
/// unthrottled. Default 10 attempts per 60-second fixed window, matching
/// #7's own default (<c>apps/gateway/src/infrastructure/auth/login-throttle.config.ts</c>).
/// </summary>
public sealed class LoginThrottleOptions
{
    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;

    public static LoginThrottleOptions FromEnvironment()
    {
        var options = new LoginThrottleOptions();

        if (int.TryParse(Environment.GetEnvironmentVariable("GATEWAY_LOGIN_RATE_LIMIT"), out var limit) && limit >= 1)
        {
            options.PermitLimit = limit;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("GATEWAY_LOGIN_RATE_WINDOW_SECONDS"), out var windowSeconds) && windowSeconds >= 1)
        {
            options.WindowSeconds = windowSeconds;
        }

        return options;
    }
}
