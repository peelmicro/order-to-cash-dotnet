using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// Raised by <see cref="ConsumerNames.Parse"/> when a token is outside the
/// closed set of dedup-ledger consumer names. Lives beside
/// <see cref="ConsumerName"/> in <c>Application/Ports/</c>, not in
/// <c>Domain/Errors/</c>: <see cref="ConsumerName"/> is an application-layer
/// concept introduced by this feature, not part of the <c>Order</c>
/// aggregate's own vocabulary — design.md §1 fixes that
/// <c>src/Orders/Domain/</c> gains nothing beyond
/// <c>Events/OrderDomainEvent.cs</c> declaring
/// <see cref="IDomainEventEnvelope"/>.
/// </summary>
public sealed class UnknownConsumerNameError : DomainError
{
    public UnknownConsumerNameError(string token)
        : base("consumer_name.unknown", $"'{token}' is not a recognised consumer name.")
    {
    }
}
