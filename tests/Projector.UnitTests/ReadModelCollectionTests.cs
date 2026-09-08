using System.Reflection;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary><c>C1</c> — every element-name constant equals the seed's real <c>[BsonElement]</c> name, discovered by reflection over the class map, never a hand-written expected list. Arm by renaming one constant.</summary>
public sealed class ReadModelCollectionTests
{
    [Fact]
    public void CollectionNameEqualsMongoSeedWritersCollectionName() =>
        Assert.Equal(MongoSeedWriter.CollectionName, ReadModelCollection.Name);

    [Fact]
    public void TopLevelElementNamesEqualOrderTimelineDocumentsRealBsonClassMap()
    {
        var classMap = BsonClassMap.LookupClassMap(typeof(OrderTimelineDocument));
        var expectedByProperty = classMap.AllMemberMaps.ToDictionary(m => m.MemberName, m => m.ElementName);

        Assert.Equal(ReadModelCollection.Fields.OrderId, expectedByProperty[nameof(OrderTimelineDocument.OrderId)]);
        Assert.Equal(ReadModelCollection.Fields.OrderReference, expectedByProperty[nameof(OrderTimelineDocument.OrderReference)]);
        Assert.Equal(ReadModelCollection.Fields.OrderDate, expectedByProperty[nameof(OrderTimelineDocument.OrderDate)]);
        Assert.Equal(ReadModelCollection.Fields.Retailer, expectedByProperty[nameof(OrderTimelineDocument.Retailer)]);
        Assert.Equal(ReadModelCollection.Fields.Company, expectedByProperty[nameof(OrderTimelineDocument.Company)]);
        Assert.Equal(ReadModelCollection.Fields.Status, expectedByProperty[nameof(OrderTimelineDocument.Status)]);
        Assert.Equal(ReadModelCollection.Fields.CancellationReason, expectedByProperty[nameof(OrderTimelineDocument.CancellationReason)]);
        Assert.Equal(ReadModelCollection.Fields.Currency, expectedByProperty[nameof(OrderTimelineDocument.Currency)]);
        Assert.Equal(ReadModelCollection.Fields.Totals, expectedByProperty[nameof(OrderTimelineDocument.Totals)]);
        Assert.Equal(ReadModelCollection.Fields.Items, expectedByProperty[nameof(OrderTimelineDocument.Items)]);
        Assert.Equal(ReadModelCollection.Fields.References, expectedByProperty[nameof(OrderTimelineDocument.References)]);
        Assert.Equal(ReadModelCollection.Fields.Events, expectedByProperty[nameof(OrderTimelineDocument.Events)]);
        Assert.Equal(ReadModelCollection.Fields.HeaderComplete, expectedByProperty[nameof(OrderTimelineDocument.HeaderComplete)]);
        Assert.Equal(ReadModelCollection.Fields.UpdatedAt, expectedByProperty[nameof(OrderTimelineDocument.UpdatedAt)]);
        Assert.Equal(ReadModelCollection.Fields.StatusRank, expectedByProperty[nameof(OrderTimelineDocument.StatusRank)]);
        Assert.Equal(ReadModelCollection.Fields.TimelineOrderVersion, expectedByProperty[nameof(OrderTimelineDocument.TimelineOrderVersion)]);
        Assert.Equal(ReadModelCollection.Fields.ProcessedEventKeys, expectedByProperty[nameof(OrderTimelineDocument.ProcessedEventKeys)]);

        // _id is a BsonId, not a named [BsonElement] — asserted against the driver's own convention.
        Assert.Equal("_id", ReadModelCollection.Fields.Id);
    }

    [Fact]
    public void NestedElementNamesEqualTheSeedsRealBsonClassMaps()
    {
        AssertElementName<PartySnapshot>(nameof(PartySnapshot.Code), ReadModelCollection.Fields.PartySnapshot.Code);
        AssertElementName<PartySnapshot>(nameof(PartySnapshot.Name), ReadModelCollection.Fields.PartySnapshot.Name);
        AssertElementName<PartySnapshot>(nameof(PartySnapshot.Gln), ReadModelCollection.Fields.PartySnapshot.Gln);

        AssertElementName<Totals>(nameof(Totals.InitialAmount), ReadModelCollection.Fields.TotalsFields.InitialAmount);
        AssertElementName<Totals>(nameof(Totals.InitialDiscount), ReadModelCollection.Fields.TotalsFields.InitialDiscount);
        AssertElementName<Totals>(nameof(Totals.TotalAmount), ReadModelCollection.Fields.TotalsFields.TotalAmount);

        AssertElementName<TimelineItem>(nameof(TimelineItem.ProductCode), ReadModelCollection.Fields.Item.ProductCode);
        AssertElementName<TimelineItem>(nameof(TimelineItem.Name), ReadModelCollection.Fields.Item.Name);
        AssertElementName<TimelineItem>(nameof(TimelineItem.Quantity), ReadModelCollection.Fields.Item.Quantity);
        AssertElementName<TimelineItem>(nameof(TimelineItem.UnitPrice), ReadModelCollection.Fields.Item.UnitPrice);
        AssertElementName<TimelineItem>(nameof(TimelineItem.LineDiscount), ReadModelCollection.Fields.Item.LineDiscount);

        AssertElementName<References>(nameof(References.DespatchReference), ReadModelCollection.Fields.ReferencesFields.DespatchReference);
        AssertElementName<References>(nameof(References.InvoiceReference), ReadModelCollection.Fields.ReferencesFields.InvoiceReference);
        AssertElementName<References>(nameof(References.PaymentReference), ReadModelCollection.Fields.ReferencesFields.PaymentReference);

        AssertElementName<TimelineEvent>(nameof(TimelineEvent.EventId), ReadModelCollection.Fields.Event.EventId);
        AssertElementName<TimelineEvent>(nameof(TimelineEvent.EventType), ReadModelCollection.Fields.Event.EventType);
        AssertElementName<TimelineEvent>(nameof(TimelineEvent.OccurredAt), ReadModelCollection.Fields.Event.OccurredAt);
        AssertElementName<TimelineEvent>(nameof(TimelineEvent.Summary), ReadModelCollection.Fields.Event.Summary);
        AssertElementName<TimelineEvent>(nameof(TimelineEvent.Detail), ReadModelCollection.Fields.Event.Detail);
        AssertElementName<TimelineEvent>(nameof(TimelineEvent.CausationId), ReadModelCollection.Fields.Event.CausationId);
    }

    private static void AssertElementName<T>(string memberName, string expected)
    {
        var classMap = BsonClassMap.LookupClassMap(typeof(T));
        var memberMap = classMap.AllMemberMaps.Single(m => m.MemberName == memberName);
        Assert.Equal(memberMap.ElementName, expected);
    }
}
