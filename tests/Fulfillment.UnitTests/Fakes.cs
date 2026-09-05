using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>Runs the delegate INLINE, with no real transaction — exactly the shape a unit test needs to prove "the reply is built from the delegate's return value" without a database.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int ExecuteCount { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ExecuteCount++;
        return await work(cancellationToken);
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ExecuteCount++;
        await work(cancellationToken);
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeStockItemRepository : IStockItemRepository
{
    public StockLockResult LockResult { get; set; } = new(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase), []);

    public OrderReservationLookup? ProductCodesLookup { get; set; }

    public IReadOnlyDictionary<string, StockItem> LockItemsResult { get; set; } = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase);

    public int SaveChangesCallCount { get; private set; }

    public Func<Task>? OnSaveChanges { get; set; }

    public Task<StockLockResult> LockForOrderAsync(string companyCode, IReadOnlyList<string> productCodes, OrderNumber orderReference, CancellationToken cancellationToken) =>
        Task.FromResult(LockResult);

    public Task<IReadOnlyDictionary<string, StockItem>> LockItemsAsync(string companyCode, IReadOnlyList<string> productCodes, CancellationToken cancellationToken) =>
        Task.FromResult(LockItemsResult);

    public Task<OrderReservationLookup?> ProductCodesOfOrderAsync(OrderNumber orderReference, CancellationToken cancellationToken) =>
        Task.FromResult(ProductCodesLookup);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        if (OnSaveChanges is not null)
        {
            await OnSaveChanges();
        }
    }
}

internal sealed class FakeDespatchRepository : IDespatchRepository
{
    /// <summary>Returned by every call unless <see cref="FindByCallIndex"/> is set — the F8 in-flight race needs the FIRST call (the fast path) to answer differently from the SECOND (the re-read under lock), which a single fixed value cannot express.</summary>
    public DespatchSnapshot? ExistingSnapshot { get; set; }

    public Func<int, DespatchSnapshot?>? FindByCallIndex { get; set; }

    public int FindCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public Domain.DespatchAdvice? Saved { get; private set; }

    public Task<DespatchSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken)
    {
        FindCallCount++;
        return Task.FromResult(FindByCallIndex is not null ? FindByCallIndex(FindCallCount) : ExistingSnapshot);
    }

    public Task SaveAsync(Domain.DespatchAdvice despatch, CancellationToken cancellationToken)
    {
        SaveCallCount++;
        Saved = despatch;
        return Task.CompletedTask;
    }
}

internal sealed class FakeDespatchNumberAllocator : IDespatchNumberAllocator
{
    public string NextReference { get; set; } = "DES-000001";

    public int CallCount { get; private set; }

    public Task<string> AllocateNextAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(NextReference);
    }
}
