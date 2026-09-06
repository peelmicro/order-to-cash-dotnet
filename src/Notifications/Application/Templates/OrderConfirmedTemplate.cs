using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>order.confirmed.v1</c> (domain-model.md §7.2, row 8) — NS2.</summary>
public static class OrderConfirmedTemplate
{
    public static NotificationMessage Build(Envelope<OrderConfirmedPayload> envelope)
    {
        var payload = envelope.Payload;
        var total = FormatMoney(payload.TotalAmount, payload.Currency);

        var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} confirmed", envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Order {payload.OrderReference} has been confirmed — stock reserved and credit approved.",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Total: {total}",
            $"Confirmed at: {payload.ConfirmedAt:O}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been confirmed — stock reserved and credit approved.</p>",
            "<ul>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Total: {EscapeHtml(total)}</li>",
            $"<li>Confirmed at: {EscapeHtml(payload.ConfirmedAt.ToString("O"))}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
