using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Errors;

/// <summary>
/// Raised by <see cref="Reservation.Release"/> / <see cref="Reservation.Consume"/>
/// when the reservation is already <c>released</c> or <c>consumed</c> —
/// <b>F4</b>, `R35`: both terminal states have no outbound edge. Carries the
/// attempted transition so a caller can log it without re-deriving it.
/// </summary>
public sealed class ReservationTerminalError(ReservationStatus from, string attemptedTransition)
    : DomainError("RESERVATION_TERMINAL", $"Reservation is already '{ReservationStatuses.ToToken(from)}'; cannot {attemptedTransition} a terminal reservation.")
{
    public ReservationStatus From { get; } = from;

    public string AttemptedTransition { get; } = attemptedTransition;
}
