using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Presentation.Auth;
using OrderToCash.Gateway.Presentation.Dto;
using OrderToCash.Gateway.Presentation.RateLimiting;

namespace OrderToCash.Gateway.Presentation.Endpoints;

/// <summary><c>POST /auth/login</c>, <c>GET /auth/me</c> (openapi.yaml <c>auth</c> tag).</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (LoginRequestDto body, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var issued = await dispatcher.SendAsync<LoginCommand, IssuedToken>(
                    new LoginCommand(body.Username, body.Password), cancellationToken).ConfigureAwait(false);

                return Results.Json(new LoginResponseDto(issued.AccessToken, "Bearer", issued.ExpiresIn), statusCode: StatusCodes.Status200OK);
            })
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimiterExtensions.PolicyName);

        app.MapGet("/auth/me", async (HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var claims = (TokenClaims)context.Items[BearerAuthenticationMiddleware.ClaimsItemKey]!;
            var result = await dispatcher.QueryAsync<GetCurrentUserQuery, CurrentUserResult>(
                new GetCurrentUserQuery(claims.Sub), cancellationToken).ConfigureAwait(false);

            return Results.Json(result);
        });

        return app;
    }
}
