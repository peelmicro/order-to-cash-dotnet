using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// The feature's own named risk, proved at the level the risk actually
/// lives at: a REAL NATS broker, never a mocked <see cref="INatsConnection"/>
/// — "the check is an end-to-end call through a real broker against the
/// real responders — not two green suites". A stand-in responder plays the
/// "real responder" role for the cases a genuine no-responder/timeout
/// scenario cannot be staged against a live service deterministically;
/// <see cref="FulfillmentStockEndToEndTests"/> covers the ACTUAL responder
/// half of the same risk.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class NatsRpcClientIntegrationTests(NatsContainerFixture nats)
{
    private sealed record Ping(string Text);

    private sealed record Pong(string Echo);

    private static NatsRpcClient BuildClient(INatsConnection connection, int timeoutMs = 2000) =>
        new(connection, Options.Create(new NatsOptions { DefaultTimeoutMs = timeoutMs }));

    [Fact]
    public async Task CallAsync_DecodesTheSuccessReply_AndSendsTheCorrelationAndRequestIdHeaders()
    {
        const string subject = "gateway.it.echo";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url, subject, data => RpcJson.Serialize(new Pong($"echo:{RpcJson.Deserialize<Ping>(data).Text}")), CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection);
        var correlationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var reply = await client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(correlationId, requestId), CancellationToken.None);

        Assert.Equal("echo:hello", reply.Echo);
        Assert.NotNull(responder.LastRequestHeaders);
        Assert.Equal(correlationId.ToString(), responder.LastRequestHeaders!["x-correlation-id"].ToString());
        Assert.Equal(requestId.ToString(), responder.LastRequestHeaders!["x-request-id"].ToString());
    }

    /// <summary>No responder subscribed at all — the IMMEDIATE transport refusal, never waiting out the deadline.</summary>
    [Fact]
    public async Task CallAsync_Throws_RpcTransportError_WhenNoResponderIsSubscribed()
    {
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 500);

        var error = await Assert.ThrowsAsync<RpcTransportError>(
            () => client.CallAsync<Ping, Pong>("gateway.it.no-responder", new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("UNAVAILABLE", error.Code);
    }

    /// <summary>A responder IS subscribed but never answers — the caller's own deadline elapses, a DIFFERENT failure from "no responder at all".</summary>
    [Fact]
    public async Task CallAsync_Throws_RpcTimeoutError_WhenAResponderIsSubscribedButNeverReplies()
    {
        const string subject = "gateway.it.silent";
        await using var responder = await StandInResponder.StartSilentAsync(nats.Url, subject, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 300);

        var error = await Assert.ThrowsAsync<RpcTimeoutError>(
            () => client.CallAsync<Ping, Pong>(subject, new Ping("hello"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("TIMEOUT", error.Code);
    }

    /// <summary>The two transient failures are DIFFERENT RpcCallError subtypes — the exact distinction the acceptance bullet names ("guard the mapping per case, not that an error becomes an error").</summary>
    [Fact]
    public async Task CallAsync_NoResponderAndSilentResponder_ThrowDifferentErrorTypes()
    {
        const string silentSubject = "gateway.it.silent-vs-unavailable";
        await using var responder = await StandInResponder.StartSilentAsync(nats.Url, silentSubject, CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection, timeoutMs: 300);

        var noResponderError = await Assert.ThrowsAsync<RpcTransportError>(
            () => client.CallAsync<Ping, Pong>("gateway.it.genuinely-nothing-here", new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
        var silentError = await Assert.ThrowsAsync<RpcTimeoutError>(
            () => client.CallAsync<Ping, Pong>(silentSubject, new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.NotEqual(noResponderError.Code, silentError.Code);
    }

    [Fact]
    public async Task CallAsync_Throws_RpcBusinessError_CarryingTheResponderAsSentCodeMessageAndDetails()
    {
        const string subject = "gateway.it.business-error";
        await using var responder = await StandInResponder.StartAsync(
            nats.Url,
            subject,
            _ => RpcJson.Serialize(new RpcErrorPayload("PRECONDITION_FAILED", "invoice already paid", new Dictionary<string, object?> { ["code"] = "INVOICE_ALREADY_PAID" })),
            CancellationToken.None);
        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var client = BuildClient(connection);

        var error = await Assert.ThrowsAsync<RpcBusinessError>(
            () => client.CallAsync<Ping, Pong>(subject, new Ping("x"), new RpcCallMeta(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("PRECONDITION_FAILED", error.Code);
        Assert.Equal("invoice already paid", error.Message);
        Assert.NotNull(error.Details);
        // The exact normalisation this class exists for: a JsonElement
        // scalar unwrapped to a native string, not left boxed as
        // JsonElement (which RpcErrorClassifier's `is string` check would
        // silently never match).
        Assert.IsType<string>(error.Details!["code"]);
        Assert.Equal("INVOICE_ALREADY_PAID", error.Details!["code"]);
    }
}
