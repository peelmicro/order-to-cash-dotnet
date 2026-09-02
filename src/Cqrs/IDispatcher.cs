namespace OrderToCash.Cqrs;

/// <summary>
/// Resolves the one handler registered for a command or query, and every
/// handler registered for a fact, from the DI container. The only entry
/// point application-layer callers (a Minimal API endpoint, a NATS
/// responder, a saga step, a Kafka consumer) use to reach a handler —
/// callers never resolve <see cref="ICommandHandler{TCommand}"/> or its
/// siblings directly.
/// </summary>
public interface IDispatcher
{
    /// <summary>Dispatches a command that produces no result.</summary>
    Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand;

    /// <summary>Dispatches a command that produces a <typeparamref name="TResult"/>.</summary>
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>;

    /// <summary>Dispatches a query and returns its <typeparamref name="TResult"/>.</summary>
    Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>;

    /// <summary>
    /// Publishes a fact to every registered <see cref="IEventHandler{TEvent}"/>,
    /// in registration order. Zero registered handlers is not an error — see
    /// <see cref="IEventHandler{TEvent}"/>'s remarks.
    /// </summary>
    /// <remarks>
    /// Takes <see cref="object"/>, not a generic <c>TEvent</c>, and resolves
    /// handlers by <c>@event.GetType()</c> at the point of the call —
    /// deliberately, not merely as an additional overload. A generic
    /// <c>PublishAsync&lt;TEvent&gt;</c> binds <c>TEvent</c> to the STATIC
    /// type of the argument at compile time, so publishing a fact through a
    /// base- or interface-typed variable (exactly how an outbox drain or an
    /// aggregate's <c>IReadOnlyList&lt;IDomainEvent&gt;</c> is iterated and
    /// published one element at a time) would silently resolve zero
    /// handlers for that base/interface type and complete having called
    /// nothing — a lost fact, not a thrown error, because zero handlers is
    /// deliberately not an error for a fact. Keeping the generic method
    /// alongside this one would not have closed that gap: given an argument
    /// whose static type is the base/interface, overload resolution prefers
    /// the generic method's exact match over an implicit reference
    /// conversion to <see cref="object"/>, so the risky call sites would
    /// still silently pick the wrong one. Removing the generic overload
    /// entirely — this method is the only <c>PublishAsync</c> — makes the
    /// mistake structurally unavailable rather than merely avoidable.
    /// See progress/impl_cqrs_dispatcher.md, defect D3, for the cost this
    /// trades for: resolving <c>IEventHandler&lt;&gt;</c> closed over a
    /// reflected <see cref="Type"/> and invoking <c>HandleAsync</c> via
    /// <see cref="System.Reflection.MethodInfo.Invoke(object?,object?[]?)"/>
    /// rather than a direct, generically-typed call.
    /// </remarks>
    Task PublishAsync(object @event, CancellationToken cancellationToken);
}
