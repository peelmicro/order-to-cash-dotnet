using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// NS5 — <c>payment.received.v1</c>. The one template whose recipient is
/// synthesised from <c>orderReference</c> rather than <c>retailerCode</c>
/// (the payload carries no <c>retailerCode</c> at all — domain-model.md
/// §7.2 row 11), and whose <c>paymentReference</c> is the one field on the
/// externally-supplied remittance path (billing_remittance_intake).
/// </summary>
public sealed class PaymentReceivedTemplateTests
{
    private static readonly Guid _correlationId = Guid.Parse("22222222-2222-2222-2222-222222222226");

    /// <summary>R2-D6 / id 59 — bracketed away from envelope.OccurredAt (2026-09-01T13:00:00Z in <see cref="BuildEnvelope"/>).</summary>
    private static readonly DateTimeOffset _valueDate = DateTimeOffset.Parse("2026-08-29T17:40:15Z");

    [Fact]
    public void Build_ProducesASubjectCarryingTheInvoiceReferenceAndDerivesTheRecipientFromTheOrderReference()
    {
        var envelope = BuildEnvelope("CR-000001");

        var message = PaymentReceivedTemplate.Build(envelope);

        Assert.Equal($"[order-to-cash] Payment received for invoice INV-000001 (correlationId: {_correlationId})", message.Subject);
        // Recipient derived from orderReference (ORD-000001), NOT from any retailerCode — the payload has none.
        Assert.Equal("ord-000001@retailer.order-to-cash.example", message.To);
        Assert.Contains("for invoice INV-000001 (order ORD-000001)", message.Text, StringComparison.Ordinal);
        Assert.Contains("Source: bank_transfer", message.Text, StringComparison.Ordinal);
        Assert.Contains($"Value date: {_valueDate:O}", message.Text, StringComparison.Ordinal);
        Assert.Contains("for invoice INV-000001 (order ORD-000001)", message.Html, StringComparison.Ordinal);
        Assert.Contains("Source: bank_transfer", message.Html, StringComparison.Ordinal);
        Assert.Contains("124.25 USD", message.Html, StringComparison.Ordinal);
        Assert.Contains($"Value date: {_valueDate:O}", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EscapesAMaliciousPaymentReferenceInHtmlButLeavesItVerbatimInText()
    {
        const string maliciousReference = "<script>alert('x')</script>";
        var envelope = BuildEnvelope(maliciousReference);

        var message = PaymentReceivedTemplate.Build(envelope);

        Assert.DoesNotContain("<script>", message.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", message.Html, StringComparison.Ordinal);
        Assert.Contains(maliciousReference, message.Text, StringComparison.Ordinal);
    }

    private static Envelope<PaymentReceivedPayload> BuildEnvelope(string paymentReference) => new(
        Guid.NewGuid(),
        "payment.received.v1",
        Guid.NewGuid(),
        _correlationId,
        Guid.NewGuid(),
        DateTimeOffset.Parse("2026-09-01T13:00:00Z"),
        new PaymentReceivedPayload("ORD-000001", "INV-000001", paymentReference, "USD", 12425, _valueDate, "bank_transfer"));
}
