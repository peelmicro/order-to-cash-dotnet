// COPY OF — src/Orders/Application/Ports/IClock.cs
namespace OrderToCash.Billing.Application.Ports;

/// <summary>
/// The clock port — design.md §8.1: <c>created_at</c> (the outbox writer),
/// <c>published_at</c> (the relay) and the aggregate's own <c>CreditContext.OccurredAt</c>
/// are its consumers.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
