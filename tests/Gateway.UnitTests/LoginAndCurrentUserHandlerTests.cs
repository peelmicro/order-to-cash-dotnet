using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Domain.Auth;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class LoginAndCurrentUserHandlerTests
{
    private sealed class FakeTokenService : ITokenService
    {
        public TokenClaims? IssuedFor { get; private set; }

        public IssuedToken Issue(TokenClaims claims)
        {
            IssuedFor = claims;
            return new IssuedToken("fake-token", 3600);
        }

        public TokenClaims Verify(string token) => throw new NotSupportedException();
    }

    private static readonly OperatorIdentity _operator = new("operator", "otc_operator_dev_password_change_me", "Order-To-Cash Operator", ["operator"]);

    [Fact]
    public async Task LoginCommandHandler_IssuesAToken_ForTheCorrectCredentials()
    {
        var tokens = new FakeTokenService();
        var handler = new LoginCommandHandler(_operator, tokens);

        var issued = await handler.HandleAsync(new LoginCommand("operator", "otc_operator_dev_password_change_me"), CancellationToken.None);

        Assert.Equal("fake-token", issued.AccessToken);
        Assert.Equal("operator", tokens.IssuedFor!.Sub);
    }

    [Fact]
    public async Task LoginCommandHandler_Throws_ForTheWrongPassword()
    {
        var handler = new LoginCommandHandler(_operator, new FakeTokenService());

        await Assert.ThrowsAsync<InvalidCredentialsError>(
            () => handler.HandleAsync(new LoginCommand("operator", "wrong"), CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentUserQueryHandler_ReturnsTheOperatorsIdentity_ForTheMatchingUsername()
    {
        var handler = new GetCurrentUserQueryHandler(_operator);

        var result = await handler.HandleAsync(new GetCurrentUserQuery("operator"), CancellationToken.None);

        Assert.Equal("Order-To-Cash Operator", result.DisplayName);
        Assert.Equal(["operator"], result.Roles);
    }

    [Fact]
    public async Task GetCurrentUserQueryHandler_Throws_ForAnUnknownUsername()
    {
        var handler = new GetCurrentUserQueryHandler(_operator);

        await Assert.ThrowsAsync<UnknownOperatorError>(
            () => handler.HandleAsync(new GetCurrentUserQuery("someone-else"), CancellationToken.None));
    }
}
