// COPY OF — src/Fulfillment/Presentation/Rpc/RpcMeta.cs
using NATS.Client.Core;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>The two correlation values every <c>billing.credit.hold</c>/<c>billing.credit.release</c> request must carry — `BC1`.</summary>
public readonly record struct RpcMeta(UniqueId CorrelationId, UniqueId RequestId);

/// <summary>
/// Extracts and validates <c>x-correlation-id</c>/<c>x-request-id</c> off a
/// request's <see cref="NatsHeaders"/> (design.md §4.6). Required on
/// <c>billing.credit.hold</c> and <c>billing.credit.release</c> only
/// (`BC1`) — never thrown, so the responder can turn a failure into
/// <c>VALIDATION_FAILED</c> before any dispatch, without paying for an
/// exception on the ordinary path.
/// </summary>
public static class RpcMetaExtractor
{
    private const string CorrelationIdHeader = "x-correlation-id";
    private const string RequestIdHeader = "x-request-id";

    public static bool TryExtract(NatsHeaders? headers, out RpcMeta meta, out string? error)
    {
        meta = default;

        if (headers is null || !headers.TryGetValue(CorrelationIdHeader, out var correlationValues))
        {
            error = $"the required header '{CorrelationIdHeader}' is missing.";
            return false;
        }

        if (!headers.TryGetValue(RequestIdHeader, out var requestValues))
        {
            error = $"the required header '{RequestIdHeader}' is missing.";
            return false;
        }

        if (!TryParseUniqueId(correlationValues.ToString(), out var correlationId))
        {
            error = $"'{CorrelationIdHeader}' is not a well-formed UniqueId.";
            return false;
        }

        if (!TryParseUniqueId(requestValues.ToString(), out var requestId))
        {
            error = $"'{RequestIdHeader}' is not a well-formed UniqueId.";
            return false;
        }

        error = null;
        meta = new RpcMeta(correlationId, requestId);
        return true;
    }

    private static bool TryParseUniqueId(string? raw, out UniqueId id)
    {
        id = default;

        if (!Guid.TryParse(raw, out var guid) || guid == Guid.Empty)
        {
            return false;
        }

        id = UniqueId.From(guid);
        return true;
    }
}
