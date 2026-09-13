using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Domain.Projection;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// R1, API half — "every monetary field of every response the Gateway
/// hands back is an integer minor-units amount accompanied by a currency
/// code". Ported from #7's <c>apps/gateway/src/money-representation.integration.spec.ts</c>
/// plus its <c>test-support/money-field-sweep.ts</c> (<see cref="MoneyFieldSweep"/>
/// here) — a GENERAL black-box sweep, not a per-field assertion list:
/// <see cref="MoneyFieldSweep.SweepForMoneyFields"/> walks the real HTTP
/// response body and DISCOVERS every field it recognises as monetary by
/// shape. Nothing here names <c>totalAmount</c>, <c>creditLimit</c>, etc.
/// as an expectation — a future field renamed or added to the wire is
/// swept automatically.
///
/// Boot pattern: real Kestrel, real socket round trip, but a FAKE
/// <see cref="IRpcClient"/>/<see cref="IOrderReadModel"/> — the SAME seam
/// <see cref="OrdersHttpTests"/>/<see cref="InvoicesHttpTests"/> already
/// establish for "this is about the Gateway's OWN wire shaping, never
/// another service's behaviour" tests. Deliberately NOT #7's boot (a real
/// spawned NestJS app plus real Mongo/NATS with stub RPC responders) —
/// see progress/impl_test_matrix_rows_that_outlived_their_named_closer.md
/// for why the substitution is faithful to what R1 actually claims.
/// </summary>
public sealed class MoneyRepresentationHttpTests
{
    private static readonly Regex _isoCurrencyShape = new("^[A-Z]{3}$", RegexOptions.Compiled);

    private sealed class RecordingRpcClient : IRpcClient
    {
        private readonly Dictionary<string, object> _replies = new();

        public void EnqueueReply(string subject, object reply) => _replies[subject] = reply;

        public Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken)
        {
            if (!_replies.TryGetValue(subject, out var reply))
            {
                throw new InvalidOperationException($"RecordingRpcClient has no reply configured for subject '{subject}'.");
            }

            return Task.FromResult((TReply)reply);
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

    private static OrderReadModelDocument SeedOrderDocForSweep(Guid orderId)
    {
        var now = DateTimeOffset.Parse("2026-08-18T09:00:00.000Z");
        return new OrderReadModelDocument(
            orderId,
            "ORD-000777",
            now,
            new OrderReadModelParty("CarrefourEs", "Carrefour ES", "8412345000013"),
            new OrderReadModelParty("IBERFOODS", "Iberfoods", "8412345000020"),
            "placed",
            null,
            "EUR",
            // Nested one level down (`totals` carries no `currency` of its
            // own — exactly the ancestor-inherited pattern this sweep must
            // catch).
            new OrderReadModelTotals(217_450, 3_500, 213_950),
            // Nested inside an array (`items[]` entries carry no
            // `currency` of their own either).
            [new OrderReadModelItem("PRD-0001", null, 5, 43_490, 700)],
            new OrderReadModelReferences(null, null, null),
            [new OrderReadModelEvent(Guid.NewGuid(), "order.placed.v1", now, "Order placed", null, null)],
            true,
            now);
    }

    private static async Task<(GatewayTestHost Gateway, RecordingRpcClient Rpc, InMemoryOrderReadModel ReadModel)> StartAuthenticatedAsync()
    {
        var rpc = new RecordingRpcClient();
        var readModel = new InMemoryOrderReadModel();

        var gateway = await GatewayTestHost.StartAsync(
            options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_money_it_unused";
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

        return (gateway, rpc, readModel);
    }

    /// <summary>Every finding in <paramref name="findings"/> must be an integer amount with an ISO-4217-shaped currency — the two clauses R1's wording actually makes.</summary>
    private static void AssertEveryFindingIsIntegerMinorUnitsWithCurrency(string endpointLabel, IReadOnlyList<MoneyFinding> findings)
    {
        Assert.True(
            findings.Count > 0,
            $"expected at least one monetary field discovered in {endpointLabel}'s response — found none, which would make this sweep vacuous rather than proving R1");

        foreach (var finding in findings)
        {
            var isInteger = finding.Amount.ValueKind == JsonValueKind.Number && finding.Amount.TryGetInt64(out _);
            Assert.True(
                isInteger,
                $"{endpointLabel} {finding.Path}: R1 requires an integer count of minor units, got {finding.Amount.GetRawText()} (kind {finding.Amount.ValueKind})");

            var currencyOk = finding.Currency is { ValueKind: JsonValueKind.String } currency
                && _isoCurrencyShape.IsMatch(currency.GetString() ?? string.Empty);
            Assert.True(
                currencyOk,
                $"{endpointLabel} {finding.Path}: R1 requires an ISO 4217 alpha-3 currency code accompanying the amount, got {(finding.Currency is { } c ? c.GetRawText() : "undefined")}");
        }
    }

    [Fact]
    public async Task EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode()
    {
        var (gateway, rpc, readModel) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var findingsByEndpoint = new Dictionary<string, IReadOnlyList<MoneyFinding>>();

        // ── POST /orders — orders.create is stubbed; the Gateway's own
        //    translation of THAT reply into the wire response is what is
        //    under test. ───────────────────────────────────────────────
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.OrdersCreate, new OrdersCreateReplyPayload(
            orderId, "ORD-000778", "placed", "EUR", 217_450, 3_500, 213_950, DateTimeOffset.Parse("2026-08-18T10:00:00.000Z")));

        var placeResponse = await gateway.Client.PostAsJsonAsync(
            "/orders",
            new { retailerCode = "CarrefourEs", companyCode = "IBERFOODS", currency = "EUR", lines = new[] { new { productCode = "PRD-0001", quantity = 5 } } });
        placeResponse.EnsureSuccessStatusCode();
        findingsByEndpoint["POST /orders"] = MoneyFieldSweep.SweepForMoneyFields(
            JsonDocument.Parse(await placeResponse.Content.ReadAsStringAsync()).RootElement);

        // ── GET /orders/{id} and GET /orders — both read directly off the
        //    fake read model (R54's seam — MongoOrderReadModelIntegrationTests
        //    already proves the real Mongo query; this file's own claim is
        //    about SHAPE, which the Gateway alone decides once it has the
        //    document). ───────────────────────────────────────────────────
        var seededOrderId = Guid.NewGuid();
        var doc = SeedOrderDocForSweep(seededOrderId);
        readModel.ById = doc;
        readModel.ListItems = [doc];

        var detailResponse = await gateway.Client.GetAsync($"/orders/{seededOrderId}");
        detailResponse.EnsureSuccessStatusCode();
        findingsByEndpoint["GET /orders/{id}"] = MoneyFieldSweep.SweepForMoneyFields(
            JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync()).RootElement);

        var listResponse = await gateway.Client.GetAsync("/orders");
        listResponse.EnsureSuccessStatusCode();
        findingsByEndpoint["GET /orders"] = MoneyFieldSweep.SweepForMoneyFields(
            JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement);

        // ── GET /invoices — billing.invoice.list is stubbed. ────────────
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload(
            [
                new InvoiceViewPayload(
                    Guid.NewGuid(), "INV-000091", DateTimeOffset.Parse("2026-08-18T09:00:00.000Z"), "ORD-000778",
                    "CarrefourEs", "IBERFOODS", "EUR", 217_450, 3_500, 213_950, "issued", null, null),
            ],
            new InvoicePageInfo(1, 25, 1)));

        var invoicesResponse = await gateway.Client.GetAsync("/invoices");
        invoicesResponse.EnsureSuccessStatusCode();
        findingsByEndpoint["GET /invoices"] = MoneyFieldSweep.SweepForMoneyFields(
            JsonDocument.Parse(await invoicesResponse.Content.ReadAsStringAsync()).RootElement);

        // ── GET /credits — billing.credit.list is stubbed. Note the four
        //    money fields here (creditLimit/activeHolds/openExposure/
        //    availableCredit) share none of the vocabulary /orders or
        //    /invoices use — exactly the case a name-based recogniser
        //    would have missed and the shape-based one does not. ─────────
        rpc.EnqueueReply(GatewaySubjects.CreditList, new CreditListReplyPayload(
            [new CreditViewPayload("CR-000001", "CarrefourEs", "IBERFOODS", "EUR", 1_000_000, 213_950, 0, 786_050)],
            new CreditPageInfo(1, 25, 1)));

        var creditsResponse = await gateway.Client.GetAsync("/credits");
        creditsResponse.EnsureSuccessStatusCode();
        findingsByEndpoint["GET /credits"] = MoneyFieldSweep.SweepForMoneyFields(
            JsonDocument.Parse(await creditsResponse.Content.ReadAsStringAsync()).RootElement);

        // ── The assertion, applied uniformly, per endpoint. ──────────────
        foreach (var (endpointLabel, findings) in findingsByEndpoint)
        {
            AssertEveryFindingIsIntegerMinorUnitsWithCurrency(endpointLabel, findings);
        }
    }
}
