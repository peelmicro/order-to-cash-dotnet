using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>Ported from #7's own <c>auth.integration.spec.ts</c> finding F8 — "exactly the four documented public routes reject nothing; every other registered route rejects an anonymous request with 401" (here: the four documented public routes — <c>/auth/login</c> and <c>/docs</c> from feature <c>gateway_rest_auth</c>, plus <c>/health/live</c>/<c>/health/ready</c>, added by <c>observability_reliability</c>'s group A4, phase 14).</summary>
public sealed class DocsAndAnonymousRouteHttpTests
{
    private static Task<GatewayTestHost> StartAsync() => GatewayTestHost.StartAsync(options =>
    {
        options.Nats.Url = "nats://127.0.0.1:1";
        options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
        options.Mongo.Database = "otc_read_model_docs_it_unused";
    });

    [Fact]
    public async Task Docs_Answers200TextHtml_Unauthenticated()
    {
        await using var gateway = await StartAsync();

        var response = await gateway.Client.GetAsync("/docs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("openapi:", body);
    }

    /// <summary>
    /// Fix round, review defect D2 — the public set is now a LITERAL, and
    /// the protected set is derived by SUBTRACTING that literal from every
    /// mapped operation, never by filtering on the <c>IAllowAnonymous</c>
    /// metadata this test exists to police. Before this rewrite, marking
    /// <c>GET /orders</c> <c>.AllowAnonymous()</c> simply removed it from
    /// the candidate list instead of failing the test — review probe P2,
    /// 141 tests green. A SEPARATE assertion below also pins the
    /// <c>IAllowAnonymous</c> set itself to exactly the same literal, so a
    /// route wrongly made public fails on ONE of two independent checks:
    /// the literal-derived loop (a real 200 where 401 is asserted) and the
    /// metadata-set equality (the anonymous set no longer being exactly
    /// the literal). WIDENED by <c>observability_reliability</c>'s group A4
    /// (phase 14) to add <c>GET /health/live</c>/<c>GET /health/ready</c> —
    /// per CLAUDE.md's own "widening a literal set is exactly when a guard
    /// can quietly stop guarding" caution, this widening was confirmed
    /// (see <c>progress/impl_observability_reliability.md</c>) still to
    /// fail when an UNRELATED route is marked anonymous, never merely when
    /// this test's own literal grows.
    /// </summary>
    [Fact]
    public async Task EveryRegisteredRouteExceptTheFourDocumentedPublicOnes_RejectsAnAnonymousRequestWith401()
    {
        await using var gateway = await StartAsync();

        var builder = GatewayHost.CreateBuilder(
            ["--urls", "http://127.0.0.1:0"],
            options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_docs_it_unused_probe";
            });
        var probeApp = GatewayHost.Configure(builder.Build());
        var endpoints = ((IEndpointRouteBuilder)probeApp).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var allOperations = endpoints
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(m => (Method: m, Path: e.RoutePattern.RawText!)))
            .ToList();

        Assert.NotEmpty(allOperations);

        // The literal — every route documented as public across every
        // feature that has shipped so far: /auth/login and /docs
        // (gateway_rest_auth, id 25), /health/live and /health/ready
        // (observability_reliability's group A4, phase 14).
        var publicRoutes = new HashSet<string>(StringComparer.Ordinal)
        {
            "POST /auth/login",
            "GET /docs",
            "GET /health/live",
            "GET /health/ready",
        };

        var anonymousRoutes = endpoints
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(publicRoutes, anonymousRoutes);

        // The protected set is derived by SUBTRACTION from every mapped
        // operation, never by re-reading the AllowAnonymous metadata — a
        // route wrongly marked .AllowAnonymous() stays in this loop and
        // answers 200 where 401 is asserted below.
        var protectedRoutes = allOperations
            .Where(op => !publicRoutes.Contains($"{op.Method} {op.Path}"))
            .ToList();

        Assert.NotEmpty(protectedRoutes);

        foreach (var (method, path) in protectedRoutes)
        {
            var url = path.Replace("{id}", Guid.NewGuid().ToString());
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            var response = await gateway.Client.SendAsync(request);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"{method} {path} was expected to reject an anonymous request with 401, but answered {(int)response.StatusCode}.");
        }
    }
}
