using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>invoice.issued.v1</c> (domain-model.md §7.2, row 10) — NS4.</summary>
public static class InvoiceIssuedTemplate
{
    public static NotificationMessage Build(Envelope<InvoiceIssuedPayload> envelope)
    {
        var payload = envelope.Payload;
        var total = FormatMoney(payload.TotalAmount, payload.Currency);

        var subject = SubjectWithCorrelationId($"Invoice {payload.InvoiceReference} issued", envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Invoice {payload.InvoiceReference} has been issued for order {payload.OrderReference}.",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Total: {total}",
            $"Invoice date: {payload.InvoiceDate:O}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Invoice <strong>{EscapeHtml(payload.InvoiceReference)}</strong> has been issued for order {EscapeHtml(payload.OrderReference)}.</p>",
            "<ul>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Total: {EscapeHtml(total)}</li>",
            $"<li>Invoice date: {EscapeHtml(payload.InvoiceDate.ToString("O"))}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
