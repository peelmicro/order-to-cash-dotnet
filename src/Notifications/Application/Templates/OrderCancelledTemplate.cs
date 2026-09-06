using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Ports;
using static OrderToCash.Notifications.Application.Templates.NotificationFormat;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>Builds the notification for <c>order.cancelled.v1</c> (domain-model.md §7.2, row 13) — NS7.</summary>
public static class OrderCancelledTemplate
{
    public static NotificationMessage Build(Envelope<OrderCancelledPayload> envelope)
    {
        var payload = envelope.Payload;
        var compensationSummary = payload.CompensationSteps.Count == 0
            ? "none"
            : string.Join(", ", payload.CompensationSteps.Select(step => step.Step));

        var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} cancelled", envelope.CorrelationId);

        var text = string.Join(
            '\n',
            $"Order {payload.OrderReference} has been cancelled.",
            $"Reason: {payload.CancellationReason}",
            $"Retailer: {payload.RetailerCode}",
            $"Company: {payload.CompanyCode}",
            $"Cancelled at: {payload.CancelledAt:O}",
            $"Compensation steps: {compensationSummary}",
            $"Correlation id: {envelope.CorrelationId}");

        var html = string.Join(
            '\n',
            $"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been cancelled.</p>",
            "<ul>",
            $"<li>Reason: {EscapeHtml(payload.CancellationReason)}</li>",
            $"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",
            $"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",
            $"<li>Cancelled at: {EscapeHtml(payload.CancelledAt.ToString("O"))}</li>",
            $"<li>Compensation steps: {EscapeHtml(compensationSummary)}</li>",
            "</ul>",
            $"<p>Correlation id: {EscapeHtml(envelope.CorrelationId.ToString())}</p>");

        return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);
    }
}
