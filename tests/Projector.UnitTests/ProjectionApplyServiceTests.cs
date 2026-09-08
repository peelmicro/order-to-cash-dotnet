using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Projector.Application;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

public sealed class ProjectionApplyServiceTests
{
    private static FactEnvelope Envelope() => new(
        Guid.NewGuid(), "order.confirmed.v1", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
        new OrderConfirmedPayload("ORD-1", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow));

    /// <summary><c>PR19</c>: log-and-swallow, never rethrow — the fact is acknowledged rather than retried. Arm by replacing the catch with a rethrow.</summary>
    [Fact]
    public async Task PR19_LogsAndSwallowsASignalPublicationFailure_AcknowledgingTheFactRatherThanRetryingWhatCouldNeverReEmit()
    {
        var writer = new FakeWriter(ProjectionOutcome.Processed);
        var publisher = new ThrowingPublisher();
        var service = new ProjectionApplyService(writer, publisher, NullLogger<ProjectionApplyService>.Instance);

        // Must NOT throw — PR19's whole point.
        await service.ApplyAsync(Envelope(), CancellationToken.None);

        Assert.Equal(1, publisher.CallCount);
    }

    /// <summary><c>PR18</c>: publishes nothing when the writer reports Duplicate — structural, because the writer's own callback is what would publish, and a Duplicate outcome never invokes it.</summary>
    [Fact]
    public async Task PublishesNothingWhenTheWriterReportsDuplicate()
    {
        var writer = new FakeWriter(ProjectionOutcome.Duplicate);
        var publisher = new RecordingPublisher();
        var service = new ProjectionApplyService(writer, publisher, NullLogger<ProjectionApplyService>.Instance);

        await service.ApplyAsync(Envelope(), CancellationToken.None);

        Assert.Equal(0, publisher.CallCount);
    }

    /// <summary>A writer failure propagates so the fact is redelivered — never swallowed.</summary>
    [Fact]
    public async Task RethrowsAWriterFailureSoTheFactIsRedelivered()
    {
        var writer = new ThrowingWriter();
        var publisher = new RecordingPublisher();
        var service = new ProjectionApplyService(writer, publisher, NullLogger<ProjectionApplyService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Envelope(), CancellationToken.None));
        Assert.Equal(0, publisher.CallCount);
    }

    private sealed class FakeWriter(ProjectionOutcome outcome) : IReadModelWriter
    {
        public async Task<ProjectionOutcome> ApplyAsync(ProjectionDelta delta, Guid eventId, Func<ReadModelDocument, CancellationToken, Task> afterApplied, CancellationToken cancellationToken)
        {
            if (outcome == ProjectionOutcome.Processed)
            {
                var document = new ReadModelDocument(delta.OrderId, null, "confirmed", null, null, null, null, null, null, null, null, delta.Entry.EventId, delta.Entry.EventType, delta.Entry.OccurredAt, delta.Entry.Summary, delta.Entry.CausationId);
                await afterApplied(document, cancellationToken);
            }

            return outcome;
        }
    }

    private sealed class ThrowingWriter : IReadModelWriter
    {
        public Task<ProjectionOutcome> ApplyAsync(ProjectionDelta delta, Guid eventId, Func<ReadModelDocument, CancellationToken, Task> afterApplied, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated writer failure");
    }

    private sealed class RecordingPublisher : IUpdateSignalPublisher
    {
        public int CallCount { get; private set; }

        public Task PublishAsync(ReadModelDocument document, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPublisher : IUpdateSignalPublisher
    {
        public int CallCount { get; private set; }

        public Task PublishAsync(ReadModelDocument document, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("simulated NATS publish failure");
        }
    }
}
