namespace OrderToCash.Gateway.Application.Ports;

public sealed record TokenClaims(string Sub, IReadOnlyList<string> Roles);

public sealed record IssuedToken(string AccessToken, int ExpiresIn);

/// <summary>Thrown by <see cref="ITokenService.Verify"/> when a bearer token is missing, malformed, expired or fails signature verification — never a specific reason on the wire, only "unauthorized" (openapi.yaml <c>Unauthorized</c>).</summary>
public sealed class InvalidTokenError(string message) : Exception(message);

/// <summary>Issues and verifies the bearer token this API's single operator identity authenticates with. One implementation, <c>Infrastructure/Auth/JwtTokenService.cs</c>.</summary>
public interface ITokenService
{
    IssuedToken Issue(TokenClaims claims);

    /// <summary>Throws <see cref="InvalidTokenError"/> rather than returning <see langword="null"/> — the caller (the authentication middleware) always wants a reason to log, never a silent miss.</summary>
    TokenClaims Verify(string token);
}
