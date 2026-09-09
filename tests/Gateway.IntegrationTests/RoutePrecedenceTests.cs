using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Fix round, review defect D7 — the ported-idiom ledger's row 6 asserted
/// "ASP.NET Core Minimal API routing is NOT order-sensitive: a literal
/// route segment always outranks a route-parameter segment at the same
/// position, regardless of <c>Map*</c> call order" with no guard, and
/// CLAUDE.md's amended ledger rule (post feature 24) requires a row like
/// this to be probed and to say which direction it was probed in. This
/// test probes the REVERSED order specifically — the parameter route
/// (<c>/probe/{id}</c>) is mapped BEFORE the literal route
/// (<c>/probe/literal</c>), the ordering under which Express (the engine
/// #7's own F9 finding was about) would swallow the literal segment. A
/// real Kestrel round trip, not a route-table inspection, because
/// precedence is a MATCH-TIME behaviour.
/// </summary>
public sealed class RoutePrecedenceTests
{
    [Fact]
    public async Task ALiteralRouteSegment_OutranksAParameterSegment_EvenWhenTheParameterRouteIsRegisteredFirst()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        var app = builder.Build();

        // Registration order REVERSED relative to how OrdersEndpoints.cs
        // registers /orders/{id}: the parameter route first, the literal
        // route second — the ordering under which an order-sensitive
        // router (Express) would swallow "literal" into {id}.
        app.MapGet("/probe/{id}", (string id) => Results.Text($"parameter:{id}"));
        app.MapGet("/probe/literal", () => Results.Text("literal"));

        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            var response = await client.GetAsync("/probe/literal");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("literal", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
