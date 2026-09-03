using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>
/// The outbound <c>fulfillment.stock.check</c> RPC client — real NATS core
/// request-reply (<c>asyncapi.yaml</c> <c>servers.rpcTransport</c>), never a
/// mocked transport: the transport itself is the thing this feature proves
/// (this feature's own scope note on how it verifies the client honestly —
/// a real broker, a stand-in responder in tests, never a mocked
/// <c>INatsConnection</c>). A non-locking read (R31) — this adapter sends
/// one request and returns the answer, mutating nothing.
/// </summary>
public sealed class NatsStockAvailabilityChecker(INatsConnection connection, IOptions<NatsOptions> options) : IStockAvailabilityChecker
{
    public async Task<StockAvailabilityResult> CheckAsync(string companyCode, IReadOnlyList<StockAvailabilityLine> lines, CancellationToken cancellationToken)
    {
        var request = new StockCheckRequestPayload(
            companyCode,
            lines.Select(line => new StockCheckRequestLine(line.ProductCode, line.Quantity.Value)).ToList());

        NatsMsg<byte[]> reply;
        try
        {
            reply = await connection.RequestAsync<byte[], byte[]>(
                RpcSubjects.StockCheck,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(options.Value.StockCheckTimeoutMs) },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (NatsNoRespondersException)
        {
            // The IMMEDIATE 503 sentinel — no responder is subscribed at
            // all. Distinct from NatsNoReplyException below: this is
            // diagnosable at once, not after waiting out the deadline.
            throw new StockCheckTransportError(RpcSubjects.StockCheck, "no responder is subscribed to fulfillment.stock.check.");
        }
        catch (NatsNoReplyException)
        {
            // A responder IS subscribed but the subscription's own Timeout
            // elapsed with no reply — the transport-level counterpart of
            // saga.md's "a timeout is a legitimate, handled answer",
            // applied at order acceptance (asyncapi.yaml's RpcTimeout
            // schema). Empirically confirmed: NATS.Client.Core 3.2.0 throws
            // this on the reply task rather than returning a NatsMsg whose
            // Data is null (the XML doc's "Response can be (null) or one
            // NatsMsg<T>" describes an OLDER shape).
            throw new StockCheckTimeoutError(RpcSubjects.StockCheck, options.Value.StockCheckTimeoutMs);
        }

        if (reply.Data is null)
        {
            throw new StockCheckTimeoutError(RpcSubjects.StockCheck, options.Value.StockCheckTimeoutMs);
        }

        var payload = RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data);

        return new StockAvailabilityResult(
            payload.Available,
            payload.Lines.Select(line => new StockAvailabilityLineResult(line.ProductCode, line.Requested, line.Available, line.Sufficient)).ToList());
    }
}
