// COPY OF — src/Orders/Application/Ports/IClock.cs
namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>The clock port — `created_at` (the outbox writer) and `published_at` (the relay) are its users here, exactly as design.md §4.6 fixes for Orders.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
