namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The closed set of dedup-ledger consumer names — <c>orders.saga</c>,
/// <c>projector</c>, <c>notifications</c> — fixed by
/// <c>specs/shared/requirements.md</c>'s Vocabulary section (design.md
/// §6.1). A typo cannot create a second, silently-empty dedup namespace: the
/// enum is the domain vocabulary, <see cref="ConsumerNames"/> carries the
/// wire token and the parse boundary, following the
/// <c>OrderStatuses</c>/<c>CancellationReasons</c> convention this repository
/// already uses.
/// </summary>
public enum ConsumerName
{
    OrdersSaga,
    Projector,
    Notifications,
}

/// <summary>Maps <see cref="ConsumerName"/> to and from its wire token — the <c>processed_events.consumer</c> column value.</summary>
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
