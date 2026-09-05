using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`R36`'s creation half — F6 (≥1 line) and F7 (the fact's payload traces each line to a despatched product/units pair) — against the pure <see cref="DespatchAdvice"/> aggregate.</summary>
public sealed class DespatchAdviceTests
{
    [Fact]
    public void Create_CreatesTheAggregateAndEmitsExactlyOneOrderDespatchedV1_WhosePayloadTracesEachLine()
    {
        var id = UniqueId.New();
        var orderReference = new OrderNumber(1);
        var correlationId = UniqueId.New();
        var causationId = UniqueId.New();
        var eventId = UniqueId.New();
        var despatchDate = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var lines = new[] { new DespatchLineEntry("P1", new Quantity(3)), new DespatchLineEntry("P2", new Quantity(5)) };

        var advice = DespatchAdvice.Create(
            id, "DES-000001", despatchDate, orderReference, "ACME", "RETAILER1", lines, correlationId, causationId, eventId);

        Assert.Equal("DES-000001", advice.DespatchReference);
        Assert.Equal(despatchDate, advice.DespatchDate);
        Assert.Equal(orderReference, advice.OrderReference);
        Assert.Equal("ACME", advice.CompanyCode);
        Assert.Equal("RETAILER1", advice.RetailerCode);
        Assert.Equal(2, advice.Lines.Count);

        var fact = Assert.IsType<OrderDespatched>(Assert.Single(advice.DomainEvents));
        Assert.Equal(eventId, fact.EventId);
        Assert.Equal(id, fact.AggregateId);
        Assert.Equal(correlationId, fact.CorrelationId);
        Assert.Equal(causationId, fact.CausationId);
        Assert.Equal("order.despatched.v1", fact.EventType);
        Assert.Equal("DES-000001", fact.DespatchReference);
        Assert.Equal(orderReference, fact.OrderReference);
        Assert.Equal("ACME", fact.CompanyCode);
        Assert.Equal("RETAILER1", fact.RetailerCode);
        Assert.Collection(
            fact.Lines,
            l => { Assert.Equal("P1", l.ProductCode); Assert.Equal(3, l.Units); },
            l => { Assert.Equal("P2", l.ProductCode); Assert.Equal(5, l.Units); });
    }

    [Fact]
    public void Create_F6_RefusesAnEmptyLineListAndCreatesNoAggregate()
    {
        var error = Assert.Throws<EmptyDespatchLinesError>(() => DespatchAdvice.Create(
            UniqueId.New(), "DES-000001", DateTimeOffset.UtcNow, new OrderNumber(1), "ACME", "RETAILER1", [], UniqueId.New(), UniqueId.New(), UniqueId.New()));

        Assert.Equal("EMPTY_DESPATCH_LINES", error.Code);
    }
}
