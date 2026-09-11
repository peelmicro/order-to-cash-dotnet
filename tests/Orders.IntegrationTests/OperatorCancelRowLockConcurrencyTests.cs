using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;
using EntityOrder = OrderToCash.Orders.Infrastructure.Persistence.Entities.Order;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Id 62 bullet 1's own proof, on the row-lock mechanism directly rather
/// than through the full saga/Kafka/NATS stack — <c>SagaCommandStoreTests</c>'
/// own precedent (<c>SO11_AClaimedRowIsInvisibleToAConcurrentClaimUntilItsLeaseElapses</c>)
/// for a store-level concurrency proof over a real, second MS-SQL
/// connection. Two REAL, concurrent transactions each call
/// <see cref="EfCoreOrderRepository.GetByIdAsync"/> for the SAME order —
/// the SECOND to arrive must WAIT for the FIRST's commit (never read
/// ahead of it, which is exactly what RCSI would otherwise let it do —
/// see that method's own remarks), and neither may deadlock (SQL error
/// 1205). Driven both ways (CLAUDE.md's two-party rule): the theory's
/// bool only relabels which of the two real production actors
/// (<c>CancelOrderCommandHandler</c>'s own read vs. <c>SagaFactHandler</c>'s
/// own read) plays the role of "arrives first" — the underlying SQL
/// mechanism is symmetric (one <c>UPDLOCK, ROWLOCK</c> request contends
/// with another regardless of which caller sent it), so this states that
/// symmetry explicitly rather than leaving it assumed.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class OperatorCancelRowLockConcurrencyTests(MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _holdDuration = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData(true, "CancelOrderCommandHandler")]
    [InlineData(false, "SagaFactHandler")]
    public async Task ConcurrentGetByIdAsync_ForTheSameOrder_TheSecondArrivalWaitsForTheFirstsCommit_NeitherDeadlocks(bool firstActorIsOperatorCancel, string firstActorName)
    {
        var secondActorName = firstActorIsOperatorCancel ? "SagaFactHandler" : "CancelOrderCommandHandler";

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_rowlock_{Guid.NewGuid():N}");
        var orderId = Guid.NewGuid();

        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);

            var currencyId = await seedDb.Currencies.Select(c => c.Id).SingleAsync();
            var retailerId = await seedDb.Retailers.Select(r => r.Id).SingleAsync();
            var companyId = await seedDb.Companies.Select(c => c.Id).SingleAsync();
            var productId = await seedDb.Products.Where(p => p.Code == OrderPersistenceTestSupport.ProductCode1).Select(p => p.Id).SingleAsync();

            var now = DateTime.UtcNow;
            seedDb.Orders.Add(new EntityOrder
            {
                Id = orderId,
                OrderReference = "ORD-000001",
                OrderDate = now,
                CompanyId = companyId,
                RetailerId = retailerId,
                CurrencyId = currencyId,
                InitialAmount = 2_500,
                InitialDiscount = 50,
                TotalAmount = 2_450,
                Status = "stock_reserved",
                CreatedAt = now,
                UpdatedAt = now,
                // Order.Rehydrate refuses a line-less order (OrderMustHaveAtLeastOneLineError)
                // — this test needs a REAL, rehydratable order (GetByIdAsync
                // maps the row all the way to the aggregate), not a bare
                // header row.
                Items =
                [
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ProductId = productId,
                        Description = "Product One",
                        Price = 1_000,
                        Quantity = 2,
                        Discount = 50,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                ],
            });
            await seedDb.SaveChangesAsync();
        }

        var firstHasTheLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DateTime firstCommittedAt = default;
        DateTime secondIssuedItsRequestAt = default;
        DateTime secondAcquiredAt = default;

        var firstTask = Task.Run(async () =>
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var repo = BuildRepository(db);
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repo.GetByIdAsync(UniqueId.From(orderId), ct); // acquires the UPDLOCK, ROWLOCK.
                    firstHasTheLock.TrySetResult();

                    // Holds the lock open for a KNOWN, fixed window — the
                    // second caller's own request is guaranteed to be
                    // issued while this transaction is STILL open (below),
                    // so this is a certainty, never a probability
                    // (CLAUDE.md: "determinism lives in the TEST").
                    await Task.Delay(_holdDuration, ct);
                },
                CancellationToken.None);

            firstCommittedAt = DateTime.UtcNow;
        });

        var secondTask = Task.Run(async () =>
        {
            await firstHasTheLock.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await using var db = mssql.CreateDbContext(connectionString);
            var repo = BuildRepository(db);
            var unitOfWork = new EfCoreUnitOfWork(db);

            secondIssuedItsRequestAt = DateTime.UtcNow;
            await unitOfWork.ExecuteAsync(
                async ct => await repo.GetByIdAsync(UniqueId.From(orderId), ct),
                CancellationToken.None);
            secondAcquiredAt = DateTime.UtcNow;
        });

        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(30));

        // The second caller's own request was issued comfortably BEFORE the
        // first released the lock — proves genuine overlap, not accidental
        // sequencing (a request issued AFTER the commit would prove nothing
        // about blocking).
        Assert.True(
            secondIssuedItsRequestAt < firstCommittedAt - TimeSpan.FromMilliseconds(500),
            $"the {secondActorName}-shaped caller's own request ({secondIssuedItsRequestAt:O}) must be issued while the {firstActorName}-shaped caller still holds the lock (released at {firstCommittedAt:O}), or this proves nothing about blocking.");

        // The proof itself: the second caller's GetByIdAsync only RETURNED
        // after the first caller's transaction committed — it waited, it
        // did not read ahead under RCSI's own row-versioning (which a plain,
        // unlocked SELECT would have let it do).
        Assert.True(
            secondAcquiredAt >= firstCommittedAt,
            $"expected the {secondActorName}-shaped caller's GetByIdAsync to be blocked until the {firstActorName}-shaped caller's commit ({firstCommittedAt:O}), but it acquired the row at {secondAcquiredAt:O} — {(firstCommittedAt - secondAcquiredAt).TotalMilliseconds:F0}ms before the commit.");

        // Neither task threw — in particular, no SqlException 1205
        // (deadlock victim). Task.WhenAll above would already have
        // rethrown; this is the explicit, named assertion CLAUDE.md's own
        // bullet asks for ("a deadlock surfaces as SQL error 1205, not a
        // hang").
        Assert.True(firstTask.IsCompletedSuccessfully, $"the {firstActorName}-shaped transaction did not complete successfully: {firstTask.Exception}");
        Assert.True(secondTask.IsCompletedSuccessfully, $"the {secondActorName}-shaped transaction did not complete successfully: {secondTask.Exception}");
    }

    private static EfCoreOrderRepository BuildRepository(OrdersDbContext db) =>
        new(db, new OutboxWriter(new FakeClock(DateTimeOffset.UtcNow), new NeverCalledFactPayloadMapper()));

    /// <summary>
    /// <see cref="OutboxWriter"/> is a required constructor dependency of
    /// <see cref="EfCoreOrderRepository"/>, but this suite never calls
    /// <see cref="EfCoreOrderRepository.SaveChangesAsync"/> — only
    /// <see cref="EfCoreOrderRepository.GetByIdAsync"/>, which never touches
    /// it. Throws if that assumption is ever violated, rather than silently
    /// mapping payloads incorrectly.
    /// </summary>
    private sealed class NeverCalledFactPayloadMapper : IFactPayloadMapper
    {
        public object ToPayload(FactEvent factEvent) => throw new NotSupportedException("This test never calls SaveChangesAsync/BuildRows — this mapper must never be invoked.");
    }
}
