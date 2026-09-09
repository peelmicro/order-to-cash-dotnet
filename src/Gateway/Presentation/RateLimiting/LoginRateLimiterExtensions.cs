using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Contracts.Wire;
using OrderToCash.Gateway.Infrastructure.RateLimiting;

namespace OrderToCash.Gateway.Presentation.RateLimiting;

/// <summary>
/// R63 — a rate limit on <c>POST /auth/login</c> ONLY, over
/// <c>Microsoft.AspNetCore.RateLimiting</c> (part of the ASP.NET Core
/// shared framework — no new package), applied as a route-scoped policy
/// (<c>.RequireRateLimiting(PolicyName)</c> on that one route only, never a
/// global middleware) so every other endpoint stays unthrottled. The
/// rejection short-circuits BEFORE the login handler ever runs, so R63's
/// "issues no token" and "independent of whether the submitted credentials
/// were valid" both hold by construction — a rejected request never reaches
/// <c>LoginCommandHandler</c> to find out whether the credentials were
/// right.
/// </summary>
public static class LoginRateLimiterExtensions
{
    public const string PolicyName = "login";

    public static IServiceCollection AddLoginRateLimiter(this IServiceCollection services, LoginThrottleOptions throttle)
    {
        return services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter(PolicyName, window =>
            {
                window.PermitLimit = throttle.PermitLimit;
                window.Window = TimeSpan.FromSeconds(throttle.WindowSeconds);
                window.QueueLimit = 0;
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = throttle.WindowSeconds;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                }

                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";

                var correlationId = context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is Guid guid
                    ? guid
                    : Guid.NewGuid();

                var body = new Dictionary<string, object?>
                {
                    ["type"] = "about:blank",
                    ["title"] = "Too many requests",
                    ["status"] = StatusCodes.Status429TooManyRequests,
                    ["detail"] = "Too many login attempts — try again later.",
                    ["code"] = "TOO_MANY_REQUESTS",
                    ["correlationId"] = correlationId,
                    ["occurredAt"] = DateTimeOffset.UtcNow,
                };

                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(body, JsonWire.Options), cancellationToken).ConfigureAwait(false);
            };
        });
    }
}
