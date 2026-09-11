using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Projection;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// R58/OR7, design.md §6's last paragraph — <c>CorrelationIdMiddleware</c>
/// registered BEFORE <c>ProblemJsonMiddleware</c> so the problem body and
/// its own log line carry the SAME <c>correlationId</c>. <see cref="Console.Out"/>
/// is redirected BEFORE <see cref="GatewayTestHost.StartAsync"/> — the JSON
/// console provider captures its <see cref="TextWriter"/> once, at
/// construction time.
/// </summary>
public sealed class ProblemJsonCorrelationTests
{
    [Fact]
    public async Task R58_OR7_TheProblemBodyAndItsOwnLogLineCarryTheSameCorrelationIdAsTheRequestThatFailed()
    {
        using var capture = new CapturedConsole();
        string correlationId;

        using (capture.Redirect())
        {
            var rpc = new RecordingRpcClient();
            var readModel = new InMemoryOrderReadModel();

            var gateway = await GatewayTestHost.StartAsync(
                options =>
                {
                    options.Nats.Url = "nats://127.0.0.1:1";
                    options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                    options.Mongo.Database = "otc_read_model_orders_it_unused";
                },
                overrideServices: services =>
                {
                    services.RemoveAll<IRpcClient>();
                    services.AddSingleton<IRpcClient>(rpc);
                    services.RemoveAll<IOrderReadModel>();
                    services.AddSingleton<IOrderReadModel>(readModel);
                });
            await using var disposeGateway = gateway;

            var login = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
            var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["accessToken"].ToString()!;
            gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // A malformed order id — 400 VALIDATION_FAILED, no downstream
            // call, ported from #7's own black_box_api scenario 4
            // (OrdersHttpTests' own precedent).
            var response = await gateway.Client.GetAsync("/orders/not-a-guid-at-all");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            correlationId = body.GetProperty("correlationId").GetString()!;

            await Task.Delay(300);
        }

        Assert.False(string.IsNullOrEmpty(correlationId));

        var records = capture.ParseJsonLines();
        var mine = records.Where(r => ScopeValue(r, "correlationId") == correlationId).ToList();

        Assert.NotEmpty(mine);
    }

    private static string? ScopeValue(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        public Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to be called by this test's own request.");
    }

    private sealed class InMemoryOrderReadModel : IOrderReadModel
    {
        public Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken) => Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken) => Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken) => Task.FromResult(new OrderListResult([], 0));
    }
}
