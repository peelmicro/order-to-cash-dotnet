using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrderToCash.Gateway.Application.Ports;

namespace OrderToCash.Gateway.Presentation.Auth;

/// <summary>
/// Requires a valid bearer token on every route EXCEPT the ones
/// <c>.AllowAnonymous()</c> marks (openapi.yaml <c>bearerAuth</c>'s own
/// list: <c>POST /auth/login</c>, <c>GET /health/live</c>,
/// <c>GET /health/ready</c>, <c>GET /docs</c>) — deny by default, opt out
/// per-route, the same shape #7's <c>JwtAuthGuard</c> establishes
/// (<c>apps/gateway/src/presentation/guards/jwt-auth.guard.ts</c>). Hand-
/// rolled middleware rather than <c>AddAuthentication().AddJwtBearer()</c>:
/// there is exactly one statically-configured identity to authenticate
/// (domain-model.md §9), so the full authentication-handler pipeline buys
/// nothing a direct header check plus <see cref="ITokenService.Verify"/>
/// does not already give, and it needs the
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> package — which is
/// NOT part of the ASP.NET Core shared framework — to exist at all.
/// <c>Microsoft.AspNetCore.Authorization</c> (for the <see cref="IAllowAnonymous"/>
/// metadata marker) IS part of the shared framework, so <c>.AllowAnonymous()</c>
/// is used purely as a metadata marker here — no
/// <c>AddAuthentication()</c>/<c>AddAuthorization()</c> pipeline is
/// configured, this middleware is the only reader of that metadata.
/// Attaches the verified claims to <see cref="HttpContext.Items"/> under
/// <see cref="ClaimsItemKey"/> so <c>GET /auth/me</c> can read them without
/// re-verifying.
/// </summary>
public sealed class BearerAuthenticationMiddleware(RequestDelegate next)
{
    public const string ClaimsItemKey = "OperatorClaims";

    public async Task InvokeAsync(HttpContext context, ITokenService tokens)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            throw new InvalidTokenError("missing bearer token");
        }

        var token = header["Bearer ".Length..].Trim();
        context.Items[ClaimsItemKey] = tokens.Verify(token);

        await next(context).ConfigureAwait(false);
    }
}
