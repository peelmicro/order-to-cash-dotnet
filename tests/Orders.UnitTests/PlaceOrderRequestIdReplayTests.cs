using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Feature <c>observability_reliability</c>, half B (design.md §2) —
/// <c>orders.create</c> <c>requestId</c> idempotent replay, at the
/// <c>PlaceOrderCommandHandler</c> level. Fakes only, matching
/// <c>PlaceOrderCommandHandlerTests</c>' own constraint (no mocking
/// library). The concurrent race itself (<c>RI3</c>'s integration case,
/// against a real MS-SQL unique-index collision) lives in
/// <c>Orders.IntegrationTests/OrdersCreateIdempotentReplayTests.cs</c> —
/// this file proves the HANDLER's own branching, over a repository fake
/// that reports the collision the same shape a real one would.
/// </summary>
public sealed class PlaceOrderRequestIdReplayTests
{
    private static readonly Guid _requestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// RI2 — a repeated requestId for which a committed order already
    /// exists performs NO reference-data lookup, NO stock check, and
    /// returns the ORIGINAL order's reply, mapped through the SAME
    /// <c>ToResult</c> the normal placement path uses (ledger L7) — every
    /// field, including all three DISTINCT money fields, compared against
    /// the original.
    /// </summary>
    [Fact]
    public async Task RI2_ARepeatedRequestIdReturnsTheOriginalOrdersReply_PerformingNoReferenceDataLookupAndNoStockCheck()
    {
        var original = OrderTestData.PlacedOrder();
        var repository = new FakeOrderRepository { FindByRequestIdResult = original };
        var allocator = new FakeOrderNumberAllocator();
        var catalog = new ThrowingOrderReferenceCatalog();
        var stock = new FakeStockAvailabilityChecker(throws: new InvalidOperationException("RI2's fast path must not call the stock check."));
        var handler = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repository, allocator, catalog, stock, new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(
            RequestId: _requestId,
            OrderTestData.RetailerCode,
            OrderTestData.CompanyCode,
            OrderTestData.Currency,
            Lines: [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)],
            OrderDiscountMinorUnits: null,
            Notes: null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(original.Id, result.OrderId);
        Assert.Equal(original.OrderReference, result.OrderReference);
        Assert.Equal(original.Status, result.Status);
        Assert.Equal(original.Currency, result.Currency);
        Assert.Equal(original.InitialAmount, result.InitialAmount);
        Assert.Equal(original.InitialDiscount, result.InitialDiscount);
        Assert.Equal(original.TotalAmount, result.TotalAmount);
        Assert.Equal(original.OrderDate, result.OrderDate);

        // No reference-data lookup, no stock check, no write, no order-number
        // allocation — the fast path exits before ANY of that (RI2).
        Assert.Empty(stock.Calls);
        Assert.Equal(0, allocator.CallCount);
        Assert.Empty(repository.Added);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Equal([_requestId], repository.FindByRequestIdCalls);
    }

    /// <summary>Arms RI2's own fast path: deleting the early return would make this call fall through to the throwing reference-data fake.</summary>
    [Fact]
    public async Task RI2_TheFastPathReturnsBeforeReferenceDataIsEverTouched()
    {
        var original = OrderTestData.PlacedOrder();
        var repository = new FakeOrderRepository { FindByRequestIdResult = original };
        var handler = new PlaceOrderCommandHandler(
            new FakeUnitOfWork(),
            repository,
            new FakeOrderNumberAllocator(),
            new ThrowingOrderReferenceCatalog(),
            new FakeStockAvailabilityChecker(throws: new InvalidOperationException("must not run")),
            new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(_requestId, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency,
            [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null);

        // No exception from the throwing catalog reaching the caller is
        // itself the proof — if the early return were deleted, this would
        // throw InvalidOperationException instead of returning.
        var result = await handler.HandleAsync(command, CancellationToken.None);
        Assert.Equal(original.Id, result.OrderId);
    }

    /// <summary>
    /// RI3 — the loser of a requestId race re-reads and resolves to the
    /// WINNER's reply rather than propagating the collision as an error.
    /// The repository fake throws the SAME shape a real
    /// <c>uq_orders_request_id</c> collision throws (ledger L3's captured
    /// message, reproduced via <see cref="SqlExceptionFactory"/>), never a
    /// bespoke exception the handler could accidentally match on the wrong
    /// property.
    /// </summary>
    [Fact]
    public async Task RI3_ADuplicateKeyOnTheRequestIdIndexResolvesToTheWinnersReReadReply()
    {
        var winner = OrderTestData.PlacedOrder();
        var repository = new FakeOrderRepository { ThrowFromNextSaveChanges = RequestIdIndexCollision() };
        // RI2's own fast-path check must see NOTHING (this request is not
        // yet committed under its requestId — that is exactly what makes it
        // a race); only the post-collision re-read finds the winner.
        repository.FindByRequestIdResults.Enqueue(null);
        repository.FindByRequestIdResults.Enqueue(winner);

        var catalog = CatalogWithOneProduct();
        var stock = new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, []));
        var handler = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repository, new FakeOrderNumberAllocator(), catalog, stock, new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(_requestId, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency,
            [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(winner.Id, result.OrderId);
        Assert.Equal(winner.OrderReference, result.OrderReference);
        Assert.Equal(winner.TotalAmount, result.TotalAmount);
        Assert.Equal([_requestId, _requestId], repository.FindByRequestIdCalls);
    }

    /// <summary>
    /// RI3's own "never a silent null" clause: if the collision is real but
    /// the winner cannot be read back, the ORIGINAL DbUpdateException
    /// propagates rather than a manufactured error or a null reply.
    /// </summary>
    [Fact]
    public async Task RI3_ADuplicateKeyOnTheRequestIdIndexWithNoReadableWinnerPropagatesTheOriginalException()
    {
        var repository = new FakeOrderRepository { ThrowFromNextSaveChanges = RequestIdIndexCollision() };
        repository.FindByRequestIdResults.Enqueue(null); // RI2's fast-path check: not yet committed.
        repository.FindByRequestIdResults.Enqueue(null); // the post-catch re-read: still not readable.

        var handler = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repository, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(_requestId, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency,
            [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null);

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(command, CancellationToken.None));
    }

    /// <summary>
    /// RI3 — a collision on `orders`'s OTHER unique index
    /// (`order_reference`) is NOT a requestId race and must propagate
    /// UNCHANGED, never mistaken for one and never resolved to a re-read.
    /// Ledger L3's discriminator: same SQL error number (2601), different
    /// index name in the message.
    /// </summary>
    [Fact]
    public async Task RI3_ADuplicateKeyOnTheOrderReferenceIndexPropagatesUnchanged()
    {
        var repository = new FakeOrderRepository { ThrowFromNextSaveChanges = OrderReferenceIndexCollision() };
        // RI2's fast-path check: not yet committed under this requestId.
        repository.FindByRequestIdResults.Enqueue(null);
        // If the index-name check were dropped, RequestIdCollision.Matches
        // would wrongly match ANY duplicate-key error and the catch would
        // re-read here — answering a real order would make a defective
        // handler return successfully instead of throwing, which is
        // EXACTLY what this arms: the assertion below fails on that mutant.
        repository.FindByRequestIdResults.Enqueue(OrderTestData.PlacedOrder());

        var handler = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repository, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(_requestId, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency,
            [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null);

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(command, CancellationToken.None));
        // Exactly the ONE fast-path call — the catch's `when` filter never
        // matched, so the collision never reached a re-read at all.
        Assert.Equal([_requestId], repository.FindByRequestIdCalls);
    }

    /// <summary>RI4 — omitting requestId places a normal order, consulting no constraint and performing no lookup: the fake FAILS the test if <c>FindByRequestIdAsync</c> is called at all (B8).</summary>
    [Fact]
    public async Task RI4_OmittingRequestIdPlacesANormalOrder_ConsultingNoConstraintAndPerformingNoLookup()
    {
        var repository = new FailIfFindByRequestIdCalledRepository();
        var handler = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repository, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));

        var command = new PlaceOrderCommand(
            RequestId: null,
            OrderTestData.RetailerCode,
            OrderTestData.CompanyCode,
            OrderTestData.Currency,
            [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)],
            null,
            null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(OrderStatus.Placed, result.Status);
        var added = Assert.Single(repository.Added);
        Assert.Null(repository.RequestIdsByOrderId[added.Id]);
    }

    /// <summary>
    /// RI5, both directions in one case — a supplied requestId seeds
    /// `order.placed.v1`'s causationId EXACTLY (never re-derived); an
    /// omitted one mints a fresh, non-empty id, distinct across two
    /// placements (so a constant fallback cannot pass this).
    /// </summary>
    [Fact]
    public async Task RI5_SeedsTheCausationIdOfOrderPlacedFromTheSuppliedRequestId_AndMintsAFreshOneWhenItIsOmitted()
    {
        var repositoryWithRequestId = new FakeOrderRepository();
        var handlerWithRequestId = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repositoryWithRequestId, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));
        await handlerWithRequestId.HandleAsync(
            new PlaceOrderCommand(_requestId, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency, [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null),
            CancellationToken.None);

        var placedWithRequestId = Assert.Single(repositoryWithRequestId.Added);
        var eventWithRequestId = (OrderPlaced)placedWithRequestId.DomainEvents[0];
        Assert.Equal(_requestId, eventWithRequestId.CausationId.Value);

        var repositoryOmitted = new FakeOrderRepository();
        var handlerOmitted = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repositoryOmitted, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));
        await handlerOmitted.HandleAsync(
            new PlaceOrderCommand(null, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency, [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null),
            CancellationToken.None);
        var repositoryOmittedAgain = new FakeOrderRepository();
        var handlerOmittedAgain = new PlaceOrderCommandHandler(new FakeUnitOfWork(), repositoryOmittedAgain, new FakeOrderNumberAllocator(), CatalogWithOneProduct(), new FakeStockAvailabilityChecker(new StockAvailabilityResult(true, [])), new FakeClock(OrderTestData.Now));
        await handlerOmittedAgain.HandleAsync(
            new PlaceOrderCommand(null, OrderTestData.RetailerCode, OrderTestData.CompanyCode, OrderTestData.Currency, [new PlaceOrderRequestLine("PROD-001", new Quantity(1), 1_000, null)], null, null),
            CancellationToken.None);

        var placedOmitted = Assert.Single(repositoryOmitted.Added);
        var eventOmitted = (OrderPlaced)placedOmitted.DomainEvents[0];
        var placedOmittedAgain = Assert.Single(repositoryOmittedAgain.Added);
        var eventOmittedAgain = (OrderPlaced)placedOmittedAgain.DomainEvents[0];

        Assert.NotEqual(default, eventOmitted.CausationId.Value);
        Assert.NotEqual(_requestId, eventOmitted.CausationId.Value);
        // Two independent placements with requestId omitted mint DIFFERENT
        // causation ids — the arming case for the transposed-branches
        // family: a fallback that always returns the SAME constant would
        // pass "non-empty" but fail this.
        Assert.NotEqual(eventOmitted.CausationId, eventOmittedAgain.CausationId);
    }

    private static FakeOrderReferenceCatalog CatalogWithOneProduct()
    {
        var catalog = new FakeOrderReferenceCatalog();
        catalog.Retailers[OrderTestData.RetailerCode] = new PartyReference(OrderTestData.RetailerCode, OrderTestData.BuyerGln);
        catalog.Companies[OrderTestData.CompanyCode] = new PartyReference(OrderTestData.CompanyCode, OrderTestData.SupplierGln);
        catalog.Currencies.Add(OrderTestData.Currency);
        catalog.Products["PROD-001"] = new ProductReference("PROD-001", "First product", new Money(1_000, OrderTestData.Currency));
        return catalog;
    }

    private static DbUpdateException RequestIdIndexCollision() =>
        new(
            "duplicate key",
            SqlExceptionFactory.WithNumber(
                2601,
                "Cannot insert duplicate key row in object 'dbo.orders' with unique index 'uq_orders_request_id'. " +
                "The duplicate key value is (11111111-1111-1111-1111-111111111111)."));

    private static DbUpdateException OrderReferenceIndexCollision() =>
        new(
            "duplicate key",
            SqlExceptionFactory.WithNumber(
                2601,
                "Cannot insert duplicate key row in object 'dbo.orders' with unique index 'uq_orders_order_reference'. " +
                "The duplicate key value is (ORD-000001)."));

    /// <summary>RI4 (B8) — fails the test outright if <c>FindByRequestIdAsync</c> is ever called, rather than merely recording the call for a later assertion.</summary>
    private sealed class FailIfFindByRequestIdCalledRepository : IOrderRepository
    {
        public List<Order> Added { get; } = [];

        public Dictionary<UniqueId, Guid?> RequestIdsByOrderId { get; } = [];

        public Task AddAsync(Order order, Guid? requestId, CancellationToken cancellationToken)
        {
            Added.Add(order);
            RequestIdsByOrderId[order.Id] = requestId;
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.Id == id));

        public Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken) => Task.FromResult<Order?>(Added.SingleOrDefault(o => o.OrderReference == reference));

        public Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("RI4: omitting requestId must perform no FindByRequestIdAsync lookup.");

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
