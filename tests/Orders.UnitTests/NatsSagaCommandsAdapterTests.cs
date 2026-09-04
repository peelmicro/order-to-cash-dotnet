using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §6.1 — the pre-42 error taxonomy (D2): timeout / no-responders /
/// <c>RpcError</c>-body classification, and that a business rejection
/// (<c>outcome: rejected</c>) resolves normally rather than throwing (SO6,
/// unit half). Against <see cref="NatsSagaCommandsAdapter.RawRequester"/>,
/// the narrow request-client seam this feature establishes — never a mocked
/// <see cref="INatsConnection"/>.
/// </summary>
public sealed class NatsSagaCommandsAdapterTests
{
    [Theory]
    [InlineData(RpcSubjectUnderTest.StockReserve)]
    [InlineData(RpcSubjectUnderTest.StockRelease)]
    [InlineData(RpcSubjectUnderTest.DespatchCreate)]
    [InlineData(RpcSubjectUnderTest.CreditHold)]
    [InlineData(RpcSubjectUnderTest.InvoiceIssue)]
    public async Task EachMethod_SendsOnItsOwnSubjectAndReturnsTheTypedReply(RpcSubjectUnderTest subjectUnderTest)
    {
        string? observedSubject = null;
        var adapter = BuildAdapter((subject, payload, opts, ct) =>
        {
            observedSubject = subject;
            return new ValueTask<NatsMsg<byte[]>>(BuildReply(SuccessBodyFor(subjectUnderTest)));
        });

        await InvokeAsync(adapter, subjectUnderTest);

        Assert.Equal(ExpectedSubject(subjectUnderTest), observedSubject);
    }

    [Fact]
    public async Task NatsNoRespondersException_MapsToSagaCommandTransportError()
    {
        var adapter = BuildAdapter((_, _, _, _) => throw new NatsNoRespondersException());

        var error = await Assert.ThrowsAsync<SagaCommandTransportError>(
            () => adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None));

        Assert.Equal(RpcSubjects.StockReserve, error.Subject);
    }

    [Fact]
    public async Task NatsNoReplyException_MapsToSagaCommandTimeoutError()
    {
        var adapter = BuildAdapter((_, _, _, _) => throw new NatsNoReplyException());

        var error = await Assert.ThrowsAsync<SagaCommandTimeoutError>(
            () => adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None));

        Assert.Equal(RpcSubjects.StockReserve, error.Subject);
    }

    [Fact]
    public async Task ANullDataReply_MapsToSagaCommandTimeoutError()
    {
        var adapter = BuildAdapter((_, _, _, _) => new ValueTask<NatsMsg<byte[]>>(new NatsMsg<byte[]>("reply", null!, 0, null!, null!, null!, default)));

        await Assert.ThrowsAsync<SagaCommandTimeoutError>(
            () => adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task AnRpcErrorReplyBody_MapsToSagaCommandTransportError()
    {
        // feature 42 splits this on `code`; this feature keeps every
        // RpcError body classified as one transport error (design.md §6.1, §12).
        var errorBody = JsonSerializer.SerializeToUtf8Bytes(new { code = "UNAVAILABLE", message = "fulfillment is down" });
        var adapter = BuildAdapter((_, _, _, _) => new ValueTask<NatsMsg<byte[]>>(BuildReply(errorBody)));

        var error = await Assert.ThrowsAsync<SagaCommandTransportError>(
            () => adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None));

        Assert.Contains("UNAVAILABLE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATypedReplyWithOutcomeRejected_IsReturnedNotThrown()
    {
        // SO6, unit half: a business rejection resolves normally.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { outcome = "rejected", orderReference = "ORD-000001", shortages = new object[0] });
        var adapter = BuildAdapter((_, _, _, _) => new ValueTask<NatsMsg<byte[]>>(BuildReply(body)));

        var reply = await adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None);

        Assert.Equal("rejected", reply.Outcome);
    }

    public enum RpcSubjectUnderTest
    {
        StockReserve,
        StockRelease,
        DespatchCreate,
        CreditHold,
        InvoiceIssue,
    }

    private static string ExpectedSubject(RpcSubjectUnderTest subject) => subject switch
    {
        RpcSubjectUnderTest.StockReserve => RpcSubjects.StockReserve,
        RpcSubjectUnderTest.StockRelease => RpcSubjects.StockRelease,
        RpcSubjectUnderTest.DespatchCreate => RpcSubjects.DespatchCreate,
        RpcSubjectUnderTest.CreditHold => RpcSubjects.CreditHold,
        RpcSubjectUnderTest.InvoiceIssue => RpcSubjects.InvoiceIssue,
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static Task InvokeAsync(NatsSagaCommandsAdapter adapter, RpcSubjectUnderTest subject) => subject switch
    {
        RpcSubjectUnderTest.StockReserve => adapter.ReserveStockAsync(SampleStockReserveRequest(), CancellationToken.None),
        RpcSubjectUnderTest.StockRelease => adapter.ReleaseStockAsync(new StockReleaseRequestPayload("ORD-000001", "credit_rejected"), CancellationToken.None),
        RpcSubjectUnderTest.DespatchCreate => adapter.CreateDespatchAsync(new DespatchCreateRequestPayload("ORD-000001"), CancellationToken.None),
        RpcSubjectUnderTest.CreditHold => adapter.HoldCreditAsync(new CreditHoldRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", new SagaMoney(1000, "EUR")), CancellationToken.None),
        RpcSubjectUnderTest.InvoiceIssue => adapter.IssueInvoiceAsync(new InvoiceIssueRequestPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", []), CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static byte[] SuccessBodyFor(RpcSubjectUnderTest subject) => subject switch
    {
        RpcSubjectUnderTest.StockReserve => JsonSerializer.SerializeToUtf8Bytes(new { outcome = "accepted", orderReference = "ORD-000001", reservations = new object[0] }),
        RpcSubjectUnderTest.StockRelease => JsonSerializer.SerializeToUtf8Bytes(new { outcome = "released", orderReference = "ORD-000001", released = new object[0] }),
        RpcSubjectUnderTest.DespatchCreate => JsonSerializer.SerializeToUtf8Bytes(new { orderReference = "ORD-000001", despatchReference = "DES-000001", despatchDate = DateTimeOffset.UtcNow, created = true, lines = new object[0] }),
        RpcSubjectUnderTest.CreditHold => JsonSerializer.SerializeToUtf8Bytes(new { outcome = "approved", orderReference = "ORD-000001", currency = "EUR", availableCredit = 5000 }),
        RpcSubjectUnderTest.InvoiceIssue => JsonSerializer.SerializeToUtf8Bytes(new { orderReference = "ORD-000001", invoiceReference = "INV-000001", invoiceDate = DateTimeOffset.UtcNow, currency = "EUR", totalAmount = 1000, status = "issued", created = true }),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static StockReserveRequestPayload SampleStockReserveRequest() =>
        new("ORD-000001", "RETAILER1", "COMPANY1", [new StockReserveRequestLine("SKU-1", 1)]);

    private static NatsMsg<byte[]> BuildReply(byte[] data) => new("reply", null!, data.Length, null!, data, null!, default);

    private static NatsSagaCommandsAdapter BuildAdapter(NatsSagaCommandsAdapter.RawRequester requester) =>
        new(requester, Options.Create(new OrdersSagaOptions()));
}
