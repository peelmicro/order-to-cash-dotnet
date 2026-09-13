using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC2` — the bare-JSON wire, a trilogy contract rather than a framework artefact (design.md §4.3).</summary>
[Collection(BillingCollection.Name)]
public sealed class CreditWireTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Theory]
    [InlineData(CreditSubjects.CreditHold)]
    [InlineData(CreditSubjects.CreditRelease)]
    [InlineData(CreditSubjects.CreditList)]
    public async Task BC2_AnswersABareJsonRequestWithABareJsonReply_OnAllThreeSubjects(string subject)
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, $"wire-{subject.Replace('.', '-')}");
        using var hostDisposer = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var (requestBytes, headers) = BuildRequest(subject);

        var reply = await BillingHostFixture.RequestBareAsync(connection, subject, requestBytes, headers);

        using var document = JsonDocument.Parse(reply.Data!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.False(root.TryGetProperty("response", out _), "the reply must not carry a framework packet's 'response' key.");
        Assert.False(root.TryGetProperty("isDisposed", out _));
        Assert.False(root.TryGetProperty("id", out _));

        await host.StopAsync();
    }

    [Fact]
    public async Task BC2_AnswersABareJsonRpcError_OnAValidationFailure()
    {
        var (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "wire-error");
        using var _disposeHost = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var malformed = RpcJson.Serialize(new CreditListRequestPayload(0, 0));
        var reply = await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditList, malformed);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("VALIDATION_FAILED", error.Code);

        await host.StopAsync();
    }

    private static (byte[] Payload, NatsHeaders? Headers) BuildRequest(string subject) => subject switch
    {
        CreditSubjects.CreditHold => (RpcJson.Serialize(new CreditHoldRequestPayload("ORD-000099", "CarrefourEs", "IBERFOODS", new CreditMoney(500, "EUR"))), BuildHeaders()),
        CreditSubjects.CreditRelease => (RpcJson.Serialize(new CreditReleaseRequestPayload("ORD-000098", "CarrefourEs", "IBERFOODS")), BuildHeaders()),
        CreditSubjects.CreditList => (RpcJson.Serialize(new CreditListRequestPayload(1, 25)), null),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static NatsHeaders BuildHeaders() => new()
    {
        { "x-correlation-id", Guid.NewGuid().ToString() },
        { "x-request-id", Guid.NewGuid().ToString() },
    };
}
