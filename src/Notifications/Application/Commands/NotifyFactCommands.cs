using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Cqrs;

namespace OrderToCash.Notifications.Application.Commands;

/// <summary>
/// One command per notified fact (domain-model.md §7.3) — the explicit-
/// command shape the dispatcher ruling requires (CLAUDE.md). Each carries
/// the envelope, typed to its own payload — nothing about routing or
/// idempotency lives here, only the data a handler needs to render and
/// dispatch one email.
/// </summary>
public sealed record NotifyOrderPlacedCommand(Envelope<OrderPlacedPayload> Envelope) : ICommand;

public sealed record NotifyOrderConfirmedCommand(Envelope<OrderConfirmedPayload> Envelope) : ICommand;

public sealed record NotifyOrderDespatchedCommand(Envelope<OrderDespatchedPayload> Envelope) : ICommand;

public sealed record NotifyInvoiceIssuedCommand(Envelope<InvoiceIssuedPayload> Envelope) : ICommand;

public sealed record NotifyPaymentReceivedCommand(Envelope<PaymentReceivedPayload> Envelope) : ICommand;

public sealed record NotifyOrderCompletedCommand(Envelope<OrderCompletedPayload> Envelope) : ICommand;

public sealed record NotifyOrderCancelledCommand(Envelope<OrderCancelledPayload> Envelope) : ICommand;
