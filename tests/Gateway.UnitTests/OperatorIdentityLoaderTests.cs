using OrderToCash.Gateway.Infrastructure.Auth;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Feature <c>composition_root_env_reads_are_unguarded</c> —
/// <see cref="OperatorIdentityLoader.FromEnvironment"/>'s three environment
/// reads had NO test of their own before this file. Reachable from the
/// composition root not through <see cref="OrderToCash.Gateway.GatewayProgramConfiguration.Configure"/>
/// but through <c>GatewayOptions</c>'s own property initialiser
/// (<c>public OperatorIdentity Operator { get; set; } = OperatorIdentityLoader.FromEnvironment();</c>),
/// which runs unconditionally the moment <c>GatewayHost.CreateBuilder</c>
/// constructs a fresh <c>GatewayOptions</c> — before <c>configure</c> is
/// even invoked. Bullet 1 of the feature's acceptance criteria names this
/// shape explicitly: "including reads that live in an Options class rather
/// than in Program.cs".
/// </summary>
[Collection(GatewayEnvironmentVariableTestCollection.Name)]
public sealed class OperatorIdentityLoaderTests
{
    private static readonly string[] _envVars =
    [
        "GATEWAY_OPERATOR_USERNAME", "GATEWAY_OPERATOR_PASSWORD", "GATEWAY_OPERATOR_DISPLAY_NAME",
    ];

    private static void ClearAll()
    {
        foreach (var name in _envVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void FromEnvironment_DefaultsToTheDocumentedIdentity_WhenNoEnvVarsAreSet()
    {
        ClearAll();
        try
        {
            var identity = OperatorIdentityLoader.FromEnvironment();

            Assert.Equal("operator", identity.Username);
            Assert.Equal("otc_operator_dev_password_change_me", identity.Password);
            Assert.Equal("Order-To-Cash Operator", identity.DisplayName);
            Assert.Equal(["operator"], identity.Roles);
        }
        finally
        {
            ClearAll();
        }
    }

    [Fact]
    public void FromEnvironment_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues()
    {
        ClearAll();
        Environment.SetEnvironmentVariable("GATEWAY_OPERATOR_USERNAME", "custom-operator");
        Environment.SetEnvironmentVariable("GATEWAY_OPERATOR_PASSWORD", "custom-password");
        Environment.SetEnvironmentVariable("GATEWAY_OPERATOR_DISPLAY_NAME", "Custom Operator Display Name");
        try
        {
            var identity = OperatorIdentityLoader.FromEnvironment();

            Assert.Equal("custom-operator", identity.Username);
            Assert.Equal("custom-password", identity.Password);
            Assert.Equal("Custom Operator Display Name", identity.DisplayName);
        }
        finally
        {
            ClearAll();
        }
    }
}
