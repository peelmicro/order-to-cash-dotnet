namespace OrderToCash.Gateway.Domain.Auth;

/// <summary>
/// The single, statically-configured operator identity (openapi.yaml
/// <c>/auth/login</c>, domain-model.md §9: "a single operator identity is
/// assumed" — deliberately not multi-tenant). Ported from #7's
/// <c>apps/gateway/src/domain/auth/operator-credentials.ts</c>
/// (<c>OperatorIdentity</c>).
/// </summary>
public sealed record OperatorIdentity(string Username, string Password, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>A submitted username/password pair, before it is known to match anything.</summary>
public readonly record struct OperatorCredentials(string Username, string Password);

/// <summary>
/// Pure comparison of a submitted credential pair against the configured
/// identity — no framework, no I/O, no hashing library: this is a single,
/// statically-configured demo identity, not a multi-user credential store,
/// so a timing side-channel against this process's own configuration is
/// not a threat this system defends against (#7's own framing, ported
/// verbatim: <c>apps/gateway/src/domain/auth/operator-credentials.ts</c>,
/// <c>matchesOperator</c>).
/// </summary>
public static class OperatorAuthenticator
{
    public static bool Matches(OperatorCredentials submitted, OperatorIdentity configured) =>
        submitted.Username == configured.Username && submitted.Password == configured.Password;
}
