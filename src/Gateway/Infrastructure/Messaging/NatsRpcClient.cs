using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Gateway.Infrastructure.Messaging;

/// <summary>
/// <see cref="IRpcClient"/> over REAL NATS core request-reply
/// (<c>specs/shared/asyncapi.yaml</c> <c>servers.rpcTransport</c>) — the
/// SAME catch-and-classify shape Orders' own
/// <c>NatsStockAvailabilityChecker</c> establishes
/// (<c>src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs</c>):
/// <see cref="NatsNoRespondersException"/> is an IMMEDIATE transport
/// refusal (no responder subscribed at all — diagnosable at once, never
/// waiting out the deadline); <see cref="NatsNoReplyException"/> is the
/// subscription's own <c>Timeout</c> elapsing with no reply (a responder
/// IS subscribed, it simply never answered in time) — the two map to
/// DIFFERENT <see cref="RpcCallError"/> subtypes, which is what "RPC
/// timeouts mapped to HTTP status codes, guard the mapping per case, not
/// that an error becomes an error" (this feature's own acceptance bullet)
/// requires: a real broker distinguishing the two is exactly the thing a
/// hand-typed status table cannot prove.
/// </summary>
public sealed class NatsRpcClient(INatsConnection connection, IOptions<NatsOptions> options) : IRpcClient
{
    public async Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken)
    {
        var headers = new NatsHeaders
        {
            ["x-correlation-id"] = meta.CorrelationId.ToString(),
            ["x-request-id"] = meta.RequestId.ToString(),
        };

        var timeoutMs = options.Value.DefaultTimeoutMs;

        NatsMsg<byte[]> reply;
        try
        {
            reply = await connection.RequestAsync<byte[], byte[]>(
                subject,
                RpcJson.Serialize(payload),
                headers: headers,
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(timeoutMs) },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (NatsNoRespondersException)
        {
            throw new RpcTransportError(subject, "no responder is subscribed to this subject.");
        }
        catch (NatsNoReplyException)
        {
            throw new RpcTimeoutError(subject, timeoutMs);
        }

        if (reply.Data is null)
        {
            // Empirically confirmed elsewhere in this repository
            // (NatsStockAvailabilityChecker's own remark): NATS.Client.Core
            // 3.2.0 throws NatsNoReplyException on the reply task rather
            // than ever returning a NatsMsg whose Data is null — this
            // branch is defensive, not the observed path.
            throw new RpcTimeoutError(subject, timeoutMs);
        }

        try
        {
            if (RpcJson.IsErrorBody(reply.Data))
            {
                var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data);
                throw new RpcBusinessError(subject, error.Code, error.Message, NormalizeDetails(error.Details));
            }

            return RpcJson.Deserialize<TReply>(reply.Data);
        }
        catch (JsonException)
        {
            throw new RpcTransportError(subject, "reply payload was not valid JSON.");
        }
    }

    /// <summary>
    /// <see cref="RpcErrorPayload.Details"/> deserialises through
    /// <c>System.Text.Json</c>'s generic <c>object?</c> handling, which
    /// yields boxed <see cref="JsonElement"/> instances, never native CLR
    /// primitives — so <c>RpcErrorClassifier</c>'s own <c>details.code is
    /// string</c> check would silently never match without this step.
    /// Scalars are unwrapped to their natural CLR type; a nested object or
    /// array (e.g. <c>STOCK_UNAVAILABLE</c>'s <c>shortages</c>) is left as
    /// a <see cref="JsonElement"/>, which <c>System.Text.Json</c> can
    /// re-serialise directly when the Presentation layer passes it through
    /// into the <c>Problem</c> response body untouched.
    /// </summary>
    private static IReadOnlyDictionary<string, object?>? NormalizeDetails(IReadOnlyDictionary<string, object?>? details)
    {
        if (details is null)
        {
            return null;
        }

        var normalized = new Dictionary<string, object?>(details.Count);
        foreach (var (key, value) in details)
        {
            normalized[key] = value is JsonElement element ? NormalizeElement(element) : value;
        }

        return normalized;
    }

    private static object? NormalizeElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var asLong) ? asLong : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element,
    };
}
