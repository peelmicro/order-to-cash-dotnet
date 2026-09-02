using OrderToCash.Cqrs;

namespace OrderToCash.Cqrs.UnitTests.Fixtures;

/// <summary>
/// Scaffolding used only by <c>DispatcherValidationTests</c>, closed over a
/// throwaway marker type per scenario so each test gets its own concrete
/// command/query type without ever appearing in a whole-assembly scan.
/// </summary>
/// <remarks>
/// Left as OPEN generics deliberately.
/// <see cref="System.Reflection.Assembly.GetTypes"/> only ever returns
/// types actually <em>declared</em> in an assembly — for a generic type,
/// that is its open definition (<c>ProbeCommand&lt;TMarker&gt;</c>), never a
/// closed instantiation built elsewhere via
/// <see cref="Type.MakeGenericType"/>. <c>DispatcherServiceCollectionExtensions</c>
/// filters out <c>IsGenericTypeDefinition</c> types before scanning for
/// real command/query/handler types (no command DTO in this repository is
/// ever generic), so the open forms below are inert to
/// <c>DispatcherTests</c>'s whole-assembly
/// <c>AddDispatcher(typeof(PingCommand).Assembly)</c> call — they simply
/// never show up as candidates. Each validation test closes them itself,
/// via <c>MakeGenericType</c>, over a private marker type nobody else
/// touches, and hands the resulting closed types straight to the internal
/// <c>AddDispatcherFromTypes</c> seam — the only way to reach a scenario
/// with zero or two handlers without those handler types permanently
/// breaking the well-formed fixtures' whole-assembly scan.
/// </remarks>
public sealed record ProbeCommand<TMarker> : ICommand;

public sealed class ProbeCommandHandlerA<TMarker> : ICommandHandler<ProbeCommand<TMarker>>
{
    public Task HandleAsync(ProbeCommand<TMarker> command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ProbeCommandHandlerB<TMarker> : ICommandHandler<ProbeCommand<TMarker>>
{
    public Task HandleAsync(ProbeCommand<TMarker> command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ProbeQuery<TMarker> : IQuery<int>
{
}

/// <summary>
/// D6, progress/review_cqrs_dispatcher.md: implements BOTH command markers,
/// which <see cref="ICommand"/>'s own remarks say a command must never do.
/// Open generic for the same reason as the other probes above — closed per
/// test, never appears in a whole-assembly scan.
/// </summary>
public sealed record AmbiguousProbeCommand<TMarker> : ICommand, ICommand<int>;

public sealed class ProbeQueryHandlerA<TMarker> : IQueryHandler<ProbeQuery<TMarker>, int>
{
    public Task<int> HandleAsync(ProbeQuery<TMarker> query, CancellationToken cancellationToken) => Task.FromResult(0);
}

public sealed class ProbeQueryHandlerB<TMarker> : IQueryHandler<ProbeQuery<TMarker>, int>
{
    public Task<int> HandleAsync(ProbeQuery<TMarker> query, CancellationToken cancellationToken) => Task.FromResult(0);
}

// One throwaway marker type per validation scenario, so each MakeGenericType
// closure in DispatcherValidationTests is independent of the others.
public sealed class ZeroHandlerCommandMarker
{
}

public sealed class DuplicateHandlerCommandMarker
{
}

public sealed class ZeroHandlerQueryMarker
{
}

public sealed class DuplicateHandlerQueryMarker
{
}

public sealed class AmbiguousCommandMarker
{
}
