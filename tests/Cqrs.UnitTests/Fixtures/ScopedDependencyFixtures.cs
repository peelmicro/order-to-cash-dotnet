using OrderToCash.Cqrs;

namespace OrderToCash.Cqrs.UnitTests.Fixtures;

/// <summary>
/// Stands in for a scoped infrastructure dependency (an EF Core
/// <c>DbContext</c>, in every real service from feature 15 onward). One
/// instance per DI scope, each with its own <see cref="InstanceId"/> — the
/// only thing <c>DispatcherScopeTests</c> needs to tell "the scope's own
/// instance" from "a captured instance from a different scope".
/// </summary>
public sealed class ScopedDependency
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

public sealed record ScopedProbeCommand : ICommand<Guid>
{
}

/// <summary>
/// Reports which <see cref="ScopedDependency"/> instance it was constructed
/// with, so a caller dispatching from two different DI scopes can tell
/// whether it reached two different instances (correct — resolved from the
/// caller's scope) or the same one twice (the captive-dependency defect —
/// resolved from the root provider regardless of scope).
/// </summary>
public sealed class ScopedProbeCommandHandler : ICommandHandler<ScopedProbeCommand, Guid>
{
    private readonly ScopedDependency _dependency;

    public ScopedProbeCommandHandler(ScopedDependency dependency)
    {
        _dependency = dependency;
    }

    public Task<Guid> HandleAsync(ScopedProbeCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(_dependency.InstanceId);
}
