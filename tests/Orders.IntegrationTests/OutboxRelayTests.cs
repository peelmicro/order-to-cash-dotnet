using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>R14, OI2, OI3, OI8, OI14 — the relay's claim/publish/stamp cycle, over one relay instance (design.md §5).</summary>
[Collection(KafkaCollection.Name)]
public sealed class OutboxRelayTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task R14_Relay_StampsARecordOnlyAfterTheBrokerAcknowledgementAndRepublishesAnUnstampedRecordOnTheNextPoll()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_r14_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(db);
            await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
        }

        // First cycle, a publisher that throws: nothing is stamped, and the
        // failing call still reaches the broker's own client library (a
        // deliberately unreachable bootstrap server), so this is a real
        // publish attempt, not a stub — proving "only after acknowledgement".
        var unreachablePublisherOptions = Options.Create(new OutboxRelayOptions { BatchSize = 10, PublishTimeoutMs = 2000 });
        using (var unreachablePublisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig
        {
            BootstrapServers = "127.0.0.1:1",
            MessageTimeoutMs = 1000,
            SocketConnectionSetupTimeoutMs = 1000,
        }).Build()))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, unreachablePublisher, clock, unreachablePublisherOptions, NullLogger<OutboxRelay>.Instance);
            await relay.RunOnceAsync(CancellationToken.None);
        }

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            var row = await assertDb.OutboxMessages.SingleAsync();
            Assert.Null(row.PublishedAt);
        }

        // Second cycle, the REAL broker: the same unstamped record is found
        // and published, and stamped only now.
        var realOptions = Options.Create(new OutboxRelayOptions { BatchSize = 10, PublishTimeoutMs = 10_000 });
        using (var realPublisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build()))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, realPublisher, clock, realOptions, NullLogger<OutboxRelay>.Instance);
            var result = await relay.RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, result.Claimed);
            Assert.Equal(1, result.Published);
        }

        await using var finalDb = mssql.CreateDbContext(connectionString);
        var finalRow = await finalDb.OutboxMessages.SingleAsync();
        Assert.NotNull(finalRow.PublishedAt);

        // A real consumer really received it. The topic is shared by the
        // whole Kafka collection, so a fresh consumer group starting from
        // Earliest sees every other test's records too — filter by this
        // test's own key rather than trusting the first record consumed.
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"r14-{Guid.NewGuid():N}",
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
        Assert.Equal(order.Id.Value.ToString(), consumed!.Message.Key);
    }

    [Fact]
    public async Task OI2_Relay_PublishesTwoRecordsWrittenByOneTransactionInAppendOrderAlthoughBothCarryTheSameOccurredAt()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi2_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var occurredAt = DateTime.UtcNow;
        var first = NewRow("order.placed.v1", occurredAt);
        var second = NewRow("order.confirmed.v1", occurredAt);

        // ONE AT A TIME, each its own SaveChangesAsync: a GUID-keyed entity
        // added via AddRange (or several Add calls) inside ONE
        // SaveChangesAsync does not get `seq` assigned in Add-call order on
        // this provider — see EfCoreOrderRepository.InsertOutboxRowAsync's
        // remarks, and this is precisely the case that fix exists for.
        db.OutboxMessages.Add(first);
        await db.SaveChangesAsync();
        db.OutboxMessages.Add(second);
        await db.SaveChangesAsync();

        var publisher = new FakeFactPublisher();
        var relay = BuildRelay(db, publisher, batchSize: 10);
        var result = await relay.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, result.Claimed);
        var published = Assert.Single(publisher.Calls);
        Assert.Equal(2, published.Count);

        // Append order — first.EventId then second.EventId — although both
        // share the identical occurredAt (OI2's own point: occurredAt alone
        // cannot order these).
        Assert.Equal(
            [first.EventId, second.EventId],
            published.Select(f => ExtractEventId(f.EnvelopeJson)));
    }

    private static Guid ExtractEventId(ReadOnlyMemory<byte> envelopeJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(envelopeJson);
        return document.RootElement.GetProperty("eventId").GetGuid();
    }

    /// <summary>
    /// design.md §9.4 arming row 7 ("the claim orders by seq") needs a case
    /// where <c>seq</c> order and <c>occurred_at</c> order genuinely
    /// DISAGREE — OI2's own case above ties <c>occurredAt</c> for both
    /// rows, and SQL Server has no obligation to break a tie any
    /// particular way, so that case alone does not reliably fail when
    /// <c>ORDER BY seq</c> is swapped for <c>ORDER BY occurred_at</c>
    /// (found arming row 7 — recorded in
    /// progress/impl_outbox_and_idempotency.md). Here the earlier-seq row
    /// is given the LATER <c>occurredAt</c>, so the two orderings produce
    /// different, unambiguous answers.
    /// </summary>
    [Fact]
    public async Task OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi2b_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var now = DateTime.UtcNow;
        var lowerSeqButLaterOccurredAt = NewRow("order.placed.v1", now.AddMinutes(10));
        var higherSeqButEarlierOccurredAt = NewRow("order.confirmed.v1", now);

        db.OutboxMessages.Add(lowerSeqButLaterOccurredAt);
        await db.SaveChangesAsync();
        db.OutboxMessages.Add(higherSeqButEarlierOccurredAt);
        await db.SaveChangesAsync();

        var publisher = new FakeFactPublisher();
        var relay = BuildRelay(db, publisher, batchSize: 10);
        await relay.RunOnceAsync(CancellationToken.None);

        var published = Assert.Single(publisher.Calls);
        Assert.Equal(
            [lowerSeqButLaterOccurredAt.EventId, higherSeqButEarlierOccurredAt.EventId],
            published.Select(f => ExtractEventId(f.EnvelopeJson)));
    }

    [Fact]
    public async Task OI3_Relay_PublishesALowerSequenceRecordThatCommittedAfterAHigherSequenceRecordWasAlreadyPublished()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi3_{Guid.NewGuid():N}");
        await using var setupDb = mssql.CreateDbContext(connectionString);
        await setupDb.Database.MigrateAsync();

        // Transaction A: begin, insert (higher seq once committed second).
        await using var connectionA = mssql.CreateDbContext(connectionString);
        await using var transactionA = await connectionA.Database.BeginTransactionAsync();
        var lowerSeqRow = NewRow("order.placed.v1", DateTime.UtcNow);
        connectionA.OutboxMessages.Add(lowerSeqRow);
        await connectionA.SaveChangesAsync();

        // Transaction B: insert AND commit — this row gets a HIGHER seq
        // even though A started first, because A has not committed yet.
        await using (var connectionB = mssql.CreateDbContext(connectionString))
        {
            var higherSeqRow = NewRow("order.confirmed.v1", DateTime.UtcNow);
            connectionB.OutboxMessages.Add(higherSeqRow);
            await connectionB.SaveChangesAsync();

            var publisherB = new FakeFactPublisher();
            var relayB = BuildRelay(connectionB, publisherB, batchSize: 10);
            var resultB = await relayB.RunOnceAsync(CancellationToken.None);

            // A's row is not committed yet — READPAST/UPDLOCK means the
            // relay finds only B's (higher-seq) row.
            Assert.Equal(1, resultB.Claimed);
            Assert.Equal(1, resultB.Published);
        }

        // Now A commits.
        await transactionA.CommitAsync();

        await using var db2 = mssql.CreateDbContext(connectionString);
        var publisher2 = new FakeFactPublisher();
        var relay2 = BuildRelay(db2, publisher2, batchSize: 10);
        var result2 = await relay2.RunOnceAsync(CancellationToken.None);

        // The lower-seq record, committed late, is still found and
        // published — the predicate is "published_at IS NULL", never a
        // high-water-mark cursor (OI3).
        Assert.Equal(1, result2.Claimed);
        Assert.Equal(1, result2.Published);

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(2, await assertDb.OutboxMessages.CountAsync(r => r.PublishedAt != null));
    }

    [Fact]
    public async Task OI8_Relay_LeavesEveryRecordOfARejectedBatchUnstampedAndRepublishesTheSameRecordsOnTheNextPoll()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi8_{Guid.NewGuid():N}");
        await using var seedDb = mssql.CreateDbContext(connectionString);
        await seedDb.Database.MigrateAsync();

        var row = NewRow("order.placed.v1", DateTime.UtcNow);
        seedDb.OutboxMessages.Add(row);
        await seedDb.SaveChangesAsync();

        var rejectingPublisher = new FakeFactPublisher
        {
            OnPublish = (_, _) => throw new InvalidOperationException("the broker rejected this batch"),
        };

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var relay = BuildRelay(db, rejectingPublisher, batchSize: 10);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Claimed);
            Assert.Equal(0, result.Published);
        }

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            Assert.Null((await assertDb.OutboxMessages.SingleAsync()).PublishedAt);
        }

        // Next poll: the SAME record is found again, in the same order, and
        // this time it succeeds.
        var acceptingPublisher = new FakeFactPublisher();
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var relay = BuildRelay(db, acceptingPublisher, batchSize: 10);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Claimed);
            Assert.Equal(1, result.Published);
        }

        var republished = Assert.Single(acceptingPublisher.Calls);
        Assert.Single(republished);
    }

    [Fact]
    public async Task OI14_Relay_AbandonsAPublishThatExceedsTheTimeoutRollsTheClaimBackAndRepublishesOnTheNextPoll()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_oi14_{Guid.NewGuid():N}");
        await using var seedDb = mssql.CreateDbContext(connectionString);
        await seedDb.Database.MigrateAsync();

        var row = NewRow("order.placed.v1", DateTime.UtcNow);
        seedDb.OutboxMessages.Add(row);
        await seedDb.SaveChangesAsync();

        var neverCompletingPublisher = new FakeFactPublisher
        {
            OnPublish = async (_, ct) => await Task.Delay(Timeout.InfiniteTimeSpan, ct),
        };

        const int publishTimeoutMs = 500;
        var stopwatch = Stopwatch.StartNew();

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var relay = BuildRelay(db, neverCompletingPublisher, batchSize: 10, publishTimeoutMs: publishTimeoutMs);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Claimed);
            Assert.Equal(0, result.Published);
        }

        stopwatch.Stop();
        // Bounded roughly by PublishTimeoutMs, not by "however long
        // Task.Delay(Infinite) would otherwise run" — generous upper bound
        // for a slow CI box.
        Assert.True(stopwatch.ElapsedMilliseconds < publishTimeoutMs + 5000, $"cycle took {stopwatch.ElapsedMilliseconds}ms, expected roughly {publishTimeoutMs}ms");

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            Assert.Null((await assertDb.OutboxMessages.SingleAsync()).PublishedAt);
        }

        // The claim's UPDLOCK was released on rollback — a second relay
        // claims the SAME record immediately, with no lock wait.
        var secondPublisher = new FakeFactPublisher();
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var relay = BuildRelay(db, secondPublisher, batchSize: 10);
            var claimStopwatch = Stopwatch.StartNew();
            var result = await relay.RunOnceAsync(CancellationToken.None);
            claimStopwatch.Stop();

            Assert.Equal(1, result.Claimed);
            Assert.Equal(1, result.Published);
            Assert.True(claimStopwatch.ElapsedMilliseconds < 5000, "the next poll should claim the record immediately, with no lock wait");
        }
    }

    private static OutboxRelay BuildRelay(OrdersDbContext db, FakeFactPublisher publisher, int batchSize, int publishTimeoutMs = 5000) =>
        new(db, publisher, new FakeClock(FakeClock.UtcNowToTheMillisecond()), Options.Create(new OutboxRelayOptions { BatchSize = batchSize, PublishTimeoutMs = publishTimeoutMs }), NullLogger<OutboxRelay>.Instance);

    private static OutboxMessage NewRow(string eventType, DateTime occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        EventType = eventType,
        AggregateId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        CausationId = Guid.NewGuid(),
        Payload = "{}",
        OccurredAt = occurredAt,
        CreatedAt = occurredAt,
    };
}
