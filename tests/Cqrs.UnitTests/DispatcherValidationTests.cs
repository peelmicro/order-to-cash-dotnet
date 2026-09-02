using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Cqrs;
using OrderToCash.Cqrs.UnitTests.Fixtures;
using Xunit;

namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// The startup validation pass — the point of this feature (CLAUDE.md: "a
/// dispatcher that resolves lazily and throws on the first command is
/// strictly worse than one that refuses to start"). Each test hands
/// <c>AddDispatcherFromTypes</c> (the internal scanning core behind the
/// public, assembly-scanning <c>AddDispatcher</c> — see its remarks) a
/// small, hand-picked type universe built by closing the open-generic
/// probes in <c>Fixtures/ValidationProbes.cs</c> over a private marker
/// type, so each scenario is fully isolated from every other test in this
/// project.
/// </summary>
public sealed class DispatcherValidationTests
{
    [Fact]
    public void AddDispatcher_CommandWithZeroHandlers_ThrowsDispatcherValidationException()
    {
        var commandType = typeof(ProbeCommand<>).MakeGenericType(typeof(ZeroHandlerCommandMarker));
        var services = new ServiceCollection();

        var exception = Assert.Throws<DispatcherValidationException>(
            () => services.AddDispatcherFromTypes([commandType]));

        Assert.Contains("No command handler is registered", exception.Message, StringComparison.Ordinal);
        Assert.Contains(commandType.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDispatcher_CommandWithTwoHandlers_ThrowsDispatcherValidationException()
    {
        var commandType = typeof(ProbeCommand<>).MakeGenericType(typeof(DuplicateHandlerCommandMarker));
        var handlerA = typeof(ProbeCommandHandlerA<>).MakeGenericType(typeof(DuplicateHandlerCommandMarker));
        var handlerB = typeof(ProbeCommandHandlerB<>).MakeGenericType(typeof(DuplicateHandlerCommandMarker));
        var services = new ServiceCollection();

        var exception = Assert.Throws<DispatcherValidationException>(
            () => services.AddDispatcherFromTypes([commandType, handlerA, handlerB]));

        Assert.Contains("2 command handlers are registered", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The decision recorded for a query with two handlers: it fails
    /// validation exactly like a command does, rather than being tolerated
    /// the way an event's multiplicity is. See the remarks on
    /// <c>DispatcherRegistrationValidator</c> for the full reasoning — in
    /// short, a query answers synchronously with one <c>TResult</c>, so two
    /// candidate handlers have no principled way to be reconciled the way
    /// "publish to everyone" reconciles two event handlers, and picking the
    /// first one the container happens to resolve would be exactly the kind
    /// of DI failure CLAUDE.md says must be loud at boot rather than
    /// discovered from an inconsistent answer at runtime.
    /// </summary>
    [Fact]
    public void AddDispatcher_QueryWithTwoHandlers_ThrowsDispatcherValidationException()
    {
        var queryType = typeof(ProbeQuery<>).MakeGenericType(typeof(DuplicateHandlerQueryMarker));
        var handlerA = typeof(ProbeQueryHandlerA<>).MakeGenericType(typeof(DuplicateHandlerQueryMarker));
        var handlerB = typeof(ProbeQueryHandlerB<>).MakeGenericType(typeof(DuplicateHandlerQueryMarker));
        var services = new ServiceCollection();

        var exception = Assert.Throws<DispatcherValidationException>(
            () => services.AddDispatcherFromTypes([queryType, handlerA, handlerB]));

        Assert.Contains("2 query handlers are registered", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same decision: a query with zero handlers
    /// fails exactly like a command with zero handlers, for the same reason
    /// — a read that can never be served is a boot-time defect, not a fact
    /// awaiting a future listener.
    /// </summary>
    [Fact]
    public void AddDispatcher_QueryWithZeroHandlers_ThrowsDispatcherValidationException()
    {
        var queryType = typeof(ProbeQuery<>).MakeGenericType(typeof(ZeroHandlerQueryMarker));
        var services = new ServiceCollection();

        var exception = Assert.Throws<DispatcherValidationException>(
            () => services.AddDispatcherFromTypes([queryType]));

        Assert.Contains("No query handler is registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDispatcher_EventWithZeroHandlers_DoesNotThrow()
    {
        // No marker interface constrains IEventHandler<T> (see its
        // remarks) — validation has no universe of "every event type" to
        // enumerate, so a fact type with no listener anywhere in the
        // candidate set is simply never considered. Passing only the fact
        // record, with no IEventHandler<UnlistenedFact> implementation in
        // the candidate list, proves registration itself never throws for
        // this case (DispatcherTests.PublishAsync_WithZeroRegisteredHandlers_CompletesWithoutError
        // proves the matching dispatch-time behaviour).
        var services = new ServiceCollection();

        services.AddDispatcherFromTypes([typeof(UnlistenedFact)]);
    }

    /// <summary>
    /// D6, progress/review_cqrs_dispatcher.md: <see cref="ICommand"/>'s own
    /// remarks say a command implements either it or
    /// <see cref="ICommand{TResult}"/>, "never both" — this is that "never"
    /// enforced. Before the fix, a type implementing both silently required
    /// TWO handlers (one per shape) rather than being rejected outright.
    /// </summary>
    [Fact]
    public void AddDispatcher_CommandImplementingBothCommandMarkers_ThrowsDispatcherValidationException()
    {
        var commandType = typeof(AmbiguousProbeCommand<>).MakeGenericType(typeof(AmbiguousCommandMarker));
        var services = new ServiceCollection();

        var exception = Assert.Throws<DispatcherValidationException>(
            () => services.AddDispatcherFromTypes([commandType]));

        Assert.Contains("implements both", exception.Message, StringComparison.Ordinal);
        Assert.Contains(commandType.ToString(), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// D5, progress/review_cqrs_dispatcher.md: a second AddDispatcher call
    /// on the same IServiceCollection would otherwise validate a second,
    /// disjoint type universe (a command declared in the first call's
    /// assemblies with its handler only in the second call's assemblies
    /// would be wrongly reported as having zero handlers) and register a
    /// second IDispatcher. The fix makes the second call itself the error.
    /// </summary>
    [Fact]
    public void AddDispatcher_CalledTwiceOnTheSameServiceCollection_Throws()
    {
        var services = new ServiceCollection();
        services.AddDispatcher(typeof(PingCommand).Assembly);

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddDispatcher(typeof(PingCommand).Assembly));

        Assert.Contains("already called", exception.Message, StringComparison.Ordinal);
    }
}
