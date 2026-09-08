// COPY OF — src/Orders/Application/Ports/ConsumerName.cs
namespace OrderToCash.Projector.Application.Ports;

/// <summary>
/// The closed set of dedup-ledger consumer names — <c>orders.saga</c>,
/// <c>projector</c>, <c>notifications</c> — fixed by
/// <c>specs/shared/requirements.md</c>'s Vocabulary section (outbox_and_idempotency
/// design.md §6.1). A typo cannot create a second, silently-empty dedup
/// namespace: the enum is the domain vocabulary, <see cref="ConsumerNames"/>
/// carries the wire token and the parse boundary, following the
/// <c>OrderStatuses</c>/<c>CancellationReasons</c> convention this repository
/// already uses. This service writes <see cref="ConsumerName.Projector"/>
/// rows, but the enum keeps the full closed set (matching Orders' own copy)
/// so <c>ConsumerNames.Parse</c> stays total over every value the shared
/// dedup key namespace can legally hold, not just this service's own.
/// </summary>
public enum ConsumerName
{
    OrdersSaga,
    Projector,
    Notifications,
}

/// <summary>Maps <see cref="ConsumerName"/> to and from its wire token — the dedup key's <c>&lt;consumer&gt;:&lt;eventId&gt;</c> prefix.</summary>
public static class ConsumerNames
{
    public static string ToToken(ConsumerName consumer) => consumer switch
    {
        ConsumerName.OrdersSaga => "orders.saga",
        ConsumerName.Projector => "projector",
        ConsumerName.Notifications => "notifications",
        _ => throw new ArgumentOutOfRangeException(nameof(consumer), consumer, "Unrecognised ConsumerName member."),
    };

    /// <summary>Parses a stored or wire-received consumer token, raising <see cref="UnknownConsumerNameError"/> on anything outside the closed set.</summary>
    public static ConsumerName Parse(string? token) => token switch
    {
        "orders.saga" => ConsumerName.OrdersSaga,
        "projector" => ConsumerName.Projector,
        "notifications" => ConsumerName.Notifications,
        _ => throw new UnknownConsumerNameError(token ?? "<null>"),
    };
}
