using Microsoft.AspNetCore.Http;

namespace OrderToCash.Gateway.Presentation;

/// <summary>Shared route/query-parameter parsing every endpoint that names an id or paginates uses — openapi.yaml <c>components.parameters.Page</c>/<c>PageSize</c> (<c>page &gt;= 1</c>, <c>1 &lt;= pageSize &lt;= 200</c>, defaults 1/25).</summary>
public static class RequestParsing
{
    public static Guid ParseId(string field, string raw)
    {
        if (!Guid.TryParse(raw, out var value) || value == Guid.Empty)
        {
            throw new GatewayRequestValidationError(field, $"\"{raw}\" is not a valid {field}");
        }

        return value;
    }

    public static (int Page, int PageSize) ParsePageParams(HttpRequest request)
    {
        return (ParsePositiveInt(request, "page", 1, 1, int.MaxValue), ParsePositiveInt(request, "pageSize", 25, 1, 200));
    }

    private static int ParsePositiveInt(HttpRequest request, string field, int fallback, int min, int max)
    {
        if (!request.Query.TryGetValue(field, out var raw) || raw.Count == 0 || string.IsNullOrEmpty(raw[0]))
        {
            return fallback;
        }

        if (!int.TryParse(raw[0], out var value) || value < min || value > max)
        {
            throw new GatewayRequestValidationError(field, $"\"{raw[0]}\" is not a valid {field} (expected an integer between {min} and {max})");
        }

        return value;
    }

    /// <summary><c>status=a&amp;status=b</c> (openapi <c>style: form, explode: true</c>) — ASP.NET Core already gives every occurrence of a repeated query key as one <see cref="Microsoft.Extensions.Primitives.StringValues"/>. <see langword="null"/> when the parameter was omitted entirely.</summary>
    public static IReadOnlyList<string>? ToStringArray(HttpRequest request, string field)
    {
        if (!request.Query.TryGetValue(field, out var raw) || raw.Count == 0)
        {
            return null;
        }

        return raw.Where(v => v is not null).Select(v => v!).ToList();
    }
}
