using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// Synchronises only on terminal/monotonic evidence, per design.md §11:
/// every subscription's <c>MoveNextAsync</c> is STARTED (not awaited)
/// before the fact is applied — <c>NATS.Client.Core</c>'s subscription is
/// only actually armed server-side once the enumerable is iterated, so
/// starting the read after the publish would race it and miss the frame
/// (found live by direct probe, see progress/impl).
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class UpdateSignalTests(MongoContainerFixture mongoFixture, NatsContainerFixture natsFixture)
{
    private static async Task<(IAsyncEnumerator<NatsMsg<byte[]>> Enumerator, Task<bool> MoveNextTask)> ArmSubscriptionAsync(NatsConnection connection, string subject)
    {
        var sub = connection.SubscribeAsync<byte[]>(subject);
        var enumerator = sub.GetAsyncEnumerator();
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(300); // let the SUB frame land server-side before anything publishes.
        return (enumerator, moveNextTask);
    }

    private static async Task<NatsMsg<byte[]>> WaitForFrameAsync(IAsyncEnumerator<NatsMsg<byte[]>> enumerator, Task<bool> moveNextTask)
    {
        var got = await moveNextTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(got, "No frame arrived on the armed subscription.");
        return enumerator.Current;
    }

    [Fact]
    public async Task PR17_PublishesOrderUpdatedAndTimelineAppendedOnPerOrderSubjects_ReceivedByBothAWildcardAndASingleOrderSubscriber()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "signal-subjects");
        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderId = Guid.NewGuid();
        var envelope = EnvelopeBuilders.OrderPlaced(correlationId: orderId);

        var (wildcardEnum, wildcardTask) = await ArmSubscriptionAsync(subscriberConnection, "readmodel.order.updated.*");
        var (singleEnum, singleTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.timeline.appended.{orderId:D}");

        await runtime.ApplyAsync(envelope.ToDomain());

        var wildcardMessage = await WaitForFrameAsync(wildcardEnum, wildcardTask);
        var singleMessage = await WaitForFrameAsync(singleEnum, singleTask);

        Assert.Equal($"readmodel.order.updated.{orderId:D}", wildcardMessage.Subject);
        Assert.Equal($"readmodel.timeline.appended.{orderId:D}", singleMessage.Subject);

        await subscriberConnection.DisposeAsync();
    }

    [Fact]
    public async Task PR17_BothPayloadsCarryOpenApisRequiredFields_ReadFromTheContractAsText()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var openapiText = File.ReadAllText(Path.Combine(root, "specs", "shared", "openapi.yaml"));

        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "signal-fields");
        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderId = Guid.NewGuid();

        var (orderEnum, orderTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.order.updated.{orderId:D}");
        var (timelineEnum, timelineTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.timeline.appended.{orderId:D}");

        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId).ToDomain());

        var orderMsg = await WaitForFrameAsync(orderEnum, orderTask);
        var timelineMsg = await WaitForFrameAsync(timelineEnum, timelineTask);

        using var orderJson = JsonDocument.Parse(orderMsg.Data!);
        using var timelineJson = JsonDocument.Parse(timelineMsg.Data!);

        foreach (var field in ExtractRequiredFields(openapiText, "OrderStreamUpdate"))
        {
            Assert.True(orderJson.RootElement.TryGetProperty(field, out _), $"OrderStreamUpdate payload missing required field '{field}'");
        }

        foreach (var field in ExtractRequiredFields(openapiText, "TimelineStreamEntry"))
        {
            Assert.True(timelineJson.RootElement.TryGetProperty(field, out _), $"TimelineStreamEntry payload missing required field '{field}'");
        }

        await subscriberConnection.DisposeAsync();
    }

    /// <summary>Reads the schema's <c>required: [a, b, c]</c> INLINE array (openapi.yaml's own style for every schema in this file) as text — never assumed.</summary>
    private static IReadOnlyList<string> ExtractRequiredFields(string openapiText, string schemaName)
    {
        var schemaIndex = openapiText.IndexOf($"{schemaName}:", StringComparison.Ordinal);
        Assert.True(schemaIndex >= 0, $"'{schemaName}:' not found in openapi.yaml");

        var requiredIndex = openapiText.IndexOf("required: [", schemaIndex, StringComparison.Ordinal);
        Assert.True(requiredIndex >= 0, $"'required: [...]' not found after '{schemaName}:' in openapi.yaml");

        var lineEnd = openapiText.IndexOf('\n', requiredIndex);
        var line = openapiText[requiredIndex..lineEnd];
        var open = line.IndexOf('[', StringComparison.Ordinal);
        var close = line.IndexOf(']', StringComparison.Ordinal);
        var fields = line[(open + 1)..close].Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();

        Assert.NotEmpty(fields);
        return fields;
    }

    [Fact]
    public async Task R55_EmitsExactlyOneUpdateSignalPairPerAppliedFact_AndNoneAtAllForASuppressedRedelivery()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "signal-exactly-one");
        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderId = Guid.NewGuid();
        var envelope = EnvelopeBuilders.OrderPlaced(correlationId: orderId);

        var (orderEnum, orderTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.order.updated.{orderId:D}");
        var (timelineEnum, timelineTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.timeline.appended.{orderId:D}");

        await runtime.ApplyAsync(envelope.ToDomain());
        await WaitForFrameAsync(orderEnum, orderTask);
        await WaitForFrameAsync(timelineEnum, timelineTask);

        // The SAME eventId again — a suppressed redelivery. Arm a SECOND
        // read and confirm no further frame arrives within a bounded wait.
        var secondOrderTask = orderEnum.MoveNextAsync().AsTask();
        await runtime.ApplyAsync(envelope.ToDomain());

        var completed = await Task.WhenAny(secondOrderTask, Task.Delay(2000));
        Assert.NotSame(secondOrderTask, completed); // the delay won — no frame arrived.

        await subscriberConnection.DisposeAsync();
    }

    [Fact]
    public async Task PR33_TheTimelineAppendedPayloadCarriesCausationId()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "signal-causation");
        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var envelope = EnvelopeBuilders.OrderPlaced(correlationId: orderId, causationId: causationId);

        var (enumerator, task) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.timeline.appended.{orderId:D}");

        await runtime.ApplyAsync(envelope.ToDomain());
        var msg = await WaitForFrameAsync(enumerator, task);

        using var json = JsonDocument.Parse(msg.Data!);
        Assert.Equal(causationId, json.RootElement.GetProperty("causationId").GetGuid());

        await subscriberConnection.DisposeAsync();
    }

    /// <summary>
    /// <c>PR42</c>, ledger <b>L9</b> — the signal carries the POST-apply
    /// status AND references, not the pre-apply ones. Reworded in the fix
    /// round (review advisory A3): the original case applied only
    /// <c>order.confirmed.v1</c>, which changes <c>status</c> but sets no
    /// reference, so the test's own name promised a reference it never
    /// exercised. It now also applies <c>order.despatched.v1</c>, which
    /// changes BOTH <c>status</c> (to <c>despatched</c>) and
    /// <c>references.despatchReference</c> (absent before, present after),
    /// and asserts the second frame carries both post-apply values.
    /// </summary>
    [Fact]
    public async Task PR42_TheOrderUpdatedSignalCarriesThePostApplyStatusAndReferences_NotThePreApplyOnes()
    {
        await using var runtime = await ProjectionRuntime.CreateAsync(mongoFixture, natsFixture, "signal-postapply");
        var subscriberConnection = new NatsConnection(new NatsOpts { Url = natsFixture.Url });
        await subscriberConnection.ConnectAsync();

        var orderId = Guid.NewGuid();
        await runtime.ApplyAsync(EnvelopeBuilders.OrderPlaced(correlationId: orderId).ToDomain());

        var (enumerator, task) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.order.updated.{orderId:D}");

        // This fact RAISES the status from "placed" to "confirmed" — the
        // signal must carry "confirmed", the value AFTER this apply, never
        // the value the document held before it.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderConfirmed(correlationId: orderId).ToDomain());

        var msg = await WaitForFrameAsync(enumerator, task);
        using var json = JsonDocument.Parse(msg.Data!);
        Assert.Equal("confirmed", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("references").TryGetProperty("despatchReference", out _), "No despatchReference exists yet — nulls are omitted on the wire.");

        var (secondEnumerator, secondTask) = await ArmSubscriptionAsync(subscriberConnection, $"readmodel.order.updated.{orderId:D}");

        // This fact RAISES the status from "confirmed" to "despatched" AND
        // sets a reference that was absent before it — the signal must
        // carry BOTH post-apply values, never the pre-apply ones.
        await runtime.ApplyAsync(EnvelopeBuilders.OrderDespatched(correlationId: orderId, despatchReference: "DES-POSTAPPLY").ToDomain());

        var secondMsg = await WaitForFrameAsync(secondEnumerator, secondTask);
        using var secondJson = JsonDocument.Parse(secondMsg.Data!);
        Assert.Equal("despatched", secondJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("DES-POSTAPPLY", secondJson.RootElement.GetProperty("references").GetProperty("despatchReference").GetString());

        await subscriberConnection.DisposeAsync();
    }
}
