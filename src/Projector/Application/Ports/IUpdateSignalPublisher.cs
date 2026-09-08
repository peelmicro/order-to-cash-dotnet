namespace OrderToCash.Projector.Application.Ports;

/// <summary>
/// Publishes the two NATS update-signal messages (<c>PR17</c>) for one
/// applied fact. The only method the application layer calls after an
/// apply reports <see cref="ProjectionOutcome.Processed"/> — never for a
/// <see cref="ProjectionOutcome.Duplicate"/> (<c>PR18</c>).
/// </summary>
public interface IUpdateSignalPublisher
{
    Task PublishAsync(ReadModelDocument document, CancellationToken cancellationToken);
}
