using OrderToCash.Cqrs;

namespace OrderToCash.Cqrs.UnitTests.Fixtures;

// ── One well-formed command/query/event pair per handler shape ──
//
// Dispatched-through by DispatcherTests, all registered together via a
// single, whole-assembly AddDispatcher(typeof(PingCommand).Assembly) call.
// None of these types participate in DispatcherValidationTests — that
// class's probe types (Fixtures/ValidationProbes.cs) are open generics that
// Assembly.GetTypes() never returns as a closed instantiation, so they
// cannot contaminate this whole-assembly scan, and this file's types never
// leave a command or query without exactly one handler, so the whole-
// assembly scan itself never fails validation.

public sealed record PingCommand(string Text) : ICommand;

public sealed class PingCommandHandler : ICommandHandler<PingCommand>
{
    public static (string Text, CancellationToken Token)? LastReceived { get; set; }

    public Task HandleAsync(PingCommand command, CancellationToken cancellationToken)
    {
        LastReceived = (command.Text, cancellationToken);
        return Task.CompletedTask;
    }
}

public sealed record WidgetCreated(Guid Id, string Name);

public sealed record CreateWidgetCommand(string Name) : ICommand<WidgetCreated>;

public sealed class CreateWidgetCommandHandler : ICommandHandler<CreateWidgetCommand, WidgetCreated>
{
    public static CancellationToken? LastToken { get; set; }

    public Task<WidgetCreated> HandleAsync(CreateWidgetCommand command, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Task.FromResult(new WidgetCreated(Guid.NewGuid(), command.Name));
    }
}

public sealed record GetWidgetCountQuery : IQuery<int>
{
}

public sealed class GetWidgetCountQueryHandler : IQueryHandler<GetWidgetCountQuery, int>
{
    public static CancellationToken? LastToken { get; set; }

    public Task<int> HandleAsync(GetWidgetCountQuery query, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Task.FromResult(42);
    }
}

public sealed record WidgetCreatedFact(Guid Id);

public sealed class FirstWidgetCreatedFactHandler : IEventHandler<WidgetCreatedFact>
{
    public static Guid? LastReceived { get; set; }

    public Task HandleAsync(WidgetCreatedFact @event, CancellationToken cancellationToken)
    {
        LastReceived = @event.Id;
        return Task.CompletedTask;
    }
}

public sealed class SecondWidgetCreatedFactHandler : IEventHandler<WidgetCreatedFact>
{
    public static Guid? LastReceived { get; set; }

    public Task HandleAsync(WidgetCreatedFact @event, CancellationToken cancellationToken)
    {
        LastReceived = @event.Id;
        return Task.CompletedTask;
    }
}

/// <summary>
/// An upstream-shaped fact: published in real call sites through a base or
/// interface-typed variable — exactly how feature 14's outbox drain and
/// feature 15's aggregate drain iterate a mixed collection of domain events
/// (<c>IReadOnlyList&lt;IDomainEvent&gt;</c>) and publish each one. The
/// STATIC type at the call site is <see cref="IUpstreamFact"/>; the RUNTIME
/// type is <see cref="ConcreteUpstreamFact"/> — <c>PublishAsync</c> must
/// resolve handlers by the latter, not the former (progress/review_cqrs_dispatcher.md, D3).
/// </summary>
public interface IUpstreamFact
{
}

public sealed record ConcreteUpstreamFact(Guid Id) : IUpstreamFact;

public sealed class ConcreteUpstreamFactHandler : IEventHandler<ConcreteUpstreamFact>
{
    public static Guid? LastReceived { get; set; }

    // D8, progress/review_cqrs_dispatcher.md: Dispatcher.PublishAsync
    // reaches this handler through MethodInfo.Invoke(handler, [@event,
    // cancellationToken]), not a direct, compile-time-checked call — CA2016
    // cannot see through that Invoke, so nothing analyzer-enforced keeps
    // the token in argument position 1 from silently being dropped or
    // swapped. LastToken is what DispatcherTests.PublishAsync_ForwardsTheCancellationTokenToTheHandler
    // asserts against.
    public static CancellationToken? LastToken { get; set; }

    public Task HandleAsync(ConcreteUpstreamFact @event, CancellationToken cancellationToken)
    {
        LastReceived = @event.Id;
        LastToken = cancellationToken;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A fact type with zero <see cref="IEventHandler{TEvent}"/> implementations
/// anywhere in this assembly — deliberately never handled. Proves the
/// command/query-versus-event asymmetry: publishing a fact nobody listens
/// for yet is not an error.
/// </summary>
public sealed record UnlistenedFact(Guid Id);
