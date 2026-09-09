using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Auth;

namespace OrderToCash.Gateway.Application.Commands;

/// <summary><c>POST /auth/login</c> — the only unauthenticated command endpoint. Compares the submitted pair against the single, statically-configured operator identity.</summary>
public sealed class InvalidCredentialsError() : Exception("username or password is incorrect");

public sealed record LoginCommand(string Username, string Password) : ICommand<IssuedToken>;

public sealed class LoginCommandHandler(OperatorIdentity operatorIdentity, ITokenService tokens) : ICommandHandler<LoginCommand, IssuedToken>
{
    public Task<IssuedToken> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (!OperatorAuthenticator.Matches(new OperatorCredentials(command.Username, command.Password), operatorIdentity))
        {
            throw new InvalidCredentialsError();
        }

        return Task.FromResult(tokens.Issue(new TokenClaims(operatorIdentity.Username, operatorIdentity.Roles)));
    }
}
