using OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// <c>PR12</c>'s fourteen-row table — every fact's payload CLR type mapped
/// to the order status it implies (or none) and that status's rank, so
/// <c>R52</c>'s "precedes" is decidable by a single <c>$max</c>/<c>$gt</c>
/// pair inside one atomic write, never a state-machine walk. Keyed by
/// <see cref="Type"/>, not by an <c>eventType</c> string literal — a second
/// eventType-keyed table here would be exactly the two-list shape
/// <c>PR36</c>/<c>L46</c> forbid (Notifications' review round-1 D1: a filter
/// beside a switch, where deleting an entry silently stopped a fact being
/// handled). The nine status-bearing pairs equal <c>MongoSeedWriter</c>'s
/// own private table verbatim (<c>PR40</c>).
/// </summary>
public static class OrderStatusRank
{
    /// <summary>Internal sentinel distinguishing "this type is in the table and implies no status" from "this type is not in the table at all" (an unmapped type throws rather than defaulting).</summary>
    private const string StatusLess = "\0__status_less__";

    private static readonly IReadOnlyDictionary<Type, (string Status, int Rank)> _table =
        new Dictionary<Type, (string, int)>
        {
            [typeof(OrderPlacedPayload)] = ("placed", 1),
            [typeof(StockReservedPayload)] = ("stock_reserved", 2),
            [typeof(CreditApprovedPayload)] = ("credit_approved", 3),
            [typeof(OrderConfirmedPayload)] = ("confirmed", 4),
            [typeof(OrderDespatchedPayload)] = ("despatched", 5),
            [typeof(InvoiceIssuedPayload)] = ("invoiced", 6),
            [typeof(PaymentReceivedPayload)] = ("paid", 7),
            [typeof(OrderCompletedPayload)] = ("completed", 98),
            [typeof(OrderCancelledPayload)] = ("cancelled", 99),

            // The five status-less facts — present so the table is total
            // over all fourteen payload types and a missing arm is a defect,
            // never an implicit default.
            [typeof(StockRejectedPayload)] = (StatusLess, 0),
            [typeof(StockReleasedPayload)] = (StatusLess, 0),
            [typeof(CreditRejectedPayload)] = (StatusLess, 0),
            [typeof(CreditReleasedPayload)] = (StatusLess, 0),
            [typeof(OrderSagaFailedPayload)] = (StatusLess, 0),
        };

    public static int Count => _table.Count;

    /// <summary>Every payload type the table carries an arm for — the fourteen-row completeness set.</summary>
    public static IReadOnlyCollection<Type> PayloadTypes => (IReadOnlyCollection<Type>)_table.Keys;

    /// <summary>The implied status, or <see langword="null"/> for one of the five status-less facts. Throws for a payload type the table does not carry at all.</summary>
    public static string? ImpliedStatusOf(Type payloadType)
    {
        var (status, _) = Lookup(payloadType);
        return status == StatusLess ? null : status;
    }

    /// <summary>The rank (0 for a status-less fact). Throws for a payload type the table does not carry at all.</summary>
    public static int RankOf(Type payloadType)
    {
        var (_, rank) = Lookup(payloadType);
        return rank;
    }

    private static (string Status, int Rank) Lookup(Type payloadType) =>
        _table.TryGetValue(payloadType, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(payloadType), payloadType, "No status-rank arm exists for this payload type.");
}
