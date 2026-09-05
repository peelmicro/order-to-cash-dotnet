using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// design.md §5.3, against fake <see cref="IUnitOfWork"/>/<see cref="IStockItemRepository"/>/<see cref="IClock"/>
/// — no database, no dispatcher. `FS5` is #7's rejected defect (D1),
/// reproduced deliberately and guarded here (C7's arming target).
/// </summary>
public sealed class StockReservationHandlerTests
{
    /// <summary>
    /// A status-filtering short-circuit (<c>&amp;&amp; status == "reserved"</c>)
    /// would happily reserve here, because the seeded item has plenty of
    /// available units — that is exactly the point: the ONLY thing that
    /// should stop a reserve is the presence of ANY existing reservation row
    /// for the order, whatever its status.
    /// </summary>
    [Theory]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Consumed)]
    public async Task FS5_ShortCircuitsToAlreadyReserved_OnAReservationInAnyStatus_CallingNoDomainFunctionAndSavingNothing(ReservationStatus existingStatus)
    {
        var orderReference = new OrderNumber(1);
        var existing = new ReservationSnapshot(UniqueId.New(), orderReference, "ACME", "RETAILER1", "P1", 2, existingStatus);
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0); // ample availability
        var repository = new FakeStockItemRepository
        {
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, [existing]),
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReservationService(unitOfWork, repository, new FakeClock());

        var command = new ReserveStockCommand(orderReference.Value, "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 2)], UniqueId.New(), UniqueId.New());

        var reply = await service.ReserveAsync(command, CancellationToken.None);

        Assert.Equal("already_reserved", reply.Outcome);
        var reservation = Assert.Single(reply.Reservations!);
        Assert.Equal(existing.Id.Value, reservation.ReservationId);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Equal(0, item.ReservedUnits);
        Assert.Empty(item.Reservations);
        Assert.Empty(item.DomainEvents);
    }

    [Fact]
    public async Task ReserveAsync_ARollbackPropagates_AndProducesNoReply()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var repository = new FakeStockItemRepository
        {
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, []),
            OnSaveChanges = () => throw new InvalidOperationException("simulated commit failure"),
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReservationService(unitOfWork, repository, new FakeClock());

        var command = new ReserveStockCommand("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 2)], UniqueId.New(), UniqueId.New());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReserveAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseAsync_TheReleasePreReadBeingEmpty_MeansExecuteAsyncIsNeverCalled()
    {
        var repository = new FakeStockItemRepository { ProductCodesLookup = null };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReservationService(unitOfWork, repository, new FakeClock());

        var reply = await service.ReleaseAsync(new ReleaseStockCommand("ORD-000001", "order_cancelled", UniqueId.New(), UniqueId.New()), CancellationToken.None);

        Assert.Equal("already_released", reply.Outcome);
        Assert.Empty(reply.Released!);
        Assert.Equal(0, unitOfWork.ExecuteCount);
    }

    [Fact]
    public async Task ReserveAsync_TheReplyIsReturnedOnlyAfterExecuteAsyncResolves()
    {
        var item = ReservationTests.BuildItem("ACME", "P1", units: 10, reservedUnits: 0);
        var repository = new FakeStockItemRepository
        {
            LockResult = new StockLockResult(new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase) { ["P1"] = item }, []),
        };
        var unitOfWork = new FakeUnitOfWork();
        var service = new StockReservationService(unitOfWork, repository, new FakeClock());

        var command = new ReserveStockCommand("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 2)], UniqueId.New(), UniqueId.New());
        var reply = await service.ReserveAsync(command, CancellationToken.None);

        Assert.Equal("accepted", reply.Outcome);
        Assert.Equal(1, unitOfWork.ExecuteCount);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }
}
