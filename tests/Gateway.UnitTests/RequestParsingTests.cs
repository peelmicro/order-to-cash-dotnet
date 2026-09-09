using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OrderToCash.Gateway.Presentation;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>Ported from #7's own <c>pagination.spec.ts</c> — page/pageSize defaults, valid parsing, and rejecting out-of-range or non-integer values (openapi.yaml <c>Page</c>/<c>PageSize</c>).</summary>
public sealed class RequestParsingTests
{
    private static HttpRequest RequestWithQuery(Dictionary<string, StringValues> query)
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(query);
        return context.Request;
    }

    [Fact]
    public void ParsePageParams_DefaultsToPage1PageSize25_WhenBothAreAbsent()
    {
        var (page, pageSize) = RequestParsing.ParsePageParams(RequestWithQuery([]));

        Assert.Equal(1, page);
        Assert.Equal(25, pageSize);
    }

    [Fact]
    public void ParsePageParams_ParsesValidPageAndPageSize()
    {
        var (page, pageSize) = RequestParsing.ParsePageParams(RequestWithQuery(new Dictionary<string, StringValues> { ["page"] = "3", ["pageSize"] = "50" }));

        Assert.Equal(3, page);
        Assert.Equal(50, pageSize);
    }

    [Fact]
    public void ParsePageParams_Throws_WhenPageSizeExceeds200()
    {
        Assert.Throws<GatewayRequestValidationError>(
            () => RequestParsing.ParsePageParams(RequestWithQuery(new Dictionary<string, StringValues> { ["pageSize"] = "201" })));
    }

    [Fact]
    public void ParsePageParams_Throws_WhenPageIsBelow1()
    {
        Assert.Throws<GatewayRequestValidationError>(
            () => RequestParsing.ParsePageParams(RequestWithQuery(new Dictionary<string, StringValues> { ["page"] = "0" })));
    }

    [Fact]
    public void ParsePageParams_Throws_ForANonIntegerPage()
    {
        Assert.Throws<GatewayRequestValidationError>(
            () => RequestParsing.ParsePageParams(RequestWithQuery(new Dictionary<string, StringValues> { ["page"] = "abc" })));
    }

    [Fact]
    public void ToStringArray_ReturnsNull_WhenTheParameterIsAbsent()
    {
        Assert.Null(RequestParsing.ToStringArray(RequestWithQuery([]), "status"));
    }

    [Fact]
    public void ToStringArray_WrapsASingleValueInAnArray()
    {
        var result = RequestParsing.ToStringArray(RequestWithQuery(new Dictionary<string, StringValues> { ["status"] = "placed" }), "status");

        Assert.Equal(["placed"], result);
    }

    [Fact]
    public void ToStringArray_PassesMultipleRepeatedValuesThrough()
    {
        var result = RequestParsing.ToStringArray(
            RequestWithQuery(new Dictionary<string, StringValues> { ["status"] = new StringValues(["placed", "cancelled"]) }), "status");

        Assert.Equal(["placed", "cancelled"], result);
    }
}
