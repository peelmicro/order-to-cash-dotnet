using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderToCash.Contracts.Wire;
using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Domain.Problem;

namespace OrderToCash.Gateway.Presentation.Problem;

/// <summary>
/// The ONE place every thrown error becomes an RFC 9457
/// <c>application/problem+json</c> body (openapi.yaml <c>Problem</c>, R58).
/// Catches everything, so a handler never has to know this shape exists —
/// it just throws the error that is true and this middleware translates.
/// Ported from #7's <c>apps/gateway/src/presentation/problem-json.filter.ts</c>
/// (<c>ProblemJsonExceptionFilter</c>).
/// </summary>
public sealed class ProblemJsonMiddleware(RequestDelegate next, IClock clock, ILogger<ProblemJsonMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteProblemAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var classification = Classify(exception);
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is Guid guid
            ? guid
            : Guid.NewGuid();
        var occurredAt = clock.UtcNow;

        logger.LogWarning(
            exception,
            "{Code} {Status}: {Message} (correlationId={CorrelationId})",
            classification.Code,
            classification.Status,
            classification.Detail,
            correlationId);

        var body = new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = classification.Title,
            ["status"] = classification.Status,
            ["detail"] = classification.Detail,
            ["code"] = classification.Code,
            ["correlationId"] = correlationId,
            ["occurredAt"] = occurredAt,
        };

        if (classification.Extra is not null)
        {
            foreach (var (key, extraValue) in classification.Extra)
            {
                body[key] = extraValue;
            }
        }

        if (classification.RetryAfterSeconds is { } retryAfter)
        {
            context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.Response.StatusCode = classification.Status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonWire.Options)).ConfigureAwait(false);
    }

    /// <summary><see langword="internal"/> (<c>InternalsVisibleTo</c>) so <c>ProblemJsonMiddlewareClassificationTests</c> can drive <see cref="Classify"/> directly — no <see cref="HttpContext"/>, no host — the same seam every other service's own <c>*ErrorMapper</c> establishes.</summary>
    internal sealed record Classification(int Status, string Code, string Title, string Detail, Dictionary<string, object?>? Extra = null, int? RetryAfterSeconds = null);

    internal static Classification Classify(Exception exception)
    {
        switch (exception)
        {
            case RpcCallError rpcError:
                {
                    var classified = RpcErrorClassifier.Classify(rpcError);
                    Dictionary<string, object?>? extra = null;
                    if (classified.Code == "STOCK_UNAVAILABLE" && rpcError.Details is { } details && details.TryGetValue("shortages", out var shortages))
                    {
                        extra = new Dictionary<string, object?> { ["shortages"] = shortages };
                    }

                    return new Classification(classified.Status, classified.Code, classified.Title, rpcError.Message, extra);
                }

            case InvalidCredentialsError:
                return new Classification(401, "INVALID_CREDENTIALS", "Bad credentials", exception.Message);

            case InvalidTokenError:
                return new Classification(401, "UNAUTHORIZED", "Missing, expired or invalid bearer token", exception.Message);

            case InvoiceNotFoundError:
                return new Classification(404, "NOT_FOUND", "No such invoice", exception.Message);

            case GatewayNotFoundError:
                return new Classification(404, "NOT_FOUND", "No such resource", exception.Message);

            case InvoiceScanBudgetExceededError:
                // The invoice may exist; the gateway only stopped looking.
                // A 404 would be a false statement, so this is a distinct
                // 503, honestly framed as "could not resolve within budget"
                // rather than "not found" (ported from #7's identical F4
                // review finding).
                return new Classification(503, "SCAN_BUDGET_EXCEEDED", "Could not resolve the invoice within this gateway's search window", exception.Message);

            case OrderNotYetProjectedError:
                return new Classification(503, "UPSTREAM_UNAVAILABLE", "The order is not yet available for this operation", exception.Message);

            case UnknownOperatorError:
                return new Classification(500, "INTERNAL_ERROR", "An unexpected error occurred", exception.Message);

            case GatewayRequestValidationError validationError:
                return new Classification(
                    400,
                    "VALIDATION_FAILED",
                    "The request was malformed or failed schema validation",
                    exception.Message,
                    new Dictionary<string, object?> { ["errors"] = validationError.Errors });

            default:
                return new Classification(500, "INTERNAL_ERROR", "An unexpected error occurred", exception.Message);
        }
    }
}
