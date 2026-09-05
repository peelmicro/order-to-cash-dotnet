using OrderToCash.Fulfillment.Domain.Errors;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>
/// The three states of a <see cref="Reservation"/> — <c>domain-model.md</c>
/// §4.2's lifecycle table. The enum is the domain vocabulary;
/// <see cref="ReservationStatuses"/> carries the snake_case wire/storage
/// tokens, the same split <c>OrderStatus</c>/<c>OrderStatuses</c> already
/// establishes.
/// </summary>
public enum ReservationStatus
{
    Reserved,
    Released,
    Consumed,
}

/// <summary>
/// Maps <see cref="ReservationStatus"/> to and from the snake_case tokens the
/// <c>status</c> column stores — an explicit table, never a case transform of
/// the C# member name. <see cref="Parse"/> refuses any token outside the
/// closed set: the persistence column is <c>nvarchar(20)</c> free text, and a
/// typo must be a loud parse failure, not a silently unmatched status
/// (design.md §3.2).
/// </summary>
public static class ReservationStatuses
{
    public static string ToToken(ReservationStatus status) => status switch
    {
        ReservationStatus.Reserved => "reserved",
        ReservationStatus.Released => "released",
        ReservationStatus.Consumed => "consumed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unrecognised ReservationStatus member."),
    };

    public static ReservationStatus Parse(string? token) => token switch
    {
        "reserved" => ReservationStatus.Reserved,
        "released" => ReservationStatus.Released,
        "consumed" => ReservationStatus.Consumed,
        _ => throw new UnknownReservationStatusError(token),
    };
}
