using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>order.despatched.v1</c> (domain-model.md §7.2, row 9) — NS3.</summary>
public static class OrderDespatchedTemplate
{
    public static NotificationMessage Build(Envelope<OrderDespatchedPayload> envelope)
    {
        var payload = envelope.Payload;
        var lineSummary = string.Join(", ", payload.Lines.Select(line => $"{line.ProductCode} x{line.Units}"));

        var subject = SubjectWithCorrelationId(
            $"Order {payload.OrderReference} despatched ({payload.DespatchReference})",
            envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Order {payload.OrderReference} has been despatched.",
            $"Despatch reference: {payload.DespatchReference}",
            $"Despatch date: {payload.DespatchDate:O}",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Lines: {lineSummary}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been despatched.</p>",
            "<ul>",
            $"<li>Despatch reference: {EscapeHtml(payload.DespatchReference)}</li>",
            $"<li>Despatch date: {EscapeHtml(payload.DespatchDate.ToString("O"))}</li>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Lines: {EscapeHtml(lineSummary)}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
