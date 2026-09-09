using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Domain.Projection;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Real Kestrel, real socket round trip, but a FAKE <see cref="IRpcClient"/>/
/// <see cref="IOrderReadModel"/> substituted after <c>CreateBuilder</c> —
/// these tests are about the HTTP TRANSLATION layer (status codes, headers,
/// the 202-vs-404 distinction), not about the NATS wire itself (which
/// <see cref="NatsRpcClientIntegrationTests"/>/<see cref="FulfillmentStockEndToEndTests"/>
/// already prove against a real broker).
/// </summary>
public sealed class OrdersHttpTests
{
    private sealed class RecordingRpcClient : IRpcClient
    {
        public Func<string, object, Task<object>>? Handler { get; set; }

        public string? LastSubject { get; private set; }

        public RpcCallMeta? LastMeta { get; private set; }

        public int CallCount { get; private set; }

        public async Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSubject = subject;
            LastMeta = meta;

            if (Handler is null)
            {
                throw new InvalidOperationException("No handler configured.");
            }

            return (TReply)await Handler(subject, payload!);
        }
    }

    private sealed class InMemoryOrderReadModel : IOrderReadModel
    {
        public OrderReadModelDocument? ById { get; set; }

        public IReadOnlyList<OrderReadModelDocument> ListItems { get; set; } = [];

        public Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(ById is not null && ById.OrderId == orderId ? ById : null);

        public Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken) =>
            Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new OrderListResult(ListItems, ListItems.Count));
    }

    private static async Task<(GatewayTestHost Gateway, RecordingRpcClient Rpc, InMemoryOrderReadModel ReadModel, string Token)> StartAuthenticatedAsync()
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

        var login = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        var token = (await login.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["accessToken"].ToString()!;
        gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return (gateway, rpc, readModel, token);
    }

    [Fact]
    public async Task PlaceOrder_Returns201_WithLocationAndCorrelationHeaders_AndTheOrderIdInTheBody()
    {
        var (gateway, rpc, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var orderId = Guid.NewGuid();
        rpc.Handler = (subject, _) =>
        {
            Assert.Equal(GatewaySubjects.OrdersCreate, subject);
            return Task.FromResult<object>(new OrdersCreateReplyPayload(orderId, "ORD-000042", "placed", "EUR", 124250, 0, 124250, DateTimeOffset.UtcNow));
        };

        var response = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = new[] { new { productCode = "PRD-0001", quantity = 5 } } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/orders/{orderId}", response.Headers.Location!.ToString());
        Assert.Equal(orderId.ToString(), response.Headers.GetValues("X-Correlation-Id").Single());
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal(orderId.ToString(), body!["orderId"].ToString());
        Assert.True((bool)((System.Text.Json.JsonElement)body["projectionPending"]).GetBoolean());
    }

    /// <summary>An order this gateway JUST placed but the read model has not caught up to yet — 202, never a false 404 (R55).</summary>
    [Fact]
    public async Task GetOrder_RightAfterPlacingIt_Answers202ProjectionPending_NotAFalse404()
    {
        var (gateway, rpc, readModel, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var orderId = Guid.NewGuid();
        rpc.Handler = (_, _) => Task.FromResult<object>(new OrdersCreateReplyPayload(orderId, "ORD-000042", "placed", "EUR", 100, 0, 100, DateTimeOffset.UtcNow));
        readModel.ById = null;

        var placed = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = new[] { new { productCode = "PRD-0001", quantity = 5 } } });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);

        var response = await gateway.Client.GetAsync($"/orders/{orderId}");

        Assert.Equal((HttpStatusCode)202, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("projection_pending", body!["status"].ToString());
    }

    /// <summary>An id this gateway never handed out — the genuine 404 openapi.yaml documents.</summary>
    [Fact]
    public async Task GetOrder_ForAnIdThisGatewayNeverIssued_Answers404()
    {
        var (gateway, _, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;

        var response = await gateway.Client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Ported from #7's own black_box_api scenario 4: "a malformed order id [answers] 400".</summary>
    [Fact]
    public async Task GetOrder_ForAMalformedId_Answers400()
    {
        var (gateway, _, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;

        var response = await gateway.Client.GetAsync("/orders/not-a-guid-at-all");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"VALIDATION_FAILED\"", body);
    }

    /// <summary>The acceptance-time availability check failing — 409 STOCK_UNAVAILABLE, with the shortages array passed through into the Problem body.</summary>
    [Fact]
    public async Task PlaceOrder_WhenTheResponderRefusesStockUnavailable_Returns409WithShortages()
    {
        var (gateway, rpc, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        rpc.Handler = (_, _) => throw new OrderToCash.Gateway.Application.Ports.RpcBusinessError(
            GatewaySubjects.OrdersCreate,
            "STOCK_UNAVAILABLE",
            "insufficient stock",
            new Dictionary<string, object?> { ["shortages"] = new[] { new { productCode = "PRD-0001", requested = 5, available = 2 } } });

        var response = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = new[] { new { productCode = "PRD-0001", quantity = 5 } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"STOCK_UNAVAILABLE\"", body);
        Assert.Contains("shortages", body);
    }

    /// <summary>The upstream RPC transport is unreachable — 503, never a 500, and the Problem body says which kind (UPSTREAM_UNAVAILABLE).</summary>
    [Fact]
    public async Task PlaceOrder_WhenTheRpcTransportIsUnreachable_Returns503UpstreamUnavailable()
    {
        var (gateway, rpc, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        rpc.Handler = (_, _) => throw new OrderToCash.Gateway.Application.Ports.RpcTransportError(GatewaySubjects.OrdersCreate, "no responder is subscribed.");

        var response = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = new[] { new { productCode = "PRD-0001", quantity = 5 } } });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"UPSTREAM_UNAVAILABLE\"", body);
    }

    // ── Fix round, review defect D4 — orders.integration.spec.ts guards
    //    #7 had that this feature's first pass had no equivalent for at
    //    ANY level. Ported at HTTP level, over real Kestrel, the same
    //    RecordingRpcClient/InMemoryOrderReadModel substitution seam every
    //    test above already uses. ────────────────────────────────────────

    /// <summary>Ported from #7's <c>orders.integration.spec.ts:211</c> ("R27/R28"). Before this test, <c>POST /orders/{id}/cancel</c> had no HTTP-level test at all — only <c>CancelOrderCommandHandlerTests</c> at the unit level.</summary>
    [Fact]
    public async Task CancelOrder_TranslatesToOrdersCancel_AndReturns202WithCompensationPlanned()
    {
        var (gateway, rpc, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var orderId = Guid.NewGuid();
        rpc.Handler = (subject, _) =>
        {
            Assert.Equal(GatewaySubjects.OrdersCancel, subject);
            return Task.FromResult<object>(new OrdersCancelReplyPayload(orderId, "ORD-000042", "stock_reserved", ["stock_release"], null));
        };

        var response = await gateway.Client.PostAsJsonAsync($"/orders/{orderId}/cancel", new { note = "demo cancel" });

        Assert.Equal((HttpStatusCode)202, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CancelOrderResponseBody>();
        Assert.Equal(["stock_release"], body!.CompensationPlanned);
        Assert.Equal(orderId, rpc.LastMeta!.Value.CorrelationId);
    }

    private sealed record CancelOrderResponseBody(Guid OrderId, string OrderReference, string Status, IReadOnlyList<string> CompensationPlanned);

    /// <summary>Ported from #7's <c>orders.integration.spec.ts:200</c>. Before this test, an empty <c>lines</c> array was proven only at the unit/handler-construction level, never over HTTP, and "before any RPC call" was never proven at all.</summary>
    [Fact]
    public async Task PlaceOrder_WithNoLines_Returns400ValidationFailed_BeforeAnyRpcCall()
    {
        var (gateway, rpc, _, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        rpc.Handler = (_, _) => throw new InvalidOperationException("orders.create must not be called for a request that fails validation.");

        var response = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"VALIDATION_FAILED\"", body);
        Assert.Equal(0, rpc.CallCount);
    }

    /// <summary>Ported from #7's <c>orders.integration.spec.ts:129</c> ("R53/R54"). Before this test, R53's exclusion was proven only at <c>ListOrdersQueryHandlerTests</c>'s unit level — never that the HTTP response body genuinely omits the placeholder.</summary>
    [Fact]
    public async Task ListOrders_ExcludesAPlaceholderDocumentWithNoOrderReferenceYet()
    {
        var (gateway, _, readModel, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var placeholderId = Guid.NewGuid();
        readModel.ListItems = [PlaceholderDocument(placeholderId)];

        var response = await gateway.Client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(placeholderId.ToString(), body);
    }

    /// <summary>Ported from #7's <c>orders.integration.spec.ts:117</c> ("R54"). Before this test, the projected-document shape was proven only at <c>MongoOrderReadModelIntegrationTests</c>'s adapter level — never that the HTTP response body genuinely carries it through.</summary>
    [Fact]
    public async Task GetOrder_ReturnsTheProjectedDocument_OnceOneExistsInTheReadModel()
    {
        var (gateway, _, readModel, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var orderId = Guid.NewGuid();
        readModel.ById = ProjectedDocument(orderId);

        var response = await gateway.Client.GetAsync($"/orders/{orderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        Assert.Equal("ORD-000042", body!["orderReference"].GetString());
        Assert.True(body["headerComplete"].GetBoolean());
        Assert.Equal(1, body["events"].GetArrayLength());
    }

    private static OrderReadModelDocument PlaceholderDocument(Guid orderId) => new(
        orderId,
        OrderReference: null,
        OrderDate: null,
        new OrderReadModelParty(null, null, null),
        new OrderReadModelParty(null, null, null),
        Status: "placed",
        CancellationReason: null,
        Currency: null,
        new OrderReadModelTotals(null, null, null),
        Items: [],
        new OrderReadModelReferences(null, null, null),
        Events: [],
        HeaderComplete: false,
        UpdatedAt: DateTimeOffset.UtcNow);

    private static OrderReadModelDocument ProjectedDocument(Guid orderId) => new(
        orderId,
        "ORD-000042",
        DateTimeOffset.Parse("2026-08-18T09:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture),
        new OrderReadModelParty("CarrefourEs", "Carrefour ES", "8412345000013"),
        new OrderReadModelParty("IBERFOODS", "Iberfoods", "8412345000020"),
        Status: "placed",
        CancellationReason: null,
        Currency: "EUR",
        new OrderReadModelTotals(124950, 0, 124950),
        Items: [new OrderReadModelItem("PRD-0001", null, 5, 24999, 0)],
        new OrderReadModelReferences(null, null, null),
        Events: [new OrderReadModelEvent(Guid.NewGuid(), "order.placed.v1", DateTimeOffset.Parse("2026-08-18T09:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture), "Order placed", null, null)],
        HeaderComplete: true,
        UpdatedAt: DateTimeOffset.UtcNow);
}
