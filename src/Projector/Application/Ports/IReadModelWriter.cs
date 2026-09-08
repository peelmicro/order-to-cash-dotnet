using OrderToCash.Projector.Domain;

namespace OrderToCash.Projector.Application.Ports;

/// <summary>Whether the fact applied (and this delivery ran the post-apply callback), or was already applied and this delivery did nothing.</summary>
public enum ProjectionOutcome
{
    Processed,
    Duplicate,
}

/// <summary>
/// The ONLY write surface the application layer sees — it never sees a
/// pipeline, a <c>BsonDocument</c> or any MongoDB.Driver type (<c>PR28</c>).
/// One method: apply one fact's delta, idempotently, and — only when it was
/// genuinely applied — invoke <paramref name="afterApplied"/> exactly once
/// with the post-apply document description, before returning.
/// </summary>
public interface IReadModelWriter
{
    /// <summary>
    /// Applies <paramref name="delta"/> to the read model in exactly two
    /// MongoDB operations (<c>PR6</c>): a placeholder upsert, then one
    /// filtered <c>findAndModify</c> whose filter IS the idempotency check.
    /// <paramref name="afterApplied"/> runs exactly once, only on
    /// <see cref="ProjectionOutcome.Processed"/>, with the document as it
    /// stood immediately after the apply (<c>PR42</c> — never the pre-apply
    /// state).
    /// </summary>
    Task<ProjectionOutcome> ApplyAsync(
        ProjectionDelta delta,
        Guid eventId,
        Func<ReadModelDocument, CancellationToken, Task> afterApplied,
        CancellationToken cancellationToken);
}

/// <summary>
/// The post-apply document, translated to a store-agnostic shape for the
/// application layer's own use (building the two update-signal payloads) —
/// never a <c>BsonDocument</c>.
/// </summary>
public sealed record ReadModelDocument(
    Guid OrderId,
    string? OrderReference,
    string Status,
    string? CancellationReason,
    string? DespatchReference,
    string? InvoiceReference,
    string? PaymentReference,
    long? InitialAmount,
    long? InitialDiscount,
    long? TotalAmount,
    string? Currency,
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Summary,
    Guid CausationId);
