using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `billing.payment.register`'s entry observation — a malformed header or
/// payload must never reach the dispatcher (#7's `N10` rule, `BI2`'s own
/// shape applied to this subject).
/// </summary>
public sealed class PaymentRegisterResponderValidationTests
{
    public enum RequestFailure
    {
        MissingCorrelationId,
        MalformedRequestId,
        NeitherInvoiceIdentifierSupplied,
        MalformedInvoiceReference,
        MissingAmount,
        NegativeAmount,
        BadCurrency,
        BadSource,
        EmptyPaymentReference,
    }

    [Theory]
    [InlineData(RequestFailure.MissingCorrelationId)]
    [InlineData(RequestFailure.MalformedRequestId)]
    [InlineData(RequestFailure.NeitherInvoiceIdentifierSupplied)]
    [InlineData(RequestFailure.MalformedInvoiceReference)]
    [InlineData(RequestFailure.MissingAmount)]
    [InlineData(RequestFailure.NegativeAmount)]
    [InlineData(RequestFailure.BadCurrency)]
    [InlineData(RequestFailure.BadSource)]
    [InlineData(RequestFailure.EmptyPaymentReference)]
    public async Task RefusesAMalformedRegisterRequestWithValidationFailed_WithoutEverCallingTheDispatcher(RequestFailure failure)
    {
        var dispatcher = new RecordingDispatcher();
        var headers = BuildHeaders(failure);
        var payload = BuildPayload(failure);
        var message = BuildMessage(payload, headers);

        await Assert.ThrowsAnyAsync<Exception>(
            () => BillingRpcResponder.DispatchAsync(InvoiceSubjects.PaymentRegister, message, dispatcher, CancellationToken.None));

        Assert.False(dispatcher.WasCalled, "the dispatcher must never be called when validation fails.");
    }

    /// <summary>The cross-field rule itself, at the validator level — belt-and-braces alongside the responder-entry theory above.</summary>
    [Fact]
    public void ValidateRegister_RefusesARequestNamingNeitherInvoiceIdNorInvoiceReference()
    {
        var request = new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test");

        var error = Assert.Throws<InvalidInvoiceRequestError>(() => PaymentRegisterRequestValidator.ValidateRegister(request));
        Assert.Contains("invoiceId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRegister_AcceptsAnInvoiceIdAlone_WithNoInvoiceReference()
    {
        var request = new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test", InvoiceId: Guid.NewGuid());

        var exception = Record.Exception(() => PaymentRegisterRequestValidator.ValidateRegister(request));
        Assert.Null(exception);
    }

    private static NatsHeaders? BuildHeaders(RequestFailure failure) => failure switch
    {
        RequestFailure.MissingCorrelationId => null,
        RequestFailure.MalformedRequestId => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() }, { "x-request-id", "not-a-guid" } },
        _ => new NatsHeaders { { "x-correlation-id", Guid.NewGuid().ToString() }, { "x-request-id", Guid.NewGuid().ToString() } },
    };

    private static byte[] BuildPayload(RequestFailure failure) => failure switch
    {
        RequestFailure.NeitherInvoiceIdentifierSupplied => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test")),
        RequestFailure.MalformedInvoiceReference => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test", InvoiceReference: "not-an-invoice")),
        RequestFailure.MissingAmount => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", null!, DateTimeOffset.UtcNow, "test", InvoiceReference: "INV-000001")),
        RequestFailure.NegativeAmount => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(-1, "EUR"), DateTimeOffset.UtcNow, "test", InvoiceReference: "INV-000001")),
        RequestFailure.BadCurrency => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "eur"), DateTimeOffset.UtcNow, "test", InvoiceReference: "INV-000001")),
        RequestFailure.BadSource => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "carrier-pigeon", InvoiceReference: "INV-000001")),
        RequestFailure.EmptyPaymentReference => RpcJson.Serialize(new PaymentRegisterRequestPayload("", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test", InvoiceReference: "INV-000001")),
        _ => RpcJson.Serialize(new PaymentRegisterRequestPayload("PAY-000001", new CreditMoney(5_000, "EUR"), DateTimeOffset.UtcNow, "test", InvoiceReference: "INV-000001")),
    };

    private static NatsMsg<byte[]> BuildMessage(byte[] data, NatsHeaders? headers) =>
        new(InvoiceSubjects.PaymentRegister, "reply", data.Length, headers!, data, null!, default);

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
