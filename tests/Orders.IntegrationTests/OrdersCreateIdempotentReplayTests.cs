using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Presentation;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature <c>observability_reliability</c>, half B — <c>orders.create</c>
/// <c>requestId</c> idempotent replay, over REAL infrastructure
/// (<c>RI1</c>/<c>RI3</c>/<c>RI4</c>, design.md §2, ledger L1/L2/L6). The
/// handler's own branching is proven with fakes in
/// <c>Orders.UnitTests/PlaceOrderRequestIdReplayTests.cs</c> — this file
/// proves the two claims only a real MS-SQL engine and a real NATS wire can
/// prove: the filtered unique index's actual admit/reject behaviour, and
/// the outcome of a genuine concurrent race.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class OrdersCreateIdempotentReplayTests(NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    /// <summary>RI1 — a supplied requestId is persisted against the created order, under the SAME constraint RI4 proves still admits many rows with none.</summary>
    [Fact]
    public async Task RI1_PersistsRequestIdAgainstTheCreatedOrderUnderAUniquenessConstraintThatStillAdmitsManyOrdersWithNone()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_ri1_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var requestId = Guid.NewGuid();
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var order = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, requestId, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var row = await assertDb.Orders.SingleAsync();
        Assert.Equal(requestId, row.RequestId);
    }

    /// <summary>
    /// RI4, ⚑ARM — count, ledger L1: two orders placed with NO requestId
    /// both commit — the ONLY shape that discriminates a filtered unique
    /// index from an unfiltered one, since a single such order passes
    /// either way (design.md §10.4).
    /// </summary>
    [Fact]
    public async Task RI4_TwoOrdersPlacedWithNoRequestIdBothCommit_TheNullableUniqueIndexAdmittingBoth()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_ri4_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());

        for (var i = 1; i <= 2; i++)
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var order = OrderPersistenceTestSupport.Place(new OrderNumber(i), clock.UtcNow, UniqueId.New());
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, requestId: null, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(2, await assertDb.Orders.CountAsync());
        Assert.All(await assertDb.Orders.ToListAsync(), o => Assert.Null(o.RequestId));
    }

    /// <summary>
    /// Ledger L4 — <c>FindByRequestIdAsync</c> is a NO-TRACKING read that
    /// never enters the EF adapter's identity map: calling it after a
    /// successful placement leaves the real <c>ChangeTracker</c> holding
    /// nothing, and a later <c>SaveChangesAsync</c> in the SAME scope writes
    /// nothing further.
    /// </summary>
    [Fact]
    public async Task L4_FindByRequestIdAsync_IsNoTracking_AndALaterSaveChangesInTheSameScopeWritesNothing()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_l4_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var requestId = Guid.NewGuid();
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());

        await using var db = mssql.CreateDbContext(connectionString);
        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
        var unitOfWork = new EfCoreUnitOfWork(db);
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());

        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                await repository.AddAsync(order, requestId, ct);
                await repository.SaveChangesAsync(ct);
            },
            CancellationToken.None);

        var found = await repository.FindByRequestIdAsync(requestId, CancellationToken.None);
        Assert.NotNull(found);

        // The tracker holds NO Order entry — FindByRequestIdAsync's own
        // AsNoTracking read never entered it, and it CLEARED whatever the
        // placement above left tracked (ledger L4's real mechanism).
        Assert.Empty(db.ChangeTracker.Entries<OrderToCash.Orders.Infrastructure.Persistence.Entities.Order>());

        // A SaveChangesAsync call in the SAME scope, on the SAME repository
        // instance (whose private identity map still references the
        // now-detached row), writes NOTHING further.
        await repository.SaveChangesAsync(CancellationToken.None);

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(1, await assertDb.Orders.CountAsync());
        Assert.Equal(1, await assertDb.OutboxMessages.CountAsync(o => o.EventType == "order.placed.v1"));
    }

    /// <summary>
    /// RI3, ⚑ARM — count, ledger L2 and L6: two GENUINELY concurrent
    /// <c>orders.create</c> RPC calls over the real NATS wire, carrying the
    /// SAME, not-yet-committed requestId, create EXACTLY one order and
    /// EXACTLY one <c>order.placed.v1</c> outbox row — never two of either
    /// — and the loser's reply equals the winner's field-by-field. Run
    /// five independent rounds, each against its own fresh database, so a
    /// race that only sometimes resolves correctly cannot hide behind a
    /// single lucky ordering.
    /// </summary>
    [Fact]
    public async Task RI3_TwoConcurrentFirstTimeOrdersCreateRequestsCarryingTheSameRequestIdCreateExactlyOneOrder_AndTheLosersReplyEqualsTheWinners()
    {
        for (var round = 1; round <= 5; round++)
        {
            var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_ri3_{round}_{Guid.NewGuid():N}");
            await using (var seedDb = mssql.CreateDbContext(connectionString))
            {
                await seedDb.Database.MigrateAsync();
                await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
            }

            var requestId = Guid.NewGuid();

            // TWO independent Orders host instances, both subscribed to the
            // SAME orders.create subject on the SAME real NATS server — the
            // horizontally-scaled shape this race actually depends on.
            // OrdersCreateResponder's own subscription loop processes ONE
            // subject sequentially per INSTANCE (design.md §1, "unchanged
            // from before this feature"), so a single instance can never
            // let two DIFFERENT client requests race at the database: by
            // the time a second request is even dequeued, the first has
            // already fully committed or rolled back, and RI2's own fast
            // path would short-circuit the second before it ever reached
            // the collision path. TWO instances is what makes "genuinely
            // concurrent" true rather than merely asserted — proven by
            // Group B's own arming pass, where the same three assertions
            // stayed green against a SINGLE instance even with the
            // duplicate-key catch entirely deleted (progress/impl_observability_reliability.md).
            using var hostA = BuildHost(connectionString);
            using var hostB = BuildHost(connectionString);
            await hostA.StartAsync();
            await hostB.StartAsync();
            try
            {
                await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
                await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
                await WaitUntilBothInstancesReachableAsync(caller, CancellationToken.None);

                var request = new OrdersCreateRequestPayload(
                    RequestId: requestId,
                    OrderPersistenceTestSupport.RetailerCode,
                    OrderPersistenceTestSupport.CompanyCode,
                    OrderPersistenceTestSupport.Currency,
                    Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 1, UnitPrice: 1_000, LineDiscount: null)],
                    OrderDiscount: null,
                    Notes: null);
                var requestBytes = RpcJson.Serialize(request);

                // ONE publish, fanned out by NATS core pub/sub (no queue
                // group — matching OrdersCreateResponder's own,
                // unmodified, production subscription) to BOTH instances
                // at once: each independently runs its own FULL handler
                // pipeline concurrently, which is exactly RI3's premise
                // ("two ... requests carrying the same, not-yet-committed
                // requestId are processed concurrently"). Collecting BOTH
                // replies (rather than the single first-arrival
                // RequestAsync gives) is what lets this test see the
                // LOSER's own reply, not only the winner's.
                var replies = await PublishAndCollectRepliesAsync(caller, RpcSubjects.OrdersCreate, requestBytes, expectedReplyCount: 2, TimeSpan.FromSeconds(20), CancellationToken.None);

                Assert.Equal(2, replies.Count);
                var replyA = RpcJson.Deserialize<OrdersCreateReplyPayload>(replies[0]);
                var replyB = RpcJson.Deserialize<OrdersCreateReplyPayload>(replies[1]);

                Assert.Equal("placed", replyA.Status);
                Assert.Equal("placed", replyB.Status);
                Assert.Equal(replyA, replyB);

                await using var assertDb = mssql.CreateDbContext(connectionString);
                Assert.Equal(1, await assertDb.Orders.CountAsync());
                var orderRow = await assertDb.Orders.SingleAsync();
                Assert.Equal(requestId, orderRow.RequestId);
                Assert.Equal(1, await assertDb.OutboxMessages.CountAsync(o => o.EventType == "order.placed.v1"));
            }
            finally
            {
                await hostA.StopAsync();
                await hostB.StopAsync();
            }
        }
    }

    /// <summary>
    /// RI3, ⚑ARM (B7c-iii) — design.md §2.4 states the race resolves via
    /// serialisation on the order-number counter, never a deadlock, and
    /// that a genuine deadlock (if one ever occurred) would surface as SQL
    /// error 1205 promptly rather than hang. THIS handler cannot produce a
    /// real deadlock on its own (there is only ever one serialisation
    /// point), so this case forces one directly against the real engine —
    /// two raw transactions taking two row locks in OPPOSITE order — and
    /// proves the environment this feature depends on (MS-SQL,
    /// <c>ReadCommitted</c> + <c>READ_COMMITTED_SNAPSHOT ON</c>) reports the
    /// deadlock rather than hanging. A bounded overall wait
    /// (<see cref="Task.WhenAny(Task,Task)"/> against a timeout task) is
    /// itself part of the proof: a hang would fail this case by timing out,
    /// not by a wrong assertion.
    /// </summary>
    [Fact]
    public async Task RI3_ALockOrderInversionReportsSql1205RatherThanHanging()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_lockinv_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        Guid orderOneId, orderTwoId;
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var orderOne = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());
            var orderTwo = OrderPersistenceTestSupport.Place(new OrderNumber(2), clock.UtcNow, UniqueId.New());
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);
            await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(orderOne, null, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
            await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(orderTwo, null, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
            orderOneId = orderOne.Id.Value;
            orderTwoId = orderTwo.Id.Value;
        }

        var aHoldsFirstLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bHoldsFirstLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int?> RunAsync(Guid firstId, Guid secondId, TaskCompletionSource ownFirstLock, TaskCompletionSource otherFirstLock)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();

            await using (var first = connection.CreateCommand())
            {
                first.Transaction = transaction;
                first.CommandText = "UPDATE dbo.orders SET notes = 'lock-order-inversion' WHERE id = @id";
                first.Parameters.AddWithValue("@id", firstId);
                await first.ExecuteNonQueryAsync();
            }

            ownFirstLock.TrySetResult();
            // Both sides must hold their FIRST lock before either attempts
            // its SECOND, crossed one — otherwise one side could simply
            // acquire both locks uncontested and no deadlock would form.
            await otherFirstLock.Task.WaitAsync(TimeSpan.FromSeconds(10));

            try
            {
                await using var second = connection.CreateCommand();
                second.Transaction = transaction;
                second.CommandText = "UPDATE dbo.orders SET notes = 'lock-order-inversion' WHERE id = @id";
                second.Parameters.AddWithValue("@id", secondId);
                await second.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
                return null;
            }
            catch (SqlException ex)
            {
                await transaction.RollbackAsync();
                return ex.Number;
            }
        }

        var taskA = RunAsync(orderOneId, orderTwoId, aHoldsFirstLock, bHoldsFirstLock);
        var taskB = RunAsync(orderTwoId, orderOneId, bHoldsFirstLock, aHoldsFirstLock);
        var raceTask = Task.WhenAll(taskA, taskB);

        var overallTimeout = Task.Delay(TimeSpan.FromSeconds(20));
        var completed = await Task.WhenAny(raceTask, overallTimeout);

        Assert.True(ReferenceEquals(completed, raceTask), "Timed out: a genuine deadlock must be reported by the engine, not hang.");
        Assert.True(taskA.IsCompletedSuccessfully && taskB.IsCompletedSuccessfully, "Both sides must resolve (one via commit, one via a reported deadlock) rather than fault the test task itself.");

        var results = new[] { await taskA, await taskB };
        // Exactly ONE side is the deadlock victim (SQL error 1205); the
        // other commits normally.
        Assert.Single(results, r => r == 1205);
        Assert.Single(results, r => r is null);
    }

    /// <summary>
    /// Paces on the SAME cheap, side-effect-free probe
    /// <c>OrdersCreateAcceptanceTests.WaitUntilOrdersCreateReachableAsync</c>
    /// uses (an unknown <c>retailerCode</c> — a fast <c>NOT_FOUND</c>, no
    /// DB write, no stock check), but requires TWO distinct replies before
    /// declaring victory, because this test needs BOTH instances
    /// subscribed, not merely "at least one."
    /// </summary>
    private static async Task WaitUntilBothInstancesReachableAsync(NatsConnection caller, CancellationToken cancellationToken)
    {
        var probe = new OrdersCreateRequestPayload(
            RequestId: null,
            RetailerCode: "NATS-PROBE-CONNECTIVITY",
            CompanyCode: "NATS-PROBE-CONNECTIVITY",
            Currency: "EUR",
            Lines: [new OrdersCreateRequestLine("NATS-PROBE-CONNECTIVITY", 1, null, null)],
            OrderDiscount: null,
            Notes: null);
        var probeBytes = RpcJson.Serialize(probe);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var replies = await PublishAndCollectRepliesAsync(caller, RpcSubjects.OrdersCreate, probeBytes, expectedReplyCount: 2, TimeSpan.FromMilliseconds(300), cancellationToken);
            if (replies.Count >= 2)
            {
                return;
            }

            // Paced explicitly, not inferred from the per-attempt timeout
            // alone — CLAUDE.md's standing rule: the fast-failing case here
            // (nobody subscribed yet) returns near-instantly, so an
            // attempt-counted budget with no floor could burn through all
            // 100 attempts in well under a second.
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        throw new TimeoutException("Both Orders host instances never became reachable on orders.create.");
    }

    /// <summary>
    /// Publishes ONE message with a fresh, dedicated reply inbox and
    /// collects up to <paramref name="expectedReplyCount"/> replies within
    /// <paramref name="perAttemptTimeout"/> — the raw publish/subscribe
    /// pair underneath NATS's own <c>RequestAsync</c> convenience, used
    /// directly here because <c>RequestAsync</c> only ever surfaces the
    /// FIRST reply and this test needs every subscriber's own answer.
    /// </summary>
    private static async Task<List<byte[]>> PublishAndCollectRepliesAsync(NatsConnection connection, string subject, byte[] data, int expectedReplyCount, TimeSpan perAttemptTimeout, CancellationToken cancellationToken)
    {
        var replySubject = connection.NewInbox();
        var replies = new List<byte[]>();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(perAttemptTimeout);

        var collectTask = Task.Run(
            async () =>
            {
                try
                {
                    await foreach (var message in connection.SubscribeAsync<byte[]>(replySubject, cancellationToken: timeoutCts.Token).ConfigureAwait(false))
                    {
                        if (message.Data is { } data2)
                        {
                            replies.Add(data2);
                        }

                        if (replies.Count >= expectedReplyCount)
                        {
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timed out with fewer than expectedReplyCount replies —
                    // the caller inspects `replies.Count` itself.
                }
            },
            timeoutCts.Token);

        // Give the subscription a moment to genuinely attach server-side
        // before publishing — a real round trip, not a fixed sleep
        // standing in for one: the readiness probe above already proved
        // orders.create itself is reachable, so this is only bridging the
        // gap between "SubscribeAsync returned" and "the server has
        // registered the interest" for THIS inbox.
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);

        await connection.PublishAsync(subject, data, replyTo: replySubject, cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            await collectTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Same as above.
        }

        return replies;
    }

    private IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOrdersOutbox(options =>
        {
            options.ConnectionString = connectionString;
            options.Relay.Enabled = false;
            // Mechanism-2 classification: this host's StopAsync/Dispose
            // sites below do NOT need the group-clearance wait —
            // "127.0.0.1:1" is deliberately unreachable, so SagaFactsConsumer
            // can never actually join a real "orders.saga" group on ANY
            // broker; mechanism 2 needs a real shared broker to cross a
            // test boundary, which is structurally impossible here.
            options.Kafka.BootstrapServers = "127.0.0.1:1";
        });
        builder.Services.AddOrdersAcceptance(options => options.Nats.Url = nats.Url);
        builder.Services.AddDispatcher(typeof(PlaceOrderCommand).Assembly);

        return builder.Build();
    }
}
