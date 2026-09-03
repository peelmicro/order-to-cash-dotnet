using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §9.2's mapping table, reproduced from #7's
/// <c>rpc-error-mapper.ts</c> — every failure the <c>orders.create</c>
/// responder can observe, mapped to <c>asyncapi.yaml</c>'s closed
/// <c>RpcError.code</c> enum.
/// </summary>
public sealed class OrdersCreateErrorMapperTests
{
    private static readonly DateTimeOffset _occurredAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The stock-check rejection path (this feature's acceptance item 3) — a business outcome, not a transport error, carrying the short lines in `details.shortages`.</summary>
    [Fact]
    public void Map_StockUnavailableError_MapsToStockUnavailableWithTheShortLinesInDetails()
    {
        var shortage = new StockAvailabilityLineResult("PROD-001", 2, 1, false);
        var error = new StockUnavailableError([shortage]);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("STOCK_UNAVAILABLE", payload.Code);
        Assert.Equal(error.Message, payload.Message);
        Assert.Same(shortage, Assert.Single((IReadOnlyList<StockAvailabilityLineResult>)payload.Details!["shortages"]!));
        Assert.Equal(_occurredAt, payload.OccurredAt);
    }

    [Fact]
    public void Map_StockCheckTimeoutError_MapsToTimeoutNotStockUnavailable()
    {
        var error = new StockCheckTimeoutError("fulfillment.stock.check", 5_000);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("TIMEOUT", payload.Code);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
        Assert.Equal(5_000, payload.Details!["timeoutMs"]);
    }

    [Fact]
    public void Map_StockCheckTransportError_MapsToUnavailableNotStockUnavailable()
    {
        var error = new StockCheckTransportError("fulfillment.stock.check", "no responder subscribed");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("UNAVAILABLE", payload.Code);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
    }

    [Fact]
    public void Map_ReferenceDataNotFoundError_MapsToNotFoundWithFieldAndValue()
    {
        var error = new ReferenceDataNotFoundError("productCode", "UNKNOWN-PRODUCT");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("NOT_FOUND", payload.Code);
        Assert.Equal("productCode", payload.Details!["field"]);
        Assert.Equal("UNKNOWN-PRODUCT", payload.Details!["value"]);
    }

    [Fact]
    public void Map_OrderDiscountNotSupportedError_MapsToValidationFailedWithNoDetails()
    {
        var error = new OrderDiscountNotSupportedError(150);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.Null(payload.Details);
    }

    /// <summary>Every aggregate refusal collapses to VALIDATION_FAILED, but the specific domain Code survives in `details.code` — design.md §9.2: "The details key is code, not domainCode".</summary>
    [Fact]
    public void Map_ADomainError_MapsToValidationFailedAndPreservesTheDomainCodeInDetails()
    {
        DomainError error = new OrderMustHaveAtLeastOneLineError();

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.Equal("order.must_have_at_least_one_line", payload.Details!["code"]);
    }

    [Fact]
    public void Map_AnUnrecognisedException_MapsToInternalError()
    {
        var payload = OrdersCreateErrorMapper.Map(new InvalidOperationException("boom"), _occurredAt);

        Assert.Equal("INTERNAL_ERROR", payload.Code);
        Assert.Equal("boom", payload.Message);
    }

    /// <summary>
    /// review D4 (round 2) — A2's whole point, unguarded until now: a
    /// wire-shape refusal (a required field missing) is client-caused and
    /// must map to <c>VALIDATION_FAILED</c>, never fall through to the
    /// catch-all's <c>INTERNAL_ERROR</c> — the exact symptom A2 was raised
    /// to fix, re-created verbatim by reverting this one arm.
    /// </summary>
    [Fact]
    public void Map_AnInvalidOrdersCreateRequestError_MapsToValidationFailedNotInternalError()
    {
        var error = new InvalidOrdersCreateRequestError("orders.create request is missing or has an empty required field: lines.");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.NotEqual("INTERNAL_ERROR", payload.Code);
        Assert.Equal(error.Message, payload.Message);
    }
}
