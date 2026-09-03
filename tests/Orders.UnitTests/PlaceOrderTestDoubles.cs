using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Hand-rolled fakes for <c>PlaceOrderCommandHandlerTests</c> — no mocking
/// library, matching this project's own constraint ("a mocking library must
/// not appear in this project's <c>PackageReference</c> list at all",
/// design.md §11.1). These are Application-layer port fakes, not domain
/// doubles, following the exact shape <c>Orders.IntegrationTests.FakeClock</c>/
/// <c>FakeFactPublisher</c> already established for this codebase.
/// </summary>
internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

/// <summary>Runs the delegate inline — no real transaction, matching the "safe to execute more than once" contract trivially since it is only ever invoked once here.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) => work(cancellationToken);

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) => work(cancellationToken);
}

/// <summary>Records every call so a test can assert NOTHING was persisted on a rejected placement (the suppression-direction guard).</summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Added { get; } = [];

    public int SaveChangesCallCount { get; private set; }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        Added.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.Id == id));

    public Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.OrderReference == reference));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeOrderNumberAllocator : IOrderNumberAllocator
{
    private long _next = 1;

    public int CallCount { get; private set; }

    public Task<OrderNumber> AllocateNextAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new OrderNumber(_next++));
    }
}

internal sealed class FakeOrderReferenceCatalog : IOrderReferenceCatalog
{
    public Dictionary<string, PartyReference> Retailers { get; } = [];

    public Dictionary<string, PartyReference> Companies { get; } = [];

    public HashSet<string> Currencies { get; } = [];

    public Dictionary<string, ProductReference> Products { get; } = [];

    public Task<PartyReference?> FindRetailerAsync(string retailerCode, CancellationToken cancellationToken) =>
        Task.FromResult(Retailers.GetValueOrDefault(retailerCode));

    public Task<PartyReference?> FindCompanyAsync(string companyCode, CancellationToken cancellationToken) =>
        Task.FromResult(Companies.GetValueOrDefault(companyCode));

    public Task<bool> CurrencyExistsAsync(string currencyCode, CancellationToken cancellationToken) =>
        Task.FromResult(Currencies.Contains(currencyCode));

    public Task<IReadOnlyDictionary<string, ProductReference>> FindProductsAsync(IReadOnlyCollection<string> productCodes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, ProductReference>>(
            Products.Where(kv => productCodes.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
}

/// <summary>Answers a fixed <see cref="StockAvailabilityResult"/> or throws a fixed transport exception — never both — recording every call's arguments for the "checked BEFORE anything is persisted" assertions.</summary>
internal sealed class FakeStockAvailabilityChecker(StockAvailabilityResult? result = null, Exception? throws = null) : IStockAvailabilityChecker
{
    public List<(string CompanyCode, IReadOnlyList<StockAvailabilityLine> Lines)> Calls { get; } = [];

    public Task<StockAvailabilityResult> CheckAsync(string companyCode, IReadOnlyList<StockAvailabilityLine> lines, CancellationToken cancellationToken)
    {
        Calls.Add((companyCode, lines));

        if (throws is not null)
        {
            throw throws;
        }

        return Task.FromResult(result!);
    }
}
