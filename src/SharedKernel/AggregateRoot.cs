namespace OrderToCash.SharedKernel;

/// <summary>
/// Base type for an aggregate root: an <see cref="Entity"/> that additionally
/// collects the domain events it has raised since it was loaded or created,
/// so that an application-layer handler can turn them into outbox records
/// after the aggregate's own invariants have accepted the change (CLAUDE.md,
/// coding conventions: "AggregateRoot ... collects domain events").
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(UniqueId id)
        : base(id)
    {
    }

    /// <summary>Every domain event raised and not yet cleared, in the order it was raised.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Appends a domain event to the pending list. Called by the aggregate itself once an invariant accepts a change.</summary>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Empties the pending list — called once the events have been durably recorded (the outbox).</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
