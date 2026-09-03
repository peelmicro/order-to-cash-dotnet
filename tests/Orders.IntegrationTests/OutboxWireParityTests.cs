using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using Xunit;
using DomainOrder = OrderToCash.Orders.Domain.Order;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// OI15 and R11 at the producer — design.md §5.5. Phase 5 proved the
/// SERIALISER against twelve real #7 envelopes; this proves the PRODUCER:
/// aggregate -&gt; payload -&gt; outbox row -&gt; relay -&gt; real broker.
/// </summary>
[Collection(KafkaCollection.Name)]
public sealed class OutboxWireParityTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    private const string GoldenBuyerGln = "5400000000034";
    private const string GoldenSupplierGln = "5400000000386";
    private const string GoldenCurrency = "EUR";
    private const string GoldenRetailerCode = "LeroyMerlinEs";
    private const string GoldenCompanyCode = "PORTOTOOLS";
    private const string GoldenProductCode = "PRD-0008";

    /// <summary>
    /// OI15, first case — asserts the relay's PASS-THROUGH: it neither
    /// reordered nor reformatted what was stored. This is NOT a claim that
    /// #8 independently reproduces MySQL's key ordering — the golden
    /// file's payload text is inserted VERBATIM and republished unchanged
    /// (design.md §5.5, CLAUDE.md's JSON-wire rule).
    /// </summary>
    [Fact]
    public async Task OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7()
    {
        var goldenPath = RepositoryPaths.Find(Path.Combine("tests", "Contracts.UnitTests", "GoldenEnvelopes", "order_placed_v1.json"));
        var goldenBytes = await File.ReadAllBytesAsync(goldenPath);
        using var golden = JsonDocument.Parse(goldenBytes);
        var envelope = golden.RootElement;

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi15a_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = envelope.GetProperty("eventId").GetGuid(),
            EventType = envelope.GetProperty("eventType").GetString()!,
            AggregateId = envelope.GetProperty("aggregateId").GetGuid(),
            CorrelationId = envelope.GetProperty("correlationId").GetGuid(),
            CausationId = envelope.GetProperty("causationId").GetGuid(),
            // Verbatim — the exact source bytes of the payload object, not a
            // re-serialisation of a parsed-and-rebuilt value.
            Payload = envelope.GetProperty("payload").GetRawText(),
            OccurredAt = envelope.GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime,
            CreatedAt = DateTime.UtcNow,
        };
        db.OutboxMessages.Add(row);
        await db.SaveChangesAsync();

        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        using var publisher = new KafkaFactPublisher(producer);
        var relay = new OutboxRelay(db, publisher, new FakeClock(FakeClock.UtcNowToTheMillisecond()), Options.Create(new OutboxRelayOptions { BatchSize = 10 }), NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, result.Published);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"oi15a-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(OrdersFactTopic.Name);

        ConsumeResult<string, byte[]>? consumed = null;
        for (var attempt = 0; attempt < 200 && consumed is null; attempt++)
        {
            var candidate = consumer.Consume(TimeSpan.FromSeconds(15));
            Assert.NotNull(candidate);
            if (candidate!.Message.Key == row.CorrelationId.ToString())
            {
                consumed = candidate;
            }
        }

        Assert.NotNull(consumed);
        Assert.Equal(goldenBytes, consumed!.Message.Value);
    }

    /// <summary>OI15, second case — same keys, values, types, casing; key order asserted nowhere (design.md §5.5's "MySQL's own json-column normalisation" note).</summary>
    [Fact]
    public async Task OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs()
    {
        var goldenPath = RepositoryPaths.Find(Path.Combine("tests", "Contracts.UnitTests", "GoldenEnvelopes", "order_placed_v1.json"));
        using var golden = JsonDocument.Parse(await File.ReadAllBytesAsync(goldenPath));
        var goldenPayload = golden.RootElement.GetProperty("payload");

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi15b_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        await SeedGoldenReferenceDataAsync(db);

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = DomainOrder.Place(
            orderReference: OrderNumber.Parse("ORD-000011"),
            orderDate: golden.RootElement.GetProperty("payload").GetProperty("orderDate").GetDateTimeOffset(),
            retailerCode: GoldenRetailerCode,
            buyerGln: new GLN(GoldenBuyerGln),
            companyCode: GoldenCompanyCode,
            supplierGln: new GLN(GoldenSupplierGln),
            currency: GoldenCurrency,
            lines: [new OrderLineRequest(GoldenProductCode, "5L concentrated liquid laundry detergent", new Quantity(6), new Money(1489, GoldenCurrency), Money.Zero(GoldenCurrency))],
            notes: null,
            occurredAt: clock.UtcNow,
            causationId: UniqueId.New());

        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
        var unitOfWork = new EfCoreUnitOfWork(db);
        await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);

        var storedPayload = await db.OutboxMessages.Select(o => o.Payload).SingleAsync();
        using var storedDocument = JsonDocument.Parse(storedPayload);

        AssertSemanticallyEqual(goldenPayload, storedDocument.RootElement);
    }

    /// <summary>R11 at the producer, for a fact the aggregate REALLY placed — not transcribed from a golden file.</summary>
    [Fact]
    public async Task R11_PublishedEnvelope_CarriesTheSevenFieldsInTheDeclaredOrderWithNoneAbsentNullOrEmpty()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_r11wire_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        await OrderPersistenceTestSupport.SeedReferenceDataAsync(db);

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var causationId = UniqueId.New();
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(7), clock.UtcNow, causationId);

        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
        var unitOfWork = new EfCoreUnitOfWork(db);
        await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);

        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        using var publisher = new KafkaFactPublisher(producer);
        var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), NullLogger<OutboxRelay>.Instance);
        await relay.RunOnceAsync(CancellationToken.None);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"r11wire-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(OrdersFactTopic.Name);

        ConsumeResult<string, byte[]>? consumed = null;
        for (var attempt = 0; attempt < 200 && consumed is null; attempt++)
        {
            var candidate = consumer.Consume(TimeSpan.FromSeconds(15));
            Assert.NotNull(candidate);
            if (candidate!.Message.Key == order.Id.Value.ToString())
            {
                consumed = candidate;
            }
        }

        Assert.NotNull(consumed);
        using var document = JsonDocument.Parse(consumed!.Message.Value);
        var envelope = document.RootElement;

        var fieldOrder = envelope.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["eventId", "eventType", "aggregateId", "correlationId", "causationId", "occurredAt", "payload"], fieldOrder);

        Assert.NotEqual(Guid.Empty, envelope.GetProperty("eventId").GetGuid());
        Assert.Matches(@"^[a-z]+\.[a-z_]+\.v[0-9]+$", envelope.GetProperty("eventType").GetString());
        Assert.Equal(order.Id.Value, envelope.GetProperty("correlationId").GetGuid());
        Assert.Equal(causationId.Value, envelope.GetProperty("causationId").GetGuid());
        Assert.NotEqual(default, envelope.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Object, envelope.GetProperty("payload").ValueKind);

        // I4: message headers — x-event-type mirrors eventType,
        // content-type: application/json, and NO traceparent (feature 27's
        // gap, a recorded fact rather than an oversight).
        var headers = consumed.Message.Headers;
        Assert.Equal(envelope.GetProperty("eventType").GetString(), System.Text.Encoding.UTF8.GetString(headers.GetLastBytes("x-event-type")));
        Assert.Equal("application/json", System.Text.Encoding.UTF8.GetString(headers.GetLastBytes("content-type")));
        Assert.False(headers.TryGetLastBytes("traceparent", out _));
    }

    private static async Task SeedGoldenReferenceDataAsync(OrdersDbContext db)
    {
        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();

        db.Currencies.Add(new Currency { Id = currencyId, Code = GoldenCurrency, IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });
        db.Retailers.Add(new Retailer { Id = Guid.NewGuid(), Code = GoldenRetailerCode, Name = "Leroy Merlin ES", Country = "ES", Vat = "ES00000000000", Gln = GoldenBuyerGln, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Companies.Add(new Company { Id = Guid.NewGuid(), Code = GoldenCompanyCode, Name = "Porto Tools", Country = "PT", Vat = "PT000000000", Gln = GoldenSupplierGln, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = GoldenProductCode, Ean = "1000000000383", Name = "Laundry detergent", Description = "5L concentrated liquid laundry detergent", Price = 1489, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// design.md §5.5 says to reuse <c>tests/Contracts.UnitTests/JsonEquivalence.cs</c>
    /// rather than write a second one. That class is <c>internal</c> to
    /// its own project, and adding an <c>InternalsVisibleTo</c> or making
    /// it <c>public</c> would mean editing a file outside this feature's
    /// scope (<c>Contracts.UnitTests</c> is not named by
    /// <c>tasks.md</c>). This is therefore a deliberate, minimal DUPLICATE
    /// of the same comparison — same rule (kind equality, object keys
    /// unordered, array elements ordered, numbers compared by raw text or
    /// value) — following the identical precedent already set by
    /// <c>RepositoryPaths</c> being copied per test project rather than
    /// shared.
    /// </summary>
    private static void AssertSemanticallyEqual(JsonElement expected, JsonElement actual, string path = "$")
    {
        Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: kind mismatch — expected {expected.ValueKind}, found {actual.ValueKind}");

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProps = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                Assert.True(
                    expectedProps.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(actualProps.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                    $"{path}: key set differs — expected {{{string.Join(",", expectedProps.Keys)}}}, found {{{string.Join(",", actualProps.Keys)}}}");
                foreach (var (key, value) in expectedProps)
                {
                    AssertSemanticallyEqual(value, actualProps[key], $"{path}.{key}");
                }

                break;

            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToArray();
                var actualItems = actual.EnumerateArray().ToArray();
                Assert.Equal(expectedItems.Length, actualItems.Length);
                for (var i = 0; i < expectedItems.Length; i++)
                {
                    AssertSemanticallyEqual(expectedItems[i], actualItems[i], $"{path}[{i}]");
                }

                break;

            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;

            case JsonValueKind.Number:
                Assert.True(expected.GetRawText() == actual.GetRawText() || expected.GetDecimal() == actual.GetDecimal(), $"{path}: expected {expected.GetRawText()}, found {actual.GetRawText()}");
                break;
        }
    }
}
