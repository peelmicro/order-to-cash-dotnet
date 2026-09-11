using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Gateway.Infrastructure.Docs;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Acceptance bullet 1 — "contract test asserts no drift from
/// openapi.yaml". Derives BOTH sides from the real artefact rather than a
/// hand-typed list on either side: the "expected" set is parsed out of the
/// EMBEDDED <c>openapi.yaml</c> bytes (<see cref="EmbeddedOpenApiDocument"/>
/// — the exact copy <c>GET /docs</c> serves), and the "actual" set is read
/// off the real, running app's own <see cref="EndpointDataSource"/> after
/// <see cref="GatewayHost.Configure"/> has mapped every endpoint — never a
/// list either side retypes. This is what backlog id 64 named as the shape
/// that CANNOT fail: comparing a hand-typed list against the spec and never
/// against the running routes.
/// </summary>
public sealed partial class OpenApiContractTests
{
    /// <summary>
    /// Empty — the last two entries this set ever carried,
    /// <c>/health/live</c>/<c>/health/ready</c>, moved into the real,
    /// mapped set below when <c>observability_reliability</c>'s group A4
    /// (phase 14) mapped both (<c>HealthEndpoints.MapHealthEndpoints</c>),
    /// exactly the way <c>/orders/stream</c> moved out through feature
    /// <c>gateway_sse_push</c> (id 26) before it. Kept as a field, not
    /// deleted, so the NEXT feature that legitimately defers a declared
    /// path has an established place to record it — the same reasoning
    /// this comment's own predecessor used.
    /// </summary>
    private static readonly HashSet<(string Method, string Path)> _deliberatelyExcluded = [];

    [Fact]
    public void MappedEndpoints_MatchOpenApiYamlExactly_ForEveryPathThisFeatureBuilds()
    {
        var expected = ParseOpenApiOperations(EmbeddedOpenApiDocument.Yaml)
            .Where(op => !_deliberatelyExcluded.Contains(op))
            .ToHashSet();

        var actual = RealMappedOperations();

        var missing = expected.Except(actual).ToList();
        var unexpected = actual.Except(expected).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"Drift from openapi.yaml.{Environment.NewLine}" +
            $"Declared in openapi.yaml but NOT mapped: {string.Join(", ", missing.Select(o => $"{o.Method} {o.Path}"))}{Environment.NewLine}" +
            $"Mapped but NOT declared in openapi.yaml: {string.Join(", ", unexpected.Select(o => $"{o.Method} {o.Path}"))}");
    }

    /// <summary>Ported from #7's own contract-drift.integration.spec.ts: "openapi.yaml declares exactly 17 top-level paths and 18 operations — GET+POST /orders share one path key."</summary>
    [Fact]
    public void OpenApiYaml_Declares17PathsAnd18Operations()
    {
        var operations = ParseOpenApiOperations(EmbeddedOpenApiDocument.Yaml);
        var distinctPaths = operations.Select(o => o.Path).ToHashSet();

        Assert.Equal(18, operations.Count);
        Assert.Equal(17, distinctPaths.Count);
    }

    /// <summary>The excluded set is itself a claim, not a hard-coded escape hatch: every entry must genuinely be absent from the running app (never mapped here, on purpose) AND genuinely present in openapi.yaml (a real contract path, not a typo this test would otherwise never catch).</summary>
    [Fact]
    public void TheDeliberatelyExcludedPaths_AreDeclaredInOpenApiYaml_ButNotMappedByThisFeature()
    {
        var declared = ParseOpenApiOperations(EmbeddedOpenApiDocument.Yaml);
        var actual = RealMappedOperations();

        foreach (var excluded in _deliberatelyExcluded)
        {
            Assert.Contains(excluded, declared);
            Assert.DoesNotContain(excluded, actual);
        }
    }

    private static HashSet<(string Method, string Path)> RealMappedOperations()
    {
        var builder = GatewayHost.CreateBuilder(
            args: ["--urls", "http://127.0.0.1:0"],
            configure: options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_contract_probe";
            });

        var app = GatewayHost.Configure(builder.Build());

        // The DI-registered singleton EndpointDataSource is not
        // dependably populated before the app has actually processed a
        // request — reading WebApplication's OWN IEndpointRouteBuilder.DataSources
        // (what every app.Map* call above actually appended to) is the
        // reliable seam: it is populated the moment Map* returns, no
        // Kestrel start required.
        var dataSources = ((IEndpointRouteBuilder)app).DataSources;
        var operations = new HashSet<(string Method, string Path)>();

        foreach (var endpoint in dataSources.SelectMany(ds => ds.Endpoints))
        {
            if (endpoint is not RouteEndpoint routeEndpoint)
            {
                continue;
            }

            var methodMetadata = routeEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
            if (methodMetadata is null)
            {
                continue;
            }

            foreach (var method in methodMetadata.HttpMethods)
            {
                operations.Add((method, routeEndpoint.RoutePattern.RawText ?? string.Empty));
            }
        }

        return operations;
    }

    /// <summary>
    /// Reads ONLY the <c>paths:</c> section's structure — a 2-space-indented
    /// path key, a 4-space-indented HTTP-verb key under it — never a full
    /// YAML parse, which this contract does not need: indentation is the
    /// one thing this file's own generator holds fixed (confirmed by
    /// direct inspection of every path/method pair in
    /// <c>specs/shared/openapi.yaml</c> before writing this regex).
    /// </summary>
    private static HashSet<(string Method, string Path)> ParseOpenApiOperations(string yaml)
    {
        var operations = new HashSet<(string, string)>();
        string? currentPath = null;
        var inPathsSection = false;

        foreach (var line in yaml.Split('\n'))
        {
            if (TopLevelKeyRegex().IsMatch(line))
            {
                inPathsSection = line.TrimEnd('\r').Equals("paths:", StringComparison.Ordinal);
                continue;
            }

            if (!inPathsSection)
            {
                continue;
            }

            var pathMatch = PathKeyRegex().Match(line);
            if (pathMatch.Success)
            {
                currentPath = pathMatch.Groups["path"].Value;
                continue;
            }

            var methodMatch = MethodKeyRegex().Match(line);
            if (methodMatch.Success && currentPath is not null)
            {
                operations.Add((methodMatch.Groups["method"].Value.ToUpperInvariant(), currentPath));
            }
        }

        return operations;
    }

    [GeneratedRegex(@"^\S")]
    private static partial Regex TopLevelKeyRegex();

    [GeneratedRegex(@"^  (?<path>/\S*):\s*$")]
    private static partial Regex PathKeyRegex();

    [GeneratedRegex(@"^    (?<method>get|post|put|delete|patch):\s*$")]
    private static partial Regex MethodKeyRegex();
}
