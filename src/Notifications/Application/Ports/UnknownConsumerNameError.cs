// COPY OF — src/Orders/Application/Ports/UnknownConsumerNameError.cs
using OrderToCash.SharedKernel;

namespace OrderToCash.Notifications.Application.Ports;

/// <summary>
/// Raised by <see cref="ConsumerNames.Parse"/> when a token is outside the
/// closed set of dedup-ledger consumer names. Lives beside
/// <see cref="ConsumerName"/> in <c>Application/Ports/</c> — this service
/// owns no <c>Domain/</c> vocabulary at all (domain-model.md §6: "owns no
/// aggregate and no invariant"), so there is no aggregate-owned errors
/// folder for it to compete with.
/// </summary>
public sealed class UnknownConsumerNameError : DomainError
{
    public UnknownConsumerNameError(string token)
        : base("consumer_name.unknown", $"'{token}' is not a recognised consumer name.")
    {
    }
}
