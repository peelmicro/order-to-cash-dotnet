using OrderToCash.Cqrs;
using OrderToCash.Notifications.Application.Templates;

namespace OrderToCash.Notifications.Application.Commands;

/// <summary>
/// Seven one-line <see cref="ICommandHandler{TCommand}"/> wrappers over the
/// ONE <see cref="NotificationDispatchService"/> — mirrors Orders'
/// <c>HandleXFactCommandHandler</c> shape exactly. Each closes over its own
/// fact's template builder, so a copy-paste of the wrong builder fails the
/// same guard a deleted dispatch call would (`notify.command-handlers.spec.ts`'s
/// own reasoning, ported here: <c>NotifyFactCommandHandlersTests</c> asserts
/// <c>toHaveBeenCalledWith</c>-equivalent template-specific content, not
/// merely "a send happened").
/// </summary>
public sealed class NotifyOrderPlacedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyOrderPlacedCommand>
{
    public Task HandleAsync(NotifyOrderPlacedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => OrderPlacedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyOrderConfirmedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyOrderConfirmedCommand>
{
    public Task HandleAsync(NotifyOrderConfirmedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => OrderConfirmedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyOrderDespatchedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyOrderDespatchedCommand>
{
    public Task HandleAsync(NotifyOrderDespatchedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => OrderDespatchedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyInvoiceIssuedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyInvoiceIssuedCommand>
{
    public Task HandleAsync(NotifyInvoiceIssuedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => InvoiceIssuedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyPaymentReceivedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyPaymentReceivedCommand>
{
    public Task HandleAsync(NotifyPaymentReceivedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => PaymentReceivedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyOrderCompletedCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyOrderCompletedCommand>
{
    public Task HandleAsync(NotifyOrderCompletedCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => OrderCompletedTemplate.Build(command.Envelope), cancellationToken);
}

public sealed class NotifyOrderCancelledCommandHandler(NotificationDispatchService dispatch) : ICommandHandler<NotifyOrderCancelledCommand>
{
    public Task HandleAsync(NotifyOrderCancelledCommand command, CancellationToken cancellationToken) =>
        dispatch.DispatchAsync(command.Envelope.EventId, () => OrderCancelledTemplate.Build(command.Envelope), cancellationToken);
}
