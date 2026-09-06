using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Facts;
using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI2`'s entry observation (a bus that was never called, never a rolled-
/// back residue — #7's `N10`) and `BI25`'s validator half (the discount
/// cross-field check sums inside a `checked` region).
/// </summary>
public sealed class InvoiceResponderValidationTests
{
    public enum RequestFailure
    {
        MissingCorrelationId,
        MalformedRequestId,
        EmptyLines,
        NegativeUnitPrice,
        MalformedOrderReference,
        BadCurrency,
        DiscountExceedsSum,
    }

    [Theory]
    [InlineData(RequestFailure.MissingCorrelationId)]
    [InlineData(RequestFailure.MalformedRequestId)]
    [InlineData(RequestFailure.EmptyLines)]
    [InlineData(RequestFailure.NegativeUnitPrice)]
    [InlineData(RequestFailure.MalformedOrderReference)]
    [InlineData(RequestFailure.BadCurrency)]
    [InlineData(RequestFailure.DiscountExceedsSum)]
    public async Task BI2_RefusesAMalformedIssueRequestWithValidationFailed_WithoutEverCallingTheDispatcher(RequestFailure failure)
    {
        var dispatcher = new RecordingDispatcher();
        var headers = BuildHeaders(failure);
        var payload = BuildPayload(failure);
        var message = BuildMessage(payload, headers);

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingRpcResponder.DispatchAsync(InvoiceSubjects.InvoiceIssue, message, dispatcher, CancellationToken.None));

        Assert.False(dispatcher.WasCalled, "the dispatcher must never be called when validation fails.");
    }

    /// <summary>`BI25` — a wrapped sum must be REFUSED, not accepted. Two lines near <see cref="long.MaxValue"/> would wrap to a small/negative sum under unchecked arithmetic, which would wrongly pass `discount ≤ sum`.</summary>
    [Fact]
    public void BI25_TheDiscountCrossFieldCheckSumsInsideACheckedRegion_SoALineTotalThatWouldWrapIsRefusedRatherThanAccepted()
    {
        var huge = long.MaxValue / 2 + 1;
        var request = new InvoiceIssueRequestPayload(
            "ORD-000001",
            "CarrefourEs",
            "IBERFOODS",
            "EUR",
            [new InvoiceLine("SKU-1", 1, huge), new InvoiceLine("SKU-2", 1, huge)],
            Discount: 1);

        var error = Assert.Throws<InvalidInvoiceRequestError>(() => InvoiceRequestValidator.ValidateIssue(request));
        Assert.Contains("overflow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static NatsHeaders? BuildHeaders(RequestFailure failure) => failure switch
    {
        RequestFailure.MissingCorrelationId => null,
        RequestFailure.MalformedRequestId => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() }, { "x-request-id", "not-a-guid" } },
        _ => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() }, { "x-request-id", Guid.NewGuid().ToString() } },
    };

    private static byte[] BuildPayload(RequestFailure failure) => failure switch
    {
        RequestFailure.EmptyLines => RpcJson.Serialize(new InvoiceIssueRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", [])),
        RequestFailure.NegativeUnitPrice => RpcJson.Serialize(new InvoiceIssueRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", [new InvoiceLine("SKU-1", 1, -1)])),
        RequestFailure.MalformedOrderReference => RpcJson.Serialize(new InvoiceIssueRequestPayload("not-an-order", "CarrefourEs", "IBERFOODS", "EUR", [new InvoiceLine("SKU-1", 1, 1_000)])),
        RequestFailure.BadCurrency => RpcJson.Serialize(new InvoiceIssueRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "eur", [new InvoiceLine("SKU-1", 1, 1_000)])),
        RequestFailure.DiscountExceedsSum => RpcJson.Serialize(new InvoiceIssueRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", [new InvoiceLine("SKU-1", 1, 1_000)], Discount: 5_000)),
        _ => RpcJson.Serialize(new InvoiceIssueRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", [new InvoiceLine("SKU-1", 1, 1_000)])),
    };

    private static NatsMsg<byte[]> BuildMessage(byte[] data, NatsHeaders? headers) =>
        new(InvoiceSubjects.InvoiceIssue, "reply", data.Length, headers!, data, null!, default);

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

            if (typeof(TResult) == typeof(InvoiceListReplyPayload))
            {
                return Task.FromResult((TResult)(object)new InvoiceListReplyPayload([], new InvoicePageInfo(1, 25, 0)));
            }

            return Task.FromResult<TResult>(default!);
        }

        public Task PublishAsync(object @event, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }
}
