namespace OrderToCash.SharedKernel;

/// <summary>
/// Marker for a fact an <see cref="AggregateRoot"/> has appended to its
/// pending-events list. The envelope that turns a raised event into a
/// wire-shaped fact (specs/shared/domain-model.md §7.1) is a Contracts
/// concern; the shared kernel only needs to know that something was raised.
/// </summary>
public interface IDomainEvent;
