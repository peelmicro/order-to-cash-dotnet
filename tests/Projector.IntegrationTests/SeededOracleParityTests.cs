using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using OrderToCash.Seed.Domain.Sagas;
using OrderToCash.Seed.Infrastructure.Mongo;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR44</c> — for each of the six seeded sagas, projects that saga's OWN
/// outbox facts through the REAL writer into an empty collection and
/// compares against <c>MongoSeedWriter.ToTimelineDocument(saga)</c>'s own
/// serialised bytes, field by field and BY BSON TYPE, excluding ONLY
/// <c>PR44</c>'s enumerated set.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class SeededOracleParityTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    public static IEnumerable<object[]> AllSagas() => SagaFixtures.All.Select(saga => new object[] { saga.Sequence });

    [Theory]
    [MemberData(nameof(AllSagas))]
    public async Task PR44_ProjectingEachSeededSagasOwnFactsReproducesTheSeedsDocument_ExceptTheEnumeratedMasterDataAndVoiceFields_IncludingBsonTypes(int sequence)
    {
        var saga = SagaFixtures.All.Single(s => s.Sequence == sequence);

        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, $"oracle-{sequence}");

        var allFacts = saga.OrdersOutbox.Concat(saga.FulfillmentOutbox).Concat(saga.BillingOutbox)
            .OrderBy(f => f.OccurredAt)
            .ToList();

        Assert.NotEmpty(allFacts);

        foreach (var fact in allFacts)
        {
            var envelope = new FactEnvelope(
                fact.EventId,
                fact.EventType,
                fact.CorrelationId,
                fact.CausationId,
                new DateTimeOffset(DateTime.SpecifyKind(fact.OccurredAt, DateTimeKind.Utc)),
                fact.Payload);

            await runtime.ApplyAsync(envelope);
        }

        var projected = await runtime.Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", saga.OrderId.ToString("D"))).FirstAsync();
        var expected = MongoSeedWriter.ToTimelineDocument(saga).ToBsonDocument();

        // Top level — every field except the two master-data ones.
        foreach (var name in expected.Names)
        {
            if (name is "retailer" or "company")
            {
                CompareParty(name, expected[name].AsBsonDocument, projected[name].AsBsonDocument);
                continue;
            }

            if (name == "events")
            {
                continue; // compared separately below.
            }

            if (name == "items")
            {
                CompareItems(expected["items"].AsBsonArray, projected["items"].AsBsonArray);
                continue;
            }

            AssertFieldEqual(name, expected[name], projected[name]);
        }

        Assert.Equal(expected["events"].AsBsonArray.Count, projected["events"].AsBsonArray.Count);
        for (var i = 0; i < expected["events"].AsBsonArray.Count; i++)
        {
            var expectedEntry = expected["events"].AsBsonArray[i].AsBsonDocument;
            var projectedEntry = projected["events"].AsBsonArray[i].AsBsonDocument;

            AssertFieldEqual($"events[{i}].eventId", expectedEntry["eventId"], projectedEntry["eventId"]);
            AssertFieldEqual($"events[{i}].eventType", expectedEntry["eventType"], projectedEntry["eventType"]);
            AssertFieldEqual($"events[{i}].occurredAt", expectedEntry["occurredAt"], projectedEntry["occurredAt"]);
            AssertFieldEqual($"events[{i}].causationId", expectedEntry["causationId"], projectedEntry["causationId"]);

            // "detail" is excluded entirely (PR44) — the seed's fixtures
            // carry a hand-written subset; PR16's builders are detail's oracle.
            // "summary" is excluded ONLY for credit.approved.v1/credit.rejected.v1
            // (PR44 — #7's projector groups thousands, its seed did not).
            var eventType = expectedEntry["eventType"].AsString;
            if (eventType is not ("credit.approved.v1" or "credit.rejected.v1"))
            {
                AssertFieldEqual($"events[{i}].summary", expectedEntry["summary"], projectedEntry["summary"]);
            }
        }
    }

    private static void CompareParty(string name, BsonDocument expected, BsonDocument actual)
    {
        AssertFieldEqual($"{name}.code", expected["code"], actual["code"]);
        AssertFieldEqual($"{name}.gln", expected["gln"], actual["gln"]);
        // "name" excluded (PR44 — master data, on no fact).
    }

    private static void CompareItems(BsonArray expected, BsonArray actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var expectedItem = expected[i].AsBsonDocument;
            var actualItem = actual[i].AsBsonDocument;

            AssertFieldEqual($"items[{i}].productCode", expectedItem["productCode"], actualItem["productCode"]);
            AssertFieldEqual($"items[{i}].quantity", expectedItem["quantity"], actualItem["quantity"]);
            AssertFieldEqual($"items[{i}].unitPrice", expectedItem["unitPrice"], actualItem["unitPrice"]);
            AssertFieldEqual($"items[{i}].lineDiscount", expectedItem["lineDiscount"], actualItem["lineDiscount"]);
            // "name" excluded (PR44 — master data, on no fact).
        }
    }

    private static void AssertFieldEqual(string path, BsonValue expected, BsonValue actual)
    {
        Assert.True(expected.BsonType == actual.BsonType, $"{path}: expected BSON type {expected.BsonType}, was {actual.BsonType}");
        Assert.True(expected.Equals(actual), $"{path}: expected {expected}, was {actual}");
    }
}
