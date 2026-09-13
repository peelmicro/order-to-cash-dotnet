using NATS.Client.Core;
using OrderToCash.Billing.Application.Queries;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC1` — a missing or malformed correlation/request header on `billing.credit.hold`/`billing.credit.release` replies <c>VALIDATION_FAILED</c> and dispatches NOTHING; `billing.credit.list` requires neither header.</summary>
public sealed class CreditResponderHeaderTests
{
    [Theory]
    [InlineData(CreditSubjects.CreditHold, HeaderCase.Missing)]
    [InlineData(CreditSubjects.CreditHold, HeaderCase.Malformed)]
    [InlineData(CreditSubjects.CreditHold, HeaderCase.MissingRequestIdOnly)]
    [InlineData(CreditSubjects.CreditHold, HeaderCase.MalformedRequestIdOnly)]
    [InlineData(CreditSubjects.CreditRelease, HeaderCase.Missing)]
    [InlineData(CreditSubjects.CreditRelease, HeaderCase.Malformed)]
    [InlineData(CreditSubjects.CreditRelease, HeaderCase.MissingRequestIdOnly)]
    [InlineData(CreditSubjects.CreditRelease, HeaderCase.MalformedRequestIdOnly)]
    public async Task BC1_RepliesValidationFailedAndDispatchesNothing_WhenACorrelationOrRequestHeaderIsMissingOrMalformed(string subject, HeaderCase headerCase)
    {
        var dispatcher = new RecordingDispatcher();
        var headers = BuildHeaders(headerCase);
        var payload = BuildPayload(subject);
        var message = BuildMessage(subject, payload, headers);

        var error = await Assert.ThrowsAsync<InvalidCreditRequestError>(
            () => BillingRpcResponder.DispatchAsync(subject, message, dispatcher, CancellationToken.None));

        Assert.False(dispatcher.WasCalled, "the dispatcher must never be called when the header check fails.");

        // A missing/malformed x-correlation-id names ITS OWN header; a
        // present-but-missing OR present-but-malformed x-request-id must
        // name THAT header — a test that only ever asserted
        // "x-correlation-id" would pass even if the responder silently
        // tolerated an absent or malformed x-request-id (D4: a malformed
        // request id was tolerated on the theory's own terms because no
        // case ever malformed it).
        var expectedHeaderName = headerCase is HeaderCase.MissingRequestIdOnly or HeaderCase.MalformedRequestIdOnly ? "x-request-id" : "x-correlation-id";
        Assert.Contains(expectedHeaderName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BillingCreditList_SucceedsWithNoHeaders()
    {
        var dispatcher = new RecordingDispatcher();
        var payload = RpcJson.Serialize(new CreditListRequestPayload(null, null));
        var message = BuildMessage(CreditSubjects.CreditList, payload, headers: null);

        var reply = await BillingRpcResponder.DispatchAsync(CreditSubjects.CreditList, message, dispatcher, CancellationToken.None);

        Assert.True(dispatcher.WasCalled);
        Assert.NotEmpty(reply);
    }

    public enum HeaderCase
    {
        Missing,
        Malformed,
        MissingRequestIdOnly,
        MalformedRequestIdOnly,
    }

    private static NatsHeaders? BuildHeaders(HeaderCase headerCase) => headerCase switch
    {
        HeaderCase.Missing => null,
        HeaderCase.Malformed => new NatsHeaders { { "x-correlation-id", "not-a-guid" }, { "x-request-id", Guid.NewGuid().ToString() } },
        HeaderCase.MissingRequestIdOnly => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() } },
        HeaderCase.MalformedRequestIdOnly => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() }, { "x-request-id", "not-a-guid" } },
        _ => throw new ArgumentOutOfRangeException(nameof(headerCase)),
    };

    private static byte[] BuildPayload(string subject) => subject switch
    {
        CreditSubjects.CreditHold => RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR"))),
        CreditSubjects.CreditRelease => RpcJson.Serialize(new CreditReleaseRequestPayload("ORD-000001", "CarrefourEs", "IBERFOODS")),
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

            if (typeof(TResult) == typeof(CreditListReplyPayload))
            {
                return Task.FromResult((TResult)(object)new CreditListReplyPayload([], new CreditPageInfo(1, 25, 0)));
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
