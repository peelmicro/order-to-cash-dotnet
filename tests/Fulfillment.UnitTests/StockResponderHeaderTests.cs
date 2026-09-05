using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`FS3` — a missing or malformed correlation/request header on `stock.reserve`/`stock.release` replies <c>VALIDATION_FAILED</c> and dispatches NOTHING.</summary>
public sealed class StockResponderHeaderTests
{
    [Theory]
    [InlineData(StockSubjects.StockReserve, HeaderCase.Missing)]
    [InlineData(StockSubjects.StockReserve, HeaderCase.Malformed)]
    [InlineData(StockSubjects.StockRelease, HeaderCase.Missing)]
    [InlineData(StockSubjects.StockRelease, HeaderCase.Malformed)]
    [InlineData(StockSubjects.DespatchCreate, HeaderCase.Missing)]
    [InlineData(StockSubjects.DespatchCreate, HeaderCase.Malformed)]
    public async Task FS3_RepliesValidationFailedAndDispatchesNothing_WhenACorrelationOrRequestHeaderIsMissingOrMalformed(string subject, HeaderCase headerCase)
    {
        var dispatcher = new RecordingDispatcher();
        var headers = BuildHeaders(headerCase);
        var payload = BuildPayload(subject);
        var message = BuildMessage(subject, payload, headers);

        var error = await Assert.ThrowsAsync<InvalidStockRequestError>(
            () => StockRpcResponder.DispatchAsync(subject, message, dispatcher, CancellationToken.None));

        Assert.False(dispatcher.WasCalled, "the dispatcher must never be called when the header check fails.");
        Assert.Contains("x-correlation-id", error.Message, StringComparison.Ordinal);
    }

    public enum HeaderCase
    {
        Missing,
        Malformed,
    }

    private static NatsHeaders? BuildHeaders(HeaderCase headerCase) => headerCase switch
    {
        HeaderCase.Missing => null,
        HeaderCase.Malformed => new NatsHeaders { { "x-correlation-id", "not-a-guid" }, { "x-request-id", Guid.NewGuid().ToString() } },
        _ => throw new ArgumentOutOfRangeException(nameof(headerCase)),
    };

    private static byte[] BuildPayload(string subject) => subject switch
    {
        StockSubjects.StockReserve => RpcJson.Serialize(new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 1)])),
        StockSubjects.StockRelease => RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000001", "order_cancelled")),
        StockSubjects.DespatchCreate => RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000001")),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static NatsMsg<byte[]> BuildMessage(string subject, byte[] data, NatsHeaders? headers) =>
        new(subject, "reply", data.Length, headers!, data, null!, default);

    private sealed class RecordingDispatcher : IDispatcher
    {
        public bool WasCalled { get; private set; }

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand
        {
            WasCalled = true;
            return Task.CompletedTask;
        }

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult>
        {
            WasCalled = true;
            return Task.FromResult<TResult>(default!);
        }

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult>
        {
            WasCalled = true;
            return Task.FromResult<TResult>(default!);
        }

        public Task PublishAsync(object @event, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }
}
