using OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// <c>PR16</c> — the fourteen summary/detail builders, from the envelope
/// alone: no read-model access, no write-model access, no clock. Every
/// expected string is #7's <c>apps/projector/src/domain/summaries.ts</c>
/// byte for byte (design.md §4's table). <c>detail</c> is populated only for
/// the five facts <c>PR16</c> names; every other builder returns
/// <see langword="null"/> for it.
/// </summary>
public static class Summaries
{
    public static SummaryResult OrderPlaced(OrderPlacedPayload payload) =>
        new($"Order {payload.OrderReference} placed for {payload.RetailerCode}", null);

    public static SummaryResult StockReserved(StockReservedPayload payload) =>
        new($"Stock reserved for {payload.Reservations.Count} line(s)", null);

    public static SummaryResult StockRejected(StockRejectedPayload payload)
    {
        var shortUnits = payload.Shortages.Sum(s => s.Requested - s.Available);
        var firstProduct = payload.Shortages.Count > 0 ? payload.Shortages[0].ProductCode : string.Empty;

        return new(
            $"Stock rejected: {shortUnits} unit(s) short on {firstProduct}",
            new Dictionary<string, object>
            {
                ["shortages"] = payload.Shortages,
                ["reason"] = payload.Reason,
            });
    }

    public static SummaryResult StockReleased(StockReleasedPayload payload)
    {
        var releasedUnits = payload.Released.Sum(r => r.Units);
        var suffix = payload.Reason == "credit_rejected" ? " (compensation)" : string.Empty;

        return new(
            $"{releasedUnits} unit(s) released back to stock{suffix}",
            new Dictionary<string, object>
            {
                ["released"] = payload.Released,
                ["reason"] = payload.Reason,
            });
    }

    public static SummaryResult CreditApproved(CreditApprovedPayload payload) =>
        new($"Credit hold of {MoneyFormat.Of(payload.HeldAmount, payload.Currency)} approved", null);

    public static SummaryResult CreditRejected(CreditRejectedPayload payload) =>
        new(
            $"Credit hold of {MoneyFormat.Of(payload.RequestedAmount, payload.Currency)} rejected ({payload.Reason})",
            new Dictionary<string, object>
            {
                ["reason"] = payload.Reason,
                ["requestedAmount"] = payload.RequestedAmount,
            });

    public static SummaryResult CreditReleased(CreditReleasedPayload payload)
    {
        var because = payload.Reason == "invoice_paid" ? "invoice paid" : "order cancelled";
        return new($"Credit exposure released — {because}", null);
    }

    public static SummaryResult OrderConfirmed(OrderConfirmedPayload payload) =>
        new("Order confirmed (ORDRSP)", null);

    public static SummaryResult OrderDespatched(OrderDespatchedPayload payload) =>
        new($"Despatch {payload.DespatchReference} created", null);

    public static SummaryResult InvoiceIssued(InvoiceIssuedPayload payload) =>
        new($"Invoice {payload.InvoiceReference} issued", null);

    public static SummaryResult PaymentReceived(PaymentReceivedPayload payload) =>
        new($"Payment {payload.PaymentReference} received", null);

    public static SummaryResult OrderCompleted(OrderCompletedPayload payload) =>
        new($"Order {payload.OrderReference} completed", null);

    /// <summary>
    /// SA-2: <c>note</c> is added to <c>detail</c> only when the fact
    /// carries one — an operator-cancelled order with no note, and every
    /// saga-decided cancellation (<c>stock_rejected</c>/<c>credit_rejected</c>),
    /// never populate the key at all, matching the wire's own optionality
    /// rather than writing a JSON <c>null</c> that would still be a key.
    /// </summary>
    public static SummaryResult OrderCancelled(OrderCancelledPayload payload)
    {
        var detail = new Dictionary<string, object>
        {
            ["cancellationReason"] = payload.CancellationReason,
            ["compensationSteps"] = payload.CompensationSteps,
        };

        if (payload.Note is { } note)
        {
            detail["note"] = note;
        }

        return new($"Order {payload.OrderReference} cancelled ({payload.CancellationReason})", detail);
    }

    public static SummaryResult OrderSagaFailed(OrderSagaFailedPayload payload) =>
        new(
            $"Saga command \"{payload.Command}\" dead-lettered after {payload.Attempts} attempt(s)",
            new Dictionary<string, object>
            {
                ["command"] = payload.Command,
                ["attempts"] = payload.Attempts,
                ["lastError"] = payload.LastError,
            });
}
