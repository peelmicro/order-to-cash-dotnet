using OrderToCash.Gateway.Domain.Auth;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class OperatorAuthenticatorTests
{
    private static readonly OperatorIdentity _configured = new("operator", "otc_operator_dev_password_change_me", "Order-To-Cash Operator", ["operator"]);

    [Fact]
    public void Matches_ReturnsTrue_ForTheExactConfiguredPair()
    {
        Assert.True(OperatorAuthenticator.Matches(new OperatorCredentials("operator", "otc_operator_dev_password_change_me"), _configured));
    }

    [Theory]
    [InlineData("operator", "wrong-password")]
    [InlineData("wrong-user", "otc_operator_dev_password_change_me")]
    [InlineData("", "")]
    [InlineData("Operator", "otc_operator_dev_password_change_me")]
    public void Matches_ReturnsFalse_ForAnyMismatch(string username, string password)
    {
        Assert.False(OperatorAuthenticator.Matches(new OperatorCredentials(username, password), _configured));
    }
}
