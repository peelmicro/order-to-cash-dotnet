using OrderToCash.Gateway.Domain.Projection;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

public sealed class OrderReadModelMapperTests
{
    private static OrderReadModelDocument CompleteDocument() => new(
        OrderId: Guid.Parse("9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f"),
        OrderReference: "ORD-000042",
        OrderDate: new DateTimeOffset(2026, 8, 18, 10, 15, 0, TimeSpan.Zero),
        Retailer: new OrderReadModelParty("CarrefourEs", "Carrefour Spain", "8412345000013"),
        Company: new OrderReadModelParty("IBERFOODS", "Iberfoods SA", "8412345000020"),
        Status: "confirmed",
        CancellationReason: null,
        Currency: "EUR",
        // Fix round, review defect D5 — distinct, non-zero values on all
        // three fields, so a transposition of InitialAmount/InitialDiscount
        // (probe P3) cannot survive on a fixture whose InitialDiscount was
        // previously 0 and never separately asserted.
        Totals: new OrderReadModelTotals(124950, 700, 124250),
        Items: [new OrderReadModelItem("PRD-0001", "Widget", 5, 24850, 0)],
        References: new OrderReadModelReferences(null, null, null),
        Events: [new OrderReadModelEvent(Guid.NewGuid(), "order.placed.v1", DateTimeOffset.UtcNow, "Order placed", null, null)],
        HeaderComplete: true,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void ToOrderSummary_ReturnsARow_ForAFullyProjectedDocument()
    {
        var summary = OrderReadModelMapper.ToOrderSummary(CompleteDocument());

        Assert.NotNull(summary);
        Assert.Equal("ORD-000042", summary!.OrderReference);
        Assert.Equal("CarrefourEs", summary.Retailer.Code);
        // D5 — all three fields on the wire's OrderTotals, not just
        // TotalAmount: a transposition of InitialAmount/InitialDiscount is
        // otherwise invisible on a fixture whose InitialDiscount is 0.
        Assert.Equal(124950, summary.Totals.InitialAmount);
        Assert.Equal(700, summary.Totals.InitialDiscount);
        Assert.Equal(124250, summary.Totals.TotalAmount);
    }

    /// <summary>R53 — a placeholder document (no orderReference yet, headerComplete: false) is excluded from the list rather than emitted half-filled.</summary>
    [Fact]
    public void ToOrderSummary_ReturnsNull_ForAPlaceholderDocumentWithNoOrderReferenceYet()
    {
        var placeholder = CompleteDocument() with { OrderReference = null, HeaderComplete = false };

        Assert.Null(OrderReadModelMapper.ToOrderSummary(placeholder));
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void ToOrderSummary_ReturnsNull_WhenAnyRequiredFieldIsMissing(
        bool missingOrderReference, bool missingOrderDate, bool missingCurrency, bool missingRetailerGln, bool missingTotalAmount)
    {
        var doc = CompleteDocument();
        if (missingOrderReference)
        {
            doc = doc with { OrderReference = null };
        }

        if (missingOrderDate)
        {
            doc = doc with { OrderDate = null };
        }

        if (missingCurrency)
        {
            doc = doc with { Currency = null };
        }

        if (missingRetailerGln)
        {
            doc = doc with { Retailer = doc.Retailer with { Gln = null } };
        }

        if (missingTotalAmount)
        {
            doc = doc with { Totals = doc.Totals with { TotalAmount = null } };
        }

        Assert.Null(OrderReadModelMapper.ToOrderSummary(doc));
    }

    [Fact]
    public void ToOrderDetail_AlwaysReturnsADocument_PlaceholderOrNot()
    {
        var placeholder = CompleteDocument() with
        {
            OrderReference = null,
            HeaderComplete = false,
            Retailer = new OrderReadModelParty(null, null, null),
            Totals = new OrderReadModelTotals(null, null, null),
        };

        var detail = OrderReadModelMapper.ToOrderDetail(placeholder);

        Assert.Equal(placeholder.OrderId, detail.OrderId);
        Assert.False(detail.HeaderComplete);
        Assert.Null(detail.Retailer);
        Assert.Null(detail.Totals);
        Assert.Single(detail.Events);
    }

    [Fact]
    public void ToOrderDetail_PassesTheTimelineThroughUnmodified_IncludingCausationId()
    {
        var causeId = Guid.NewGuid();
        var effectId = Guid.NewGuid();
        var doc = CompleteDocument() with
        {
            Events =
            [
                new OrderReadModelEvent(causeId, "credit.rejected.v1", DateTimeOffset.UtcNow, "Credit rejected", null, null),
                new OrderReadModelEvent(effectId, "stock.released.v1", DateTimeOffset.UtcNow, "Stock released", null, causeId),
            ],
        };

        var detail = OrderReadModelMapper.ToOrderDetail(doc);

        Assert.Equal(2, detail.Events.Count);
        Assert.Null(detail.Events[0].CausationId);
        Assert.Equal(causeId, detail.Events[1].CausationId);
    }
}
