using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BI16` — the bare-JSON wire on both new subjects, a trilogy contract rather than a framework artefact (design.md §4.3).</summary>
[Collection(BillingCollection.Name)]
public sealed class InvoiceWireTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Theory]
    [InlineData(InvoiceSubjects.InvoiceIssue)]
    [InlineData(InvoiceSubjects.InvoiceList)]
    public async Task BI16_AnswersABareJsonRequestFromARawNatsClientWithABareJsonReplyOnBothInvoiceSubjects_AndABareJsonRpcErrorOnARefusal(string subject)
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, $"invoice-wire-{subject.Replace('.', '-')}");
        using var hostDisposer = host;

        var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        const string orderReference = "ORD-000301";
        await BillingHostFixture.SeedLedgerEntryAsync(mssql, connectionString, creditId, orderReference, 1_000, "hold");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var (requestBytes, headers) = BuildRequest(subject, orderReference);

        var reply = await BillingHostFixture.RequestBareAsync(connection, subject, requestBytes, headers);

        using var document = JsonDocument.Parse(reply.Data!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.False(root.TryGetProperty("response", out _), "the reply must not carry a framework packet's 'response' key.");
        Assert.False(root.TryGetProperty("isDisposed", out _));
        Assert.False(root.TryGetProperty("id", out _));
        Assert.False(root.TryGetProperty("$type", out _));

        var keySet = root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var replyKeySet = subject == InvoiceSubjects.InvoiceIssue
            ? new HashSet<string>(StringComparer.Ordinal) { "orderReference", "invoiceReference", "invoiceDate", "currency", "totalAmount", "status", "created" }
            : new HashSet<string>(StringComparer.Ordinal) { "items", "page" };
        Assert.Equal(replyKeySet, keySet);

        await host.StopAsync();
    }

    [Fact]
    public async Task BI16_AnswersABareJsonRpcError_OnARefusal()
    {
        var (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "invoice-wire-error");
        using var _disposeHost = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var malformed = new InvoiceIssueRequestPayload("ORD-000302", "CarrefourEs", "IBERFOODS", "EUR", []);
        var reply = await BillingHostFixture.RequestBareAsync(connection, InvoiceSubjects.InvoiceIssue, RpcJson.Serialize(malformed), BuildHeaders());

        using var document = JsonDocument.Parse(reply.Data!);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("$type", out _));
        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("VALIDATION_FAILED", error.Code);

        await host.StopAsync();
    }

    private static (byte[] Payload, NatsHeaders? Headers) BuildRequest(string subject, string orderReference) => subject switch
    {
        InvoiceSubjects.InvoiceIssue => (RpcJson.Serialize(BillingHostFixture.IssueRequest(orderReference, "CarrefourEs", "IBERFOODS", [("SKU-1", 1, 1_000)])), BuildHeaders()),
        InvoiceSubjects.InvoiceList => (RpcJson.Serialize(new InvoiceListRequestPayload(1, 25)), null),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static NatsHeaders BuildHeaders() => new()
    {
        { "x-correlation-id", Guid.NewGuid().ToString() },
        { "x-request-id", Guid.NewGuid().ToString() },
    };
}
