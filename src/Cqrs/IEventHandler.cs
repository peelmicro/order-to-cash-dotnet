namespace OrderToCash.Cqrs;

/// <summary>
/// Handles a fact of type <typeparamref name="TEvent"/>.
/// </summary>
/// <remarks>
/// Deliberately unconstrained — there is no <c>IEvent</c> marker the way
/// <see cref="ICommand"/> and <see cref="IQuery{TResult}"/> constrain their
/// handler interfaces. A command or a query with zero handlers is a defect
/// (nothing can ever execute it, or nothing can ever answer it), so the
/// startup validation pass needs a closed universe of command/query types to
/// check registrations against. A fact may legitimately have zero listeners
/// — #7's <c>EventBus</c> behaves this way, and this repository's own
/// events are published before every consumer exists (a saga step, a
/// notification template, a projector) — so there is nothing to validate
/// and therefore nothing to enumerate. Adding an <c>IEvent</c> marker here
/// would invite the same "must have exactly one handler" check the command
/// and query sides need, which is precisely the asymmetry this type
/// exists to preserve.
/// </remarks>
public interface IEventHandler<in TEvent>
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
