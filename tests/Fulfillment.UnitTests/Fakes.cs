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
