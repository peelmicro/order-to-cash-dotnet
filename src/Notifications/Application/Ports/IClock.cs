// COPY OF — src/Orders/Application/Ports/IClock.cs
namespace OrderToCash.Notifications.Application.Ports;

/// <summary>The clock port — <c>processed_at</c>/<c>created_at</c> (the idempotent consumer's dedup insert) is its one user here.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
