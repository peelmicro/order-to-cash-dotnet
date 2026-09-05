using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature 46 (`orders_stock_check_rpc_error_discriminator`): over the REAL
/// transport — a real NATS broker and a real stand-in Fulfillment responder
/// answering raw <c>RpcError</c> bytes, never a mocked <see cref="INatsConnection"/>
/// (<see cref="NatsStockAvailabilityChecker"/>'s own class remark). Proves
/// the discriminator directly, at the level the bug lives at, before
/// <c>OrdersCreateAcceptanceTests</c>' end-to-end counterpart proves what
/// <c>orders.create</c> answers the caller with.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class NatsStockAvailabilityCheckerTests(NatsContainerFixture nats)
{
    /// <summary>
    /// The exact bug fixed: before the discriminator, an RpcError-shaped
    /// reply deserialised into <c>StockCheckReplyPayload</c> with a null
    /// <c>Lines</c>, and the very next line's LINQ <c>.Select</c> threw a
    /// bare <see cref="NullReferenceException"/>. Armed by deleting the
    /// discriminator — see progress/impl report.
    /// </summary>
    [Theory]
    [InlineData("NOT_FOUND", "product ZZZ is not known to Fulfillment")]
    [InlineData("PRECONDITION_FAILED", "stock item is locked for replenishment")]
    public async Task AnRpcErrorReply_ThrowsStockCheckBusinessErrorCarryingTheRespondersCodeAndMessage_NeverANullReferenceException(string responderCode, string responderMessage)
    {
        await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartErrorAsync(nats.Url, responderCode, responderMessage, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        var lines = new[] { new StockAvailabilityLine("P1", new Quantity(1)) };

        var error = await Assert.ThrowsAsync<StockCheckBusinessError>(
            () => checker.CheckAsync("ACME", lines, CancellationToken.None));

        Assert.Equal(RpcSubjects.StockCheck, error.Subject);
        Assert.Equal(responderCode, error.RpcErrorCode);
        Assert.Equal(responderMessage, error.ResponderMessage);
    }

    /// <summary>
    /// Advisory A1 (round 2): a reply body that is not valid JSON at all —
    /// neither the <c>RpcError</c> shape nor <c>StockCheckReplyPayload</c> —
    /// must not reach the caller as a bare <see cref="JsonException"/>. #7's
    /// same seam guards this exact case (see the production fix's remark);
    /// this proves the containment, not just the discrimination.
    /// </summary>
    [Fact]
    public async Task AMalformedNonJsonReply_ThrowsStockCheckTransportError_NeverABareJsonException()
    {
        await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartMalformedAsync(nats.Url, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        var lines = new[] { new StockAvailabilityLine("P1", new Quantity(1)) };

        var error = await Assert.ThrowsAsync<StockCheckTransportError>(
            () => checker.CheckAsync("ACME", lines, CancellationToken.None));

        Assert.Equal(RpcSubjects.StockCheck, error.Subject);
    }
}
