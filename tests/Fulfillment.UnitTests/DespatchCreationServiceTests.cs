using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>Against fake <see cref="IUnitOfWork"/>/<see cref="IStockItemRepository"/>/<see cref="IDespatchRepository"/>/<see cref="IDespatchNumberAllocator"/>/<see cref="IClock"/> — no database, no dispatcher.</summary>
public sealed class DespatchCreationServiceTests
{
    [Fact]
    public async Task F8_FastPath_ReturnsTheExistingDespatchWithCreatedFalse_OpeningNoTransactionAndAllocatingNoNumber()
    {
        var orderReference = new OrderNumber(1);
        var despatchDate = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
        var despatchRepository = new FakeDespatchRepository
        {
            ExistingSnapshot = new DespatchSnapshot(
                UniqueId.New(), "DES-000001", despatchDate, orderReference, "ACME", "RETAILER1",
                [new DespatchLineEntry("P1", new Quantity(3))]),
        };
        var stockRepository = new FakeStockItemRepository();
        var allocator = new FakeDespatchNumberAllocator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new DespatchCreationService(unitOfWork, stockRepository, despatchRepository, allocator, new FakeClock());

        var reply = await service.CreateAsync(new CreateDespatchCommand(orderReference.Value, UniqueId.New(), UniqueId.New()), CancellationToken.None);

        Assert.False(reply.Created);
        Assert.Equal("DES-000001", reply.DespatchReference);
        Assert.Equal(despatchDate, reply.DespatchDate);
        Assert.Single(reply.Lines!);
        Assert.Equal(0, unitOfWork.ExecuteCount);
        Assert.Equal(0, allocator.CallCount);
        Assert.Equal(0, despatchRepository.SaveCallCount);
    }

    [Fact]
    public async Task Precondition_NeverReserved_ThrowsBeforeAnyTransaction()
    {
        var despatchRepository = new FakeDespatchRepository();
        var stockRepository = new FakeStockItemRepository { ProductCodesLookup = null };
        var allocator = new FakeDespatchNumberAllocator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new DespatchCreationService(unitOfWork, stockRepository, despatchRepository, allocator, new FakeClock());

        var error = await Assert.ThrowsAsync<NoReservedStockForDespatchError>(
            () => service.CreateAsync(new CreateDespatchCommand("ORD-000001", UniqueId.New(), UniqueId.New()), CancellationToken.None));

        Assert.Equal("ORD-000001", error.OrderReference);
        Assert.Equal(0, unitOfWork.ExecuteCount);
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public async Task Precondition_EveryReservationAlreadyReleased_ThrowsInsideTheTransaction_AllocatingNoNumber()
    {
        var orderReference = new OrderNumber(1);
        var released = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Released);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0, released);
        var stockRepository = new FakeStockItemRepository
        {
            ProductCodesLookup = new OrderReservationLookup("ACME", ["P1"]),
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, [released]),
        };
        var despatchRepository = new FakeDespatchRepository();
        var allocator = new FakeDespatchNumberAllocator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new DespatchCreationService(unitOfWork, stockRepository, despatchRepository, allocator, new FakeClock());

        var error = await Assert.ThrowsAsync<NoReservedStockForDespatchError>(
            () => service.CreateAsync(new CreateDespatchCommand(orderReference.Value, UniqueId.New(), UniqueId.New()), CancellationToken.None));

        Assert.Equal(orderReference.Value, error.OrderReference);
        Assert.Equal(1, unitOfWork.ExecuteCount);
        Assert.Equal(0, allocator.CallCount);
        Assert.Equal(0, despatchRepository.SaveCallCount);
    }

    [Fact]
    public async Task HappyPath_ConsumesTheReservationCreatesTheDespatchAndReturnsCreatedTrue()
    {
        var orderReference = new OrderNumber(1);
        var reserved = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Reserved);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 3, reserved);
        var stockRepository = new FakeStockItemRepository
        {
            ProductCodesLookup = new OrderReservationLookup("ACME", ["P1"]),
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, [reserved]),
        };
        var despatchRepository = new FakeDespatchRepository();
        var allocator = new FakeDespatchNumberAllocator { NextReference = "DES-000007" };
        var unitOfWork = new FakeUnitOfWork();
        var service = new DespatchCreationService(unitOfWork, stockRepository, despatchRepository, allocator, new FakeClock());

        var reply = await service.CreateAsync(new CreateDespatchCommand(orderReference.Value, UniqueId.New(), UniqueId.New()), CancellationToken.None);

        Assert.True(reply.Created);
        Assert.Equal("DES-000007", reply.DespatchReference);
        Assert.Single(reply.Lines!);
        Assert.Equal(ReservationStatus.Consumed, Assert.Single(item.Reservations).Status);
        Assert.Equal(1, stockRepository.SaveChangesCallCount);
        Assert.Equal(1, despatchRepository.SaveCallCount);
        Assert.Equal(1, allocator.CallCount);
        Assert.NotNull(despatchRepository.Saved);
        Assert.Single(despatchRepository.Saved!.DomainEvents);
    }

    [Fact]
    public async Task F8_InFlightRace_AConcurrentCommitterAlreadyCreatedTheDespatch_ReturnsTheExistingOneWithCreatedFalse_AllocatingNoSecondNumber()
    {
        var orderReference = new OrderNumber(1);
        var consumed = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 3, ReservationStatus.Consumed);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 7, reservedUnits: 0, consumed);
        var stockRepository = new FakeStockItemRepository
        {
            ProductCodesLookup = new OrderReservationLookup("ACME", ["P1"]),
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, [consumed]),
        };
        var despatchDate = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);
        var raced = new DespatchSnapshot(UniqueId.New(), "DES-000002", despatchDate, orderReference, "ACME", "RETAILER1", [new DespatchLineEntry("P1", new Quantity(3))]);
        var despatchRepository = new FakeDespatchRepository { FindByCallIndex = callIndex => callIndex == 1 ? null : raced };
        var allocator = new FakeDespatchNumberAllocator();
        var unitOfWork = new FakeUnitOfWork();
        var service = new DespatchCreationService(unitOfWork, stockRepository, despatchRepository, allocator, new FakeClock());

        var reply = await service.CreateAsync(new CreateDespatchCommand(orderReference.Value, UniqueId.New(), UniqueId.New()), CancellationToken.None);

        Assert.False(reply.Created);
        Assert.Equal("DES-000002", reply.DespatchReference);
        Assert.Equal(1, unitOfWork.ExecuteCount);
        Assert.Equal(0, allocator.CallCount);
        Assert.Equal(0, despatchRepository.SaveCallCount);
    }
}
