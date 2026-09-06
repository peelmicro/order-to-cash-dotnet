using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>order.placed.v1</c> (domain-model.md §7.2, row 1) — NS1.</summary>
public static class OrderPlacedTemplate
{
    public static NotificationMessage Build(Envelope<OrderPlacedPayload> envelope)
    {
        var payload = envelope.Payload;
        var total = FormatMoney(payload.TotalAmount, payload.Currency);

        var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} placed", envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Order {payload.OrderReference} has been placed.",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Total: {total}",
            $"Order date: {payload.OrderDate:O}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been placed.</p>",
            "<ul>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Total: {EscapeHtml(total)}</li>",
            $"<li>Order date: {EscapeHtml(payload.OrderDate.ToString("O"))}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
