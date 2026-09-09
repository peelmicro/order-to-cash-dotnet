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
/// Fix round, review defect D6 — <c>POST /invoices/{id}/payments</c> had NO
/// HTTP-level test at all before this file, so three of
/// <c>ProblemJsonMiddleware.Classify</c>'s ten branches
/// (<c>InvoiceNotFoundError</c>, <c>InvoiceScanBudgetExceededError</c>,
/// <c>OrderNotYetProjectedError</c>) were exercised only at the unit level
/// (<c>RegisterPaymentCommandHandlerTests</c>) — never proven to translate
/// correctly onto the wire. Real Kestrel, real socket round trip, a FAKE
/// <see cref="IRpcClient"/>/<see cref="IOrderReadModel"/> substituted after
/// <c>CreateBuilder</c> — the same seam <c>OrdersHttpTests</c> establishes.
/// </summary>
public sealed class InvoicesHttpTests
{
    private sealed class QueuedRpcClient : IRpcClient
    {
        private readonly Dictionary<string, Queue<object>> _replies = new();
        private readonly Dictionary<string, Exception> _errors = new();

        public void EnqueueReply(string subject, object reply)
        {
            if (!_replies.TryGetValue(subject, out var queue))
            {
                queue = new Queue<object>();
                _replies[subject] = queue;
            }

            queue.Enqueue(reply);
        }

        public void FailAllCallsTo(string subject, Exception error) => _errors[subject] = error;

        public Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken)
        {
            if (_errors.TryGetValue(subject, out var error))
            {
                throw error;
            }

            if (_replies.TryGetValue(subject, out var queue) && queue.Count > 0)
            {
                return Task.FromResult((TReply)queue.Dequeue());
            }

            throw new InvalidOperationException($"QueuedRpcClient has no reply queued for subject '{subject}'.");
        }
    }

    private sealed class InMemoryOrderReadModel : IOrderReadModel
    {
        public OrderReadModelDocument? ByOrderReference { get; set; }

        public Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<OrderReadModelDocument?>(null);

        public Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken) =>
            Task.FromResult(ByOrderReference is not null && ByOrderReference.OrderReference == orderReference ? ByOrderReference : null);

        public Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new OrderListResult([], 0));
    }

    private static InvoiceViewPayload Invoice(Guid invoiceId, string orderReference) => new(
        invoiceId, "INV-000027", DateTimeOffset.UtcNow, orderReference, "CarrefourEs", "IBERFOODS", "EUR", 100, 0, 100, "issued", null, null);

    private static OrderReadModelDocument OrderDocument(Guid orderId, string orderReference) => new(
        orderId, orderReference, DateTimeOffset.UtcNow,
        new OrderReadModelParty("CarrefourEs", null, "8412345000013"),
        new OrderReadModelParty("IBERFOODS", null, "8412345000020"),
        "invoiced", null, "EUR", new OrderReadModelTotals(100, 0, 100), [],
        new OrderReadModelReferences(null, "INV-000027", null), [], true, DateTimeOffset.UtcNow);

    private static async Task<(GatewayTestHost Gateway, QueuedRpcClient Rpc, InMemoryOrderReadModel ReadModel)> StartAuthenticatedAsync()
    {
        var rpc = new QueuedRpcClient();
        var readModel = new InMemoryOrderReadModel();

        var gateway = await GatewayTestHost.StartAsync(
            options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_invoices_it_unused";
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

    private static Task<HttpResponseMessage> PostPaymentAsync(GatewayTestHost gateway, Guid invoiceId) =>
        gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoiceId}/payments",
            new { paymentReference = "PAY-1", amount = new { amount = 100, currency = "EUR" }, valueDate = DateTimeOffset.UtcNow, source = "operator" });

    /// <summary>Ported from #7's own review finding shape (D6) — the genuinely-exhausted scan is a 404, never the 503 the OTHER two branches answer.</summary>
    [Fact]
    public async Task RegisterPayment_WhenTheInvoiceScanIsGenuinelyExhausted_Returns404NotFound()
    {
        var (gateway, rpc, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var invoiceId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(Guid.NewGuid(), "ORD-OTHER")], new InvoicePageInfo(1, 200, 1)));

        var response = await PostPaymentAsync(gateway, invoiceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"NOT_FOUND\"", body);
    }

    /// <summary>The scan hit its page budget while every page fetched was still full — 503, never a false 404 (the invoice may exist beyond the search window).</summary>
    [Fact]
    public async Task RegisterPayment_WhenTheInvoiceScanBudgetIsExceeded_Returns503ScanBudgetExceeded()
    {
        var (gateway, rpc, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var invoiceId = Guid.NewGuid();
        for (var page = 1; page <= 5; page++)
        {
            rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload(
                Enumerable.Range(0, 200).Select(_ => Invoice(Guid.NewGuid(), "ORD-OTHER")).ToList(), new InvoicePageInfo(page, 200, 2000)));
        }

        var response = await PostPaymentAsync(gateway, invoiceId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"SCAN_BUDGET_EXCEEDED\"", body);
    }

    /// <summary>The invoice IS found, but its order has not been projected into the read model yet — 503 UPSTREAM_UNAVAILABLE, never a 404 (the invoice is known to exist).</summary>
    [Fact]
    public async Task RegisterPayment_WhenTheInvoicesOrderIsNotYetProjected_Returns503UpstreamUnavailable()
    {
        var (gateway, rpc, _) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var invoiceId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(invoiceId, "ORD-000042")], new InvoicePageInfo(1, 200, 1)));

        var response = await PostPaymentAsync(gateway, invoiceId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"UPSTREAM_UNAVAILABLE\"", body);
    }

    /// <summary>The happy path, over real Kestrel — the invoice is found, its order IS projected, and billing.payment.register answers accepted.</summary>
    [Fact]
    public async Task RegisterPayment_WhenTheInvoiceAndItsOrderAreBothResolvable_Returns201WithTheReply()
    {
        var (gateway, rpc, readModel) = await StartAuthenticatedAsync();
        await using var disposeGateway = gateway;
        var invoiceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(invoiceId, "ORD-000042")], new InvoicePageInfo(1, 200, 1)));
        rpc.EnqueueReply(GatewaySubjects.PaymentRegister, new PaymentRegisterReplyPayload("accepted", "PAY-1", "INV-000027", "ORD-000042", "paid", DateTimeOffset.UtcNow));
        readModel.ByOrderReference = OrderDocument(orderId, "ORD-000042");

        var response = await PostPaymentAsync(gateway, invoiceId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(orderId.ToString(), response.Headers.GetValues("X-Correlation-Id").Single());
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("accepted", body!["outcome"].ToString());
    }
}
