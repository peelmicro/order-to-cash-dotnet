using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>PlaceOrderCommandHandler</c> — orders_acceptance's three acceptance
/// items realised at the Application layer: acceptance item 1 (synchronous
/// stock check BEFORE anything persists), item 2 (the order id/reference/
/// totals returned synchronously from the same call), item 3 (rejection —
/// and NOTHING persisted — when the stock check fails). Fakes only, no
/// mocking library, no NATS, no MS-SQL — the transport itself is proven in
/// <c>Orders.IntegrationTests</c>.
/// </summary>
public sealed class PlaceOrderCommandHandlerTests
{
    private const string RetailerCode = "RETAILER-01";
    private const string CompanyCode = "COMPANY-01";
    private const string Currency = "EUR";
    private const string ProductCode1 = "PROD-001";
    private const string ProductCode2 = "PROD-002";

    private static readonly GLN _buyerGln = new("4006381333931");
    private static readonly GLN _supplierGln = new("5001234567890");

    [Fact]
    public async Task AcceptanceItems1And2_Handler_ChecksStockBeforePersistingAndReturnsTheOrderIdSynchronously()
    {
        var (handler, repository, allocator, stock) = BuildHandler(stockAvailable: true);

        var command = TwoLineCommand();
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Item 1: the stock check ran, and it ran with the request's own lines/company.
        var call = Assert.Single(stock.Calls);
        Assert.Equal(CompanyCode, call.CompanyCode);
        Assert.Equal([ProductCode1, ProductCode2], call.Lines.Select(l => l.ProductCode));

        // Item 2: the order id, reference and totals come back on THIS call — no
        // second round trip needed. initialAmount = 1000*2 + 500*1 = 2500,
        // initialDiscount = 50 + 0 = 50, totalAmount = 2450 — three DISTINCT,
        // non-zero values (the #7 D-defect fixture rule).
        Assert.NotEqual(default, result.OrderId);
        Assert.Equal("ORD-000001", result.OrderReference.Value);
        Assert.Equal(OrderStatus.Placed, result.Status);
        Assert.Equal(new Money(2_500, Currency), result.InitialAmount);
        Assert.Equal(new Money(50, Currency), result.InitialDiscount);
        Assert.Equal(new Money(2_450, Currency), result.TotalAmount);
        Assert.NotEqual(result.InitialAmount, result.InitialDiscount);
        Assert.NotEqual(result.InitialDiscount, result.TotalAmount);
        Assert.NotEqual(result.InitialAmount, result.TotalAmount);

        // The order was genuinely persisted through the repository — one
        // aggregate added, saved exactly once.
        var added = Assert.Single(repository.Added);
        Assert.Equal(result.OrderId, added.Id);
        Assert.Equal(1, repository.SaveChangesCallCount);
        Assert.Equal(1, allocator.CallCount);
    }

    /// <summary>
    /// Acceptance item 3, and CLAUDE.md's suppression-direction guard: a
    /// stock rejection persists NOTHING — no aggregate added, no
    /// <c>SaveChangesAsync</c> call, no order-number allocated (the
    /// allocator runs inside the SAME unit of work as the persist, after
    /// the stock check, so it must never even be called).
    /// </summary>
    [Fact]
    public async Task AcceptanceItem3_Handler_RejectsAndPersistsNothingWhenTheStockCheckReportsUnavailable()
    {
        var shortage = new StockAvailabilityLineResult(ProductCode1, Requested: 2, Available: 1, Sufficient: false);
        var (handler, repository, allocator, _) = BuildHandler(stockAvailable: false, [shortage]);

        var error = await Assert.ThrowsAsync<StockUnavailableError>(() => handler.HandleAsync(TwoLineCommand(), CancellationToken.None));

        Assert.Equal("STOCK_UNAVAILABLE", error.Code);
        Assert.Same(shortage, Assert.Single(error.Shortages));
        Assert.Empty(repository.Added);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public async Task Handler_PropagatesAStockCheckTimeoutWithoutPersistingAnything()
    {
        var (handler, repository, allocator, _) = BuildHandler(stockAvailable: true, throwsFromStockCheck: new StockCheckTimeoutError("fulfillment.stock.check", 5000));

        var error = await Assert.ThrowsAsync<StockCheckTimeoutError>(() => handler.HandleAsync(TwoLineCommand(), CancellationToken.None));

        Assert.Equal("fulfillment.stock.check", error.Subject);
        Assert.Empty(repository.Added);
        Assert.Equal(0, allocator.CallCount);
    }

    [Theory]
    [InlineData("retailerCode", "UNKNOWN-RETAILER")]
    [InlineData("companyCode", "UNKNOWN-COMPANY")]
    [InlineData("currency", "USD")]
    [InlineData("productCode", "UNKNOWN-PRODUCT")]
    public async Task Handler_RefusesUnresolvableReferenceDataBeforeCheckingStock(string field, string offendingValue)
    {
        var (handler, repository, _, stock) = BuildHandler(stockAvailable: true);
        var command = field switch
        {
            "retailerCode" => TwoLineCommand() with { RetailerCode = offendingValue },
            "companyCode" => TwoLineCommand() with { CompanyCode = offendingValue },
            "currency" => TwoLineCommand() with { Currency = offendingValue },
            "productCode" => TwoLineCommand() with
            {
                Lines = [new PlaceOrderRequestLine(offendingValue, new Quantity(1), 100, 0)],
            },
            _ => throw new InvalidOperationException(),
        };

        var error = await Assert.ThrowsAsync<ReferenceDataNotFoundError>(() => handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal("REFERENCE_DATA_NOT_FOUND", error.Code);
        Assert.Equal(field, error.Field);
        Assert.Equal(offendingValue, error.Value);
        Assert.Empty(stock.Calls);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Handler_RefusesANonZeroOrderDiscountBeforeResolvingAnythingElse()
    {
        var (handler, repository, _, stock) = BuildHandler(stockAvailable: true);
        var command = TwoLineCommand() with { OrderDiscountMinorUnits = 150 };

        var error = await Assert.ThrowsAsync<OrderDiscountNotSupportedError>(() => handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal("ORDER_DISCOUNT_NOT_SUPPORTED", error.Code);
        Assert.Equal(150, error.OrderDiscountMinorUnits);
        Assert.Empty(stock.Calls);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Handler_SnapshotsTheCatalougePriceWhenALineOmitsUnitPrice()
    {
        var (handler, repository, _, _) = BuildHandler(stockAvailable: true);
        var command = TwoLineCommand() with
        {
            Lines = [new PlaceOrderRequestLine(ProductCode1, new Quantity(3), UnitPriceMinorUnits: null, LineDiscountMinorUnits: null)],
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        var line = Assert.Single(Assert.Single(repository.Added).Lines);
        Assert.Equal(new Money(1_000, Currency), line.UnitPrice); // the catalogue price, PROD-001 -> 1000
        Assert.Equal(Money.Zero(Currency), line.LineDiscount);
        Assert.Equal(new Money(3_000, Currency), result.InitialAmount);
    }

    private static PlaceOrderCommand TwoLineCommand() => new(
        RequestId: null,
        RetailerCode,
        CompanyCode,
        Currency,
        Lines:
        [
            new PlaceOrderRequestLine(ProductCode1, new Quantity(2), UnitPriceMinorUnits: 1_000, LineDiscountMinorUnits: 50),
            new PlaceOrderRequestLine(ProductCode2, new Quantity(1), UnitPriceMinorUnits: 500, LineDiscountMinorUnits: 0),
        ],
        OrderDiscountMinorUnits: null,
        Notes: null);

    private static (PlaceOrderCommandHandler Handler, FakeOrderRepository Repository, FakeOrderNumberAllocator Allocator, FakeStockAvailabilityChecker Stock) BuildHandler(
        bool stockAvailable,
        IReadOnlyList<StockAvailabilityLineResult>? shortages = null,
        Exception? throwsFromStockCheck = null)
    {
        var catalog = new FakeOrderReferenceCatalog();
        catalog.Retailers[RetailerCode] = new PartyReference(RetailerCode, _buyerGln);
        catalog.Companies[CompanyCode] = new PartyReference(CompanyCode, _supplierGln);
        catalog.Currencies.Add(Currency);
        catalog.Products[ProductCode1] = new ProductReference(ProductCode1, "First product", new Money(1_000, Currency));
        catalog.Products[ProductCode2] = new ProductReference(ProductCode2, "Second product", new Money(500, Currency));

        var stockResult = new StockAvailabilityResult(
            stockAvailable,
            shortages ?? (stockAvailable ? [] : [new StockAvailabilityLineResult(ProductCode1, 1, 0, false)]));
        var stock = new FakeStockAvailabilityChecker(stockResult, throwsFromStockCheck);

        var repository = new FakeOrderRepository();
        var allocator = new FakeOrderNumberAllocator();
        var handler = new PlaceOrderCommandHandler(
            new FakeUnitOfWork(),
            repository,
            allocator,
            catalog,
            stock,
            new FakeClock(new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero)));

        return (handler, repository, allocator, stock);
    }
}
