using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Notifications.Application;
using OrderToCash.Notifications.Application.Commands;
using OrderToCash.Notifications.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// NS1-NS7 — one guard per notified fact's command handler. Each asserts the
/// SENT MESSAGE carries that fact's OWN distinctive subject text, not merely
/// "a send happened" — so a copy-paste of the wrong template builder into
/// the wrong handler fails this suite too, not only a deleted dispatch call
/// (CLAUDE.md's "both mutation families" rule).
/// </summary>
public sealed class NotifyFactCommandHandlersTests
{
    private static readonly Guid _correlationId = Guid.NewGuid();

    private static NotificationDispatchService NewService(FakeNotificationSender sender) =>
        new(new FakeNotificationIdempotency(), sender, NullLogger<NotificationDispatchService>.Instance);

    [Fact]
    public async Task NS1_NotifyOrderPlacedCommandHandler_SendsTheOrderPlacedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyOrderPlacedCommandHandler(NewService(sender));
        var envelope = new Envelope<OrderPlacedPayload>(
            Guid.NewGuid(), "order.placed.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderPlacedPayload("ORD-000001", "CarrefourEs", "COMP01", "1", "2", "USD", DateTimeOffset.UtcNow, [], 100, 0, 100));

        await handler.HandleAsync(new NotifyOrderPlacedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Order ORD-000001 placed", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS2_NotifyOrderConfirmedCommandHandler_SendsTheOrderConfirmedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyOrderConfirmedCommandHandler(NewService(sender));
        var envelope = new Envelope<OrderConfirmedPayload>(
            Guid.NewGuid(), "order.confirmed.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderConfirmedPayload("ORD-000001", "CarrefourEs", "COMP01", "USD", 100, DateTimeOffset.UtcNow));

        await handler.HandleAsync(new NotifyOrderConfirmedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Order ORD-000001 confirmed", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS3_NotifyOrderDespatchedCommandHandler_SendsTheOrderDespatchedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyOrderDespatchedCommandHandler(NewService(sender));
        var envelope = new Envelope<OrderDespatchedPayload>(
            Guid.NewGuid(), "order.despatched.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderDespatchedPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, "COMP01", "CarrefourEs", [new DespatchLine("SKU-1", 1)]));

        await handler.HandleAsync(new NotifyOrderDespatchedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Order ORD-000001 despatched (DES-000001)", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS4_NotifyInvoiceIssuedCommandHandler_SendsTheInvoiceIssuedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyInvoiceIssuedCommandHandler(NewService(sender));
        var envelope = new Envelope<InvoiceIssuedPayload>(
            Guid.NewGuid(), "invoice.issued.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new InvoiceIssuedPayload("ORD-000001", "INV-000001", DateTimeOffset.UtcNow, "CarrefourEs", "COMP01", "USD", [], 100, 0, 100));

        await handler.HandleAsync(new NotifyInvoiceIssuedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Invoice INV-000001 issued", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS5_NotifyPaymentReceivedCommandHandler_SendsThePaymentReceivedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyPaymentReceivedCommandHandler(NewService(sender));
        var envelope = new Envelope<PaymentReceivedPayload>(
            Guid.NewGuid(), "payment.received.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new PaymentReceivedPayload("ORD-000001", "INV-000001", "CR-000001", "USD", 100, DateTimeOffset.UtcNow, "bank_transfer"));

        await handler.HandleAsync(new NotifyPaymentReceivedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Payment received for invoice INV-000001", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS6_NotifyOrderCompletedCommandHandler_SendsTheOrderCompletedTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyOrderCompletedCommandHandler(NewService(sender));
        var envelope = new Envelope<OrderCompletedPayload>(
            Guid.NewGuid(), "order.completed.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderCompletedPayload("ORD-000001", "CarrefourEs", "COMP01", "USD", 100, DateTimeOffset.UtcNow));

        await handler.HandleAsync(new NotifyOrderCompletedCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Order ORD-000001 completed", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NS7_NotifyOrderCancelledCommandHandler_SendsTheOrderCancelledTemplate()
    {
        var sender = new FakeNotificationSender();
        var handler = new NotifyOrderCancelledCommandHandler(NewService(sender));
        var envelope = new Envelope<OrderCancelledPayload>(
            Guid.NewGuid(), "order.cancelled.v1", Guid.NewGuid(), _correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderCancelledPayload("ORD-000001", "CarrefourEs", "COMP01", "operator_cancelled", DateTimeOffset.UtcNow, []));

        await handler.HandleAsync(new NotifyOrderCancelledCommand(envelope), CancellationToken.None);

        Assert.Single(sender.SentMessages);
        Assert.Contains("Order ORD-000001 cancelled", sender.SentMessages[0].Subject, StringComparison.Ordinal);
    }
}
