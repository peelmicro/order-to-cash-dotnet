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

    /// <summary>Records the requestId AddAsync was called with, keyed by the aggregate's own id — feature observability_reliability, RI1.</summary>
    public Dictionary<UniqueId, Guid?> RequestIdsByOrderId { get; } = [];

    /// <summary>Every requestId lookup performed — feature observability_reliability, RI2/RI4: some cases assert this stays empty.</summary>
    public List<Guid> FindByRequestIdCalls { get; } = [];

    /// <summary>Answered by <see cref="FindByRequestIdAsync"/> instead of the usual by-requestId scan over <see cref="Added"/> — set by RI3 tests that need the "no lookup should have been performed" and "the winner's reply" cases to stay independent.</summary>
    public Order? FindByRequestIdResult { get; set; }

    /// <summary>
    /// RI3's race has TWO distinct calls to <see cref="FindByRequestIdAsync"/>
    /// in one <c>HandleAsync</c>: RI2's own fast-path check (which must see
    /// NOTHING yet — the order is not-yet-committed at that point, which is
    /// exactly what makes it a race) and, only on collision, the post-catch
    /// re-read. When this queue is non-empty each call dequeues its own
    /// answer; <see cref="FindByRequestIdResult"/> is the fallback once it is
    /// drained (or was never set), so every OTHER test in this file — which
    /// has only one meaningful call — needs no change.
    /// </summary>
    public Queue<Order?> FindByRequestIdResults { get; } = [];

    /// <summary>Feature observability_reliability, RI3 — thrown from <see cref="SaveChangesAsync"/> exactly once, then cleared, so it models the ONE colliding attempt inside <c>unitOfWork.ExecuteAsync</c> without making every subsequent save fail too.</summary>
    public Exception? ThrowFromNextSaveChanges { get; set; }

    public Task AddAsync(Order order, Guid? requestId, CancellationToken cancellationToken)
    {
        Added.Add(order);
        RequestIdsByOrderId[order.Id] = requestId;
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.Id == id));

    public Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.OrderReference == reference));

    public Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        FindByRequestIdCalls.Add(requestId);
        return Task.FromResult(FindByRequestIdResults.Count > 0 ? FindByRequestIdResults.Dequeue() : FindByRequestIdResult);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (ThrowFromNextSaveChanges is { } toThrow)
        {
            ThrowFromNextSaveChanges = null;
            throw toThrow;
        }

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

    // Feature orders_catalog_responder: the list-shaped half of this same
    // port. Kept on the SAME fake class as the find-shaped half above —
    // proving, at the test-double level too, that ListCatalogReferenceQueryHandlerTests
    // exercises the identical port PlaceOrderCommandHandlerTests already
    // exercises, never a second fake.
    public List<ProductCatalogEntry> ProductEntries { get; } = [];

    public List<PartyCatalogEntry> RetailerEntries { get; } = [];

    public List<PartyCatalogEntry> CompanyEntries { get; } = [];

    public List<CurrencyCatalogEntry> CurrencyEntries { get; } = [];

    public List<bool> ListProductsIncludeDisabledCalls { get; } = [];

    public List<bool> ListRetailersIncludeDisabledCalls { get; } = [];

    public List<bool> ListCompaniesIncludeDisabledCalls { get; } = [];

    public int ListCurrenciesCallCount { get; private set; }

    public Task<IReadOnlyList<ProductCatalogEntry>> ListProductsAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        ListProductsIncludeDisabledCalls.Add(includeDisabled);
        IReadOnlyList<ProductCatalogEntry> result = includeDisabled ? ProductEntries : ProductEntries.Where(p => p.Enabled).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PartyCatalogEntry>> ListRetailersAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        ListRetailersIncludeDisabledCalls.Add(includeDisabled);
        IReadOnlyList<PartyCatalogEntry> result = includeDisabled ? RetailerEntries : RetailerEntries.Where(r => r.Enabled).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PartyCatalogEntry>> ListCompaniesAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        ListCompaniesIncludeDisabledCalls.Add(includeDisabled);
        IReadOnlyList<PartyCatalogEntry> result = includeDisabled ? CompanyEntries : CompanyEntries.Where(c => c.Enabled).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<CurrencyCatalogEntry>> ListCurrenciesAsync(CancellationToken cancellationToken)
    {
        ListCurrenciesCallCount++;
        return Task.FromResult<IReadOnlyList<CurrencyCatalogEntry>>(CurrencyEntries);
    }
}

/// <summary>
/// Feature <c>observability_reliability</c>, <c>RI2</c>/design.md §2.3 — every
/// member throws unconditionally, so a test can assert "the fast path
/// performs NO reference-data lookup" the same way it asserts a count: by
/// making a violation impossible to pass silently.
/// </summary>
internal sealed class ThrowingOrderReferenceCatalog : IOrderReferenceCatalog
{
    private static InvalidOperationException Unexpected([System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        new($"RI2's fast path must not call IOrderReferenceCatalog.{member}.");

    public Task<PartyReference?> FindRetailerAsync(string retailerCode, CancellationToken cancellationToken) => throw Unexpected();

    public Task<PartyReference?> FindCompanyAsync(string companyCode, CancellationToken cancellationToken) => throw Unexpected();

    public Task<bool> CurrencyExistsAsync(string currencyCode, CancellationToken cancellationToken) => throw Unexpected();

    public Task<IReadOnlyDictionary<string, ProductReference>> FindProductsAsync(IReadOnlyCollection<string> productCodes, CancellationToken cancellationToken) => throw Unexpected();

    public Task<IReadOnlyList<ProductCatalogEntry>> ListProductsAsync(bool includeDisabled, CancellationToken cancellationToken) => throw Unexpected();

    public Task<IReadOnlyList<PartyCatalogEntry>> ListRetailersAsync(bool includeDisabled, CancellationToken cancellationToken) => throw Unexpected();

    public Task<IReadOnlyList<PartyCatalogEntry>> ListCompaniesAsync(bool includeDisabled, CancellationToken cancellationToken) => throw Unexpected();

    public Task<IReadOnlyList<CurrencyCatalogEntry>> ListCurrenciesAsync(CancellationToken cancellationToken) => throw Unexpected();
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
