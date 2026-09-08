using OrderToCash.Cqrs;
using OrderToCash.Projector.Domain;

namespace OrderToCash.Projector.Application.Commands;

/// <summary>
/// ONE command carrying the domain <see cref="FactEnvelope"/> (<c>PR27</c>)
/// — not fourteen. The projector's behaviour does not vary with
/// <c>eventType</c>: every one of the fourteen variations lives inside the
/// pure <see cref="FactProjection.Project(FactEnvelope)"/>.
/// </summary>
public sealed record ProjectFactCommand(FactEnvelope Envelope) : ICommand;
