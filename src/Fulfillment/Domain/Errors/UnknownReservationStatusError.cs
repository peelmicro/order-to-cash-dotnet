using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="ReservationStatuses.Parse"/> when a stored or
/// wire-received token is outside the closed three-value set — a load-time
/// fault (a typo in the persistence column, which is free-text
/// <c>nvarchar(20)</c>), not a business rejection.
/// </summary>
public sealed class UnknownReservationStatusError(string? token)
    : DomainError("UNKNOWN_RESERVATION_STATUS", $"'{token ?? "<null>"}' is not a recognised ReservationStatus token.")
{
    public string? Token { get; } = token;
}
