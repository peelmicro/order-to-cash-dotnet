using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Queries;
using OrderToCash.Gateway.Presentation;
using OrderToCash.Gateway.Presentation.Problem;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Direct unit tests of <see cref="ProblemJsonMiddleware.Classify"/> — no
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/>, no host. Ported from
/// #7's own <c>problem-json.filter.spec.ts</c>, which drives the equivalent
/// NestJS filter the same way (constructing the exception directly and
/// reading the classification), for every case that is exercised end-to-end
/// in <c>OrdersHttpTests</c>/<c>AuthAndRateLimitHttpTests</c> too — the pair
/// proves the SAME mapping both fast (here) and over a real socket (there).
/// </summary>
public sealed class ProblemJsonMiddlewareClassificationTests
{
    [Fact]
    public void Classify_RpcBusinessError_StockUnavailable_MapsTo409AndSurfacesShortages()
    {
        var shortages = new[] { new { productCode = "PRD-0001", requested = 5, available = 2 } };
        var error = new RpcBusinessError("orders.create", "STOCK_UNAVAILABLE", "insufficient stock", new Dictionary<string, object?> { ["shortages"] = shortages });

        var classified = ProblemJsonMiddleware.Classify(error);

        Assert.Equal(409, classified.Status);
        Assert.Equal("STOCK_UNAVAILABLE", classified.Code);
        Assert.NotNull(classified.Extra);
        Assert.Same(shortages, classified.Extra!["shortages"]);
    }

    [Fact]
    public void Classify_RpcTimeoutError_MapsTo503UpstreamTimeout()
    {
        var classified = ProblemJsonMiddleware.Classify(new RpcTimeoutError("orders.create", 5000));

        Assert.Equal(503, classified.Status);
        Assert.Equal("UPSTREAM_TIMEOUT", classified.Code);
    }

    [Fact]
    public void Classify_InvalidCredentialsError_MapsTo401()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvalidCredentialsError());

        Assert.Equal(401, classified.Status);
        Assert.Equal("INVALID_CREDENTIALS", classified.Code);
    }

    [Fact]
    public void Classify_InvalidTokenError_MapsTo401Unauthorized()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvalidTokenError("expired"));

        Assert.Equal(401, classified.Status);
        Assert.Equal("UNAUTHORIZED", classified.Code);
    }

    [Fact]
    public void Classify_InvoiceNotFoundError_MapsTo404()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvoiceNotFoundError(Guid.NewGuid()));

        Assert.Equal(404, classified.Status);
        Assert.Equal("NOT_FOUND", classified.Code);
    }

    /// <summary>F4 (#7's own review finding, ported) — the invoice MAY exist; the gateway only stopped looking. Never a 404.</summary>
    [Fact]
    public void Classify_InvoiceScanBudgetExceededError_MapsTo503_NeverA404()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvoiceScanBudgetExceededError(Guid.NewGuid(), 1000));

        Assert.Equal(503, classified.Status);
        Assert.Equal("SCAN_BUDGET_EXCEEDED", classified.Code);
    }

    [Fact]
    public void Classify_OrderNotYetProjectedError_MapsTo503()
    {
        var classified = ProblemJsonMiddleware.Classify(new OrderNotYetProjectedError("ORD-000042"));

        Assert.Equal(503, classified.Status);
        Assert.Equal("UPSTREAM_UNAVAILABLE", classified.Code);
    }

    [Fact]
    public void Classify_GatewayRequestValidationError_MapsTo400WithAnErrorsArray()
    {
        var error = new GatewayRequestValidationError("page", "\"abc\" is not a valid page");

        var classified = ProblemJsonMiddleware.Classify(error);

        Assert.Equal(400, classified.Status);
        Assert.Equal("VALIDATION_FAILED", classified.Code);
        Assert.NotNull(classified.Extra);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<ValidationFieldError>>(classified.Extra!["errors"]);
        Assert.Single(errors);
        Assert.Equal("page", errors[0].Field);
    }

    [Fact]
    public void Classify_GatewayNotFoundError_MapsTo404()
    {
        var classified = ProblemJsonMiddleware.Classify(new GatewayNotFoundError("no order for id \"x\""));

        Assert.Equal(404, classified.Status);
        Assert.Equal("NOT_FOUND", classified.Code);
    }

    /// <summary>Fix round, review defect D6 — before this case, <c>UnknownOperatorError</c> was the ONE built branch (of ten) with no test at any level. Behaviourally identical to the <c>default</c> fallback, but that identity was never checked.</summary>
    [Fact]
    public void Classify_UnknownOperatorError_MapsTo500InternalError()
    {
        var classified = ProblemJsonMiddleware.Classify(new UnknownOperatorError("ghost"));

        Assert.Equal(500, classified.Status);
        Assert.Equal("INTERNAL_ERROR", classified.Code);
    }

    [Fact]
    public void Classify_AnUnrecognisedException_FallsBackTo500InternalError()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvalidOperationException("something unexpected"));

        Assert.Equal(500, classified.Status);
        Assert.Equal("INTERNAL_ERROR", classified.Code);
    }

    /// <summary>The detail message is exactly the thrown error's own message — never a generic wrapper that could accidentally leak or obscure it (#7's own review wording: "never leaks the JWT secret or any credential").</summary>
    [Fact]
    public void Classify_NeverRewritesTheOriginalMessage()
    {
        var classified = ProblemJsonMiddleware.Classify(new InvalidCredentialsError());

        Assert.Equal("username or password is incorrect", classified.Detail);
    }
}
