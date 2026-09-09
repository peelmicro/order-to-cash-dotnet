using OrderToCash.Cqrs;
using OrderToCash.Gateway.Domain.Auth;

namespace OrderToCash.Gateway.Application.Queries;

public sealed record CurrentUserResult(string Username, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>The username named by an already-verified bearer token names no configured operator — cannot actually happen with today's single-identity model, but the vocabulary the mapper answers with must be complete.</summary>
public sealed class UnknownOperatorError(string username) : Exception($"no operator identity for \"{username}\"");

/// <summary><c>GET /auth/me</c> — describes the operator identified by the already-verified bearer token. Re-resolves the full identity from configuration rather than trusting a token's own baked-in claims, so a configuration change takes effect on the very next call even for a token issued before it.</summary>
public sealed record GetCurrentUserQuery(string Username) : IQuery<CurrentUserResult>;

public sealed class GetCurrentUserQueryHandler(OperatorIdentity operatorIdentity) : IQueryHandler<GetCurrentUserQuery, CurrentUserResult>
{
    public Task<CurrentUserResult> HandleAsync(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (query.Username != operatorIdentity.Username)
        {
            throw new UnknownOperatorError(query.Username);
        }

        return Task.FromResult(new CurrentUserResult(operatorIdentity.Username, operatorIdentity.DisplayName, operatorIdentity.Roles));
    }
}
