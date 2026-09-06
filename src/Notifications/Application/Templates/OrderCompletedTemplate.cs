using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>order.completed.v1</c> (domain-model.md §7.2, row 12) — NS6.</summary>
public static class OrderCompletedTemplate
{
    public static NotificationMessage Build(Envelope<OrderCompletedPayload> envelope)
    {
        var payload = envelope.Payload;
        var total = FormatMoney(payload.TotalAmount, payload.Currency);

        var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} completed", envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Order {payload.OrderReference} is complete — despatched, invoiced and paid.",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Total: {total}",
            $"Completed at: {payload.CompletedAt:O}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> is complete — despatched, invoiced and paid.</p>",
            "<ul>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Total: {EscapeHtml(total)}</li>",
            $"<li>Completed at: {EscapeHtml(payload.CompletedAt.ToString("O"))}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
