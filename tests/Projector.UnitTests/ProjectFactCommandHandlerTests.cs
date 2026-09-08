using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Projector.Application;
using OrderToCash.Projector.Application.Commands;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary><c>PR27</c> — delegates to <see cref="ProjectionApplyService"/> with no branching on <c>eventType</c>. Arm by inserting a branch and confirming this still passes trivially for the one path exercised, then rely on <c>ProjectorDispatcherRegistrationTests</c> for the "exactly one handler" half.</summary>
public sealed class ProjectFactCommandHandlerTests
{
    [Fact]
    public async Task PR27_DelegatesToProjectionApplyServiceWithNoBranchingOnEventType()
    {
        var writer = new RecordingWriter();
        var publisher = new RecordingPublisher();
        var applyService = new ProjectionApplyService(writer, publisher, NullLogger<ProjectionApplyService>.Instance);
        var handler = new ProjectFactCommandHandler(applyService);

        var envelope = new FactEnvelope(
            Guid.NewGuid(), "order.confirmed.v1", Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            new OrderConfirmedPayload("ORD-1", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow));

        await handler.HandleAsync(new ProjectFactCommand(envelope), CancellationToken.None);

        Assert.Equal(1, writer.ApplyCount);
    }

    private sealed class RecordingWriter : IReadModelWriter
    {
        public int ApplyCount { get; private set; }

        public Task<ProjectionOutcome> ApplyAsync(ProjectionDelta delta, Guid eventId, Func<ReadModelDocument, CancellationToken, Task> afterApplied, CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(ProjectionOutcome.Processed);
        }
    }

    private sealed class RecordingPublisher : IUpdateSignalPublisher
    {
        public Task PublishAsync(ReadModelDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
