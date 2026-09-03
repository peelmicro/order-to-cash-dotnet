namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The clock port <c>orders_aggregate</c>'s design.md §7.3 said would live
/// in the application layer — it lands here, in feature
/// <c>outbox_and_idempotency</c>, because <c>created_at</c> (the outbox
/// writer), <c>published_at</c> (the relay) and <c>processed_at</c> (the
/// idempotent consumer) are its first users. Ordering correction, not a
/// design change: the port, its layer and its signature are unchanged
/// (design.md §4.6).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
