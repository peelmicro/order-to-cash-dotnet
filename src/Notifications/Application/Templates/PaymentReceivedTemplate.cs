using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>payment.received.v1</c> (domain-model.md §7.2, row 11) — NS5.</summary>
public static class PaymentReceivedTemplate
{
    public static NotificationMessage Build(Envelope<PaymentReceivedPayload> envelope)
    {
        var payload = envelope.Payload;
        var amount = FormatMoney(payload.Amount, payload.Currency);

        var subject = SubjectWithCorrelationId(
            $"Payment received for invoice {payload.InvoiceReference}",
            envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Payment {payload.PaymentReference} has been received for invoice {payload.InvoiceReference} (order {payload.OrderReference}).",
            $"Amount: {amount}",
            $"Value date: {payload.ValueDate:O}",
            $"Source: {payload.Source}",
            $"Correlation id: {envelope.CorrelationId}");

        // paymentReference is the field most worth escaping here: unlike the
        // other six facts, the remittance path (billing_remittance_intake)
        // lets an EXTERNAL caller supply it — this is the one template where
        // an unescaped value is genuinely reachable from outside the system.
        var html = string.Join(
            '\n',
            $"<p>Payment <strong>{EscapeHtml(payload.PaymentReference)}</strong> has been received for invoice {EscapeHtml(payload.InvoiceReference)} (order {EscapeHtml(payload.OrderReference)}).</p>",
            "<ul>",
            $"<li>Amount: {EscapeHtml(amount)}</li>",
            $"<li>Value date: {EscapeHtml(payload.ValueDate.ToString("O"))}</li>",
            $"<li>Source: {EscapeHtml(payload.Source)}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        // PaymentReceivedPayload carries no retailerCode (NotificationFormat.RecipientFor's
        // own doc) — orderReference is the best available identifier.
        return new NotificationMessage(RecipientFor(payload.OrderReference), subject, text, html);
    }
}
