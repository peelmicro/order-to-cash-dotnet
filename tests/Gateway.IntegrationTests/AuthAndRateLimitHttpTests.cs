using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>Real Kestrel, real socket round trip, real hand-rolled JWT — no NATS/Mongo needed for these (auth is entirely local to the Gateway process).</summary>
public sealed class AuthAndRateLimitHttpTests
{
    private static Task<GatewayTestHost> StartAsync(int loginPermitLimit = 10) =>
        GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = "nats://127.0.0.1:1";
            options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
            options.Mongo.Database = "otc_read_model_auth_it_unused";
            options.LoginThrottle = new Infrastructure.RateLimiting.LoginThrottleOptions { PermitLimit = loginPermitLimit, WindowSeconds = 60 };
        });

    [Fact]
    public async Task Login_WithTheCorrectCredentials_Returns200AndABearerToken()
    {
        await using var gateway = await StartAsync();

        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("Bearer", body!["tokenType"].ToString());
        Assert.False(string.IsNullOrEmpty(body["accessToken"].ToString()));
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_Returns401ProblemJson()
    {
        await using var gateway = await StartAsync();

        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Me_WithoutABearerToken_Returns401()
    {
        await using var gateway = await StartAsync();

        var response = await gateway.Client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAValidBearerToken_ReturnsTheOperatorsIdentity()
    {
        await using var gateway = await StartAsync();
        var login = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["accessToken"].ToString();
        gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await gateway.Client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("operator", body!["username"].ToString());
    }

    /// <summary>Fix round, review defect D6 — before this test, <c>InvalidTokenError</c> was proven over HTTP only in its MISSING-token flavour (<see cref="Me_WithoutABearerToken_Returns401"/>); a tampered token had no HTTP-level test at all.</summary>
    [Fact]
    public async Task Me_WithATamperedBearerToken_Returns401()
    {
        await using var gateway = await StartAsync();
        var login = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["accessToken"].ToString()!;
        var flipIndex = token.Length / 2;
        var tampered = token[..flipIndex] + (token[flipIndex] == 'A' ? 'B' : 'A') + token[(flipIndex + 1)..];
        gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tampered);

        var response = await gateway.Client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>R63 — tripping the limit answers 429 + Problem + Retry-After in seconds, issues no token, and does not affect any other endpoint.</summary>
    [Fact]
    public async Task Login_ExceedingTheRateLimit_Returns429WithRetryAfterAndIssuesNoToken()
    {
        await using var gateway = await StartAsync(loginPermitLimit: 3);

        for (var i = 0; i < 3; i++)
        {
            var ok = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var limited = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });

        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null || limited.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>The SAME 429 for valid and invalid credentials — the refusal reveals nothing about whether the credentials would have worked (R63).</summary>
    [Fact]
    public async Task Login_ExceedingTheRateLimit_AnswersTheSame429_RegardlessOfWhetherTheCredentialsWereValid()
    {
        await using var gateway = await StartAsync(loginPermitLimit: 2);

        await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });

        var withValidCredentials = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        var withInvalidCredentials = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "definitely-wrong" });

        Assert.Equal((HttpStatusCode)429, withValidCredentials.StatusCode);
        Assert.Equal((HttpStatusCode)429, withInvalidCredentials.StatusCode);
    }

    /// <summary>The limit is scoped to /auth/login alone — a client refused there is still served on every other endpoint.</summary>
    [Fact]
    public async Task ARefusedClient_IsStillServed_OnEveryOtherEndpoint()
    {
        await using var gateway = await StartAsync(loginPermitLimit: 1);

        await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        var limited = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);

        var docs = await gateway.Client.GetAsync("/docs");
        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);

        var meWithoutToken = await gateway.Client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meWithoutToken.StatusCode);
    }
}
