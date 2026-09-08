using System.Text.Json;
using NATS.Client.Core;
using OrderToCash.Contracts.Wire;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Application.Signals;

namespace OrderToCash.Projector.Infrastructure.Signal;

/// <summary>
/// <c>PR17</c> — two core NATS publishes per applied fact, on
/// <c>readmodel.order.updated.&lt;orderId&gt;</c> and
/// <c>readmodel.timeline.appended.&lt;orderId&gt;</c>, over the shared
/// singleton <see cref="INatsConnection"/>. Fire-and-forget: no
/// <c>RequestAsync</c>, no reply subject, nothing awaited beyond the
/// publish itself. Each publish builds a FRESH <see cref="NatsHeaders"/> —
/// <c>NATS.Client.Core</c>'s own XML documentation says it is not
/// thread-safe and must not be shared across concurrent calls (ledger
/// <b>L39</b>, feature 17's <c>FS2</c>).
/// </summary>
public sealed class NatsUpdateSignalPublisher(INatsConnection connection) : IUpdateSignalPublisher
{
    private const string OrderUpdatedSubjectPrefix = "readmodel.order.updated.";
    private const string TimelineAppendedSubjectPrefix = "readmodel.timeline.appended.";
    private const string CorrelationIdHeader = "x-correlation-id";

    public async Task PublishAsync(ReadModelDocument document, CancellationToken cancellationToken)
    {
        var orderId = document.OrderId.ToString("D");

        var orderUpdate = new OrderStreamUpdate(
            document.EventId,
            document.OrderId,
            document.OrderReference,
            document.Status,
            document.CancellationReason,
            new OrderStreamReferences(document.DespatchReference, document.InvoiceReference, document.PaymentReference),
            document.Currency is null
                ? null
                : new OrderStreamTotals(document.Currency, document.InitialAmount ?? 0, document.InitialDiscount ?? 0, document.TotalAmount ?? 0),
            document.OccurredAt);

        var timelineEntry = new TimelineStreamEntry(
            document.EventId,
            document.CausationId,
            document.OrderId,
            document.OrderReference,
            document.EventType,
            document.OccurredAt,
            document.Summary);

        await connection.PublishAsync(
            OrderUpdatedSubjectPrefix + orderId,
            JsonSerializer.SerializeToUtf8Bytes(orderUpdate, JsonWire.Options),
            headers: BuildHeaders(orderId),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await connection.PublishAsync(
            TimelineAppendedSubjectPrefix + orderId,
            JsonSerializer.SerializeToUtf8Bytes(timelineEntry, JsonWire.Options),
            headers: BuildHeaders(orderId),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A FRESH instance every call — see the class remarks (ledger L39).</summary>
    private static NatsHeaders BuildHeaders(string orderId) => new() { { CorrelationIdHeader, orderId } };
}
