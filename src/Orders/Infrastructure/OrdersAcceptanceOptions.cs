using OrderToCash.Orders.Infrastructure.Messaging;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>Configuration <see cref="OrdersAcceptanceServiceCollectionExtensions.AddOrdersAcceptance"/> binds — this feature's own ports, distinct from <see cref="OrdersOutboxOptions"/> (feature <c>outbox_and_idempotency</c>'s).</summary>
public sealed class OrdersAcceptanceOptions
{
    public NatsOptions Nats { get; set; } = new();
}
