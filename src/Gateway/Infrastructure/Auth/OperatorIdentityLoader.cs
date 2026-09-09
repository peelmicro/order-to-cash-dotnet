using OrderToCash.Gateway.Domain.Auth;

namespace OrderToCash.Gateway.Infrastructure.Auth;

/// <summary>
/// Loads the single operator identity from the environment
/// (<c>GATEWAY_OPERATOR_USERNAME</c>/<c>GATEWAY_OPERATOR_PASSWORD</c> —
/// already declared in <c>.env.example</c> and <c>docker-compose.infra.yml</c>
/// ahead of this feature). Fallbacks are byte-identical to
/// <c>.env.example</c>'s own committed values, matching every other
/// <c>*Options.FromEnvironment()</c> loader in this repository
/// (e.g. <c>ProjectorMongoOptions.FromEnvironment</c>).
/// </summary>
public static class OperatorIdentityLoader
{
    public static OperatorIdentity FromEnvironment() => new(
        Username: Environment.GetEnvironmentVariable("GATEWAY_OPERATOR_USERNAME") ?? "operator",
        Password: Environment.GetEnvironmentVariable("GATEWAY_OPERATOR_PASSWORD") ?? "otc_operator_dev_password_change_me",
        DisplayName: Environment.GetEnvironmentVariable("GATEWAY_OPERATOR_DISPLAY_NAME") ?? "Order-To-Cash Operator",
        Roles: ["operator"]);
}
