using OrderToCash.Notifications.Application.Templates;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

public sealed class NotificationFormatTests
{
    [Theory]
    [InlineData(124250, "USD", "1242.50 USD")]
    [InlineData(0, "EUR", "0.00 EUR")]
    [InlineData(5, "EUR", "0.05 EUR")]
    [InlineData(-124250, "USD", "-1242.50 USD")]
    [InlineData(100, "USD", "1.00 USD")]
    public void FormatMoney_RendersIntegerMinorUnitsAsAMajorUnitDisplayString(long minorUnits, string currency, string expected) =>
        Assert.Equal(expected, NotificationFormat.FormatMoney(minorUnits, currency));

    [Fact]
    public void RecipientFor_LowercasesTheIdentifierAndAppendsTheSyntheticDomain() =>
        Assert.Equal("carrefoures@retailer.order-to-cash.example", NotificationFormat.RecipientFor("CarrefourEs"));

    [Fact]
    public void SubjectWithCorrelationId_CarriesTheSummaryAndTheCorrelationId()
    {
        var correlationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var subject = NotificationFormat.SubjectWithCorrelationId("Order ORD-000001 placed", correlationId);

        Assert.Equal("[order-to-cash] Order ORD-000001 placed (correlationId: 11111111-1111-1111-1111-111111111111)", subject);
    }

    [Theory]
    [InlineData("&", "&amp;")]
    [InlineData("<", "&lt;")]
    [InlineData(">", "&gt;")]
    [InlineData("\"", "&quot;")]
    [InlineData("'", "&#39;")]
    public void EscapeHtml_EscapesEachReservedCharacter(string input, string expected) =>
        Assert.Equal(expected, NotificationFormat.EscapeHtml(input));

    [Fact]
    public void EscapeHtml_EscapesAnXssShapedValueCompletely()
    {
        var escaped = NotificationFormat.EscapeHtml("<script>alert('x')</script>");

        Assert.DoesNotContain("<script>", escaped, StringComparison.Ordinal);
        Assert.Equal("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", escaped);
    }

    [Fact]
    public void EscapeHtml_LeavesAnOrdinaryReferenceUnchanged() =>
        Assert.Equal("ORD-000001", NotificationFormat.EscapeHtml("ORD-000001"));
}
