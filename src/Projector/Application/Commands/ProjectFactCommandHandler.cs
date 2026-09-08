using OrderToCash.Cqrs;
using OrderToCash.Projector.Application;

namespace OrderToCash.Projector.Application.Commands;

/// <summary>ONE handler, delegation only — no branching on <c>eventType</c> (<c>PR27</c>). Every one of the fourteen variations lives inside <see cref="ProjectionApplyService"/>'s call to the pure domain projection.</summary>
public sealed class ProjectFactCommandHandler(ProjectionApplyService applyService) : ICommandHandler<ProjectFactCommand>
{
    public Task HandleAsync(ProjectFactCommand command, CancellationToken cancellationToken) =>
        applyService.ApplyAsync(command.Envelope, cancellationToken);
}
