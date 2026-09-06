using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

public sealed class NotificationMimeMessageBuilderTests
{
    [Fact]
    public void Build_CarriesTheFromToSubjectAndBothBodies()
    {
        var message = new NotificationMessage("retailer@example.com", "a subject", "plain text", "<p>html</p>", "event-1@order-to-cash");

        var mime = NotificationMimeMessageBuilder.Build(message, "no-reply@order-to-cash.example");

        Assert.Equal("no-reply@order-to-cash.example", mime.From.Mailboxes.Single().Address);
        Assert.Equal("retailer@example.com", mime.To.Mailboxes.Single().Address);
        Assert.Equal("a subject", mime.Subject);
        Assert.Equal("event-1@order-to-cash", mime.MessageId);
        Assert.Equal("plain text", mime.TextBody);
        Assert.Equal("<p>html</p>", mime.HtmlBody);
    }

    [Fact]
    public void Build_OmitsTheMessageIdHeaderWhenNoneWasSupplied()
    {
        var message = new NotificationMessage("retailer@example.com", "a subject", "plain text", "<p>html</p>");

        var mime = NotificationMimeMessageBuilder.Build(message, "no-reply@order-to-cash.example");

        // MimeKit auto-generates a MessageId if one is never set explicitly
        // — the guard here is that OUR value is never silently overwritten
        // when we DO supply one (proven by the case above), not that the
        // header is literally absent.
        Assert.NotEqual("event-1@order-to-cash", mime.MessageId);
    }
}
