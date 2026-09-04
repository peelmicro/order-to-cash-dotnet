using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §5.5 — <c>SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse</c>:
/// all five event-to-signal mappings, against a recording <see cref="ISagaCommandSignal"/>.
/// </summary>
public sealed class OrderSagasTests
{
    [Fact]
    public async Task SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse()
    {
        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await AssertSignalsExactlyOne(new OrderPlacedFactRecorded(orderId, correlationId), SagaCommandKind.StockReserve, signal => new OrderPlacedFactRecordedHandler(signal));
        await AssertSignalsExactlyOne(new OrderMarkedStockReserved(orderId, correlationId), SagaCommandKind.CreditHold, signal => new OrderMarkedStockReservedHandler(signal));
        await AssertSignalsExactlyOne(new CreditRejectionRecorded(orderId, correlationId), SagaCommandKind.StockRelease, signal => new CreditRejectionRecordedHandler(signal));
        await AssertSignalsExactlyOne(new OrderConfirmedBySaga(orderId, correlationId), SagaCommandKind.DespatchCreate, signal => new OrderConfirmedBySagaHandler(signal));
        await AssertSignalsExactlyOne(new OrderMarkedDespatched(orderId, correlationId), SagaCommandKind.InvoiceIssue, signal => new OrderMarkedDespatchedHandler(signal));

        async Task AssertSignalsExactlyOne<TEvent>(TEvent @event, SagaCommandKind expectedCommand, Func<RecordingSagaCommandSignal, OrderToCash.Cqrs.IEventHandler<TEvent>> buildHandler)
        {
            var signal = new RecordingSagaCommandSignal();
            var handler = buildHandler(signal);

            await handler.HandleAsync(@event, CancellationToken.None);

            var signalled = Assert.Single(signal.Signalled);
            Assert.Equal(orderId, signalled.OrderId);
            Assert.Equal(expectedCommand, signalled.Command);
        }
    }

    private sealed class RecordingSagaCommandSignal : ISagaCommandSignal
    {
        public List<SagaCommandRef> Signalled { get; } = [];

        public void Signal(SagaCommandRef commandRef) => Signalled.Add(commandRef);
    }
}
