using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>One command per consumed fact — the explicit-command shape the dispatcher ruling requires (CLAUDE.md). Each carries the parsed <see cref="SagaFact"/> only.</summary>
public sealed record HandleOrderPlacedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleStockReservedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleStockRejectedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleCreditApprovedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleCreditRejectedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleStockReleasedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleOrderDespatchedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleInvoiceIssuedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandlePaymentReceivedFactCommand(SagaFact Fact) : ICommand;

public sealed record HandleCreditReleasedFactCommand(SagaFact Fact) : ICommand;

/// <summary>
/// <c>FactCommandFor(eventType)</c> — routes a well-formed, consumed fact to
/// the closure that dispatches its own command through
/// <see cref="IDispatcher.SendAsync{TCommand}"/> (design.md §3.5). Returns a
/// delegate rather than a boxed <see cref="ICommand"/> so the caller needs no
/// reflection to reach the generic <c>SendAsync&lt;TCommand&gt;</c> — every
/// branch below closes over its own concrete command type at compile time.
/// The four self-produced facts (SO2) map to <see langword="null"/>, exactly
/// like an uncatalogued <c>eventType</c> — <c>SagaFactsConsumer</c> filters
/// the former out before this is ever consulted (design.md §3.5), so this is
/// the belt-and-braces second layer for both.
/// </summary>
public static class SagaFactCommands
{
    public static Func<IDispatcher, SagaFact, CancellationToken, Task>? FactCommandFor(string eventType) => eventType switch
    {
        "order.placed.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleOrderPlacedFactCommand(fact), ct),
        "stock.reserved.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleStockReservedFactCommand(fact), ct),
        "stock.rejected.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleStockRejectedFactCommand(fact), ct),
        "credit.approved.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleCreditApprovedFactCommand(fact), ct),
        "credit.rejected.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleCreditRejectedFactCommand(fact), ct),
        "stock.released.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleStockReleasedFactCommand(fact), ct),
        "order.despatched.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleOrderDespatchedFactCommand(fact), ct),
        "invoice.issued.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleInvoiceIssuedFactCommand(fact), ct),
        "payment.received.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandlePaymentReceivedFactCommand(fact), ct),
        "credit.released.v1" => (dispatcher, fact, ct) => dispatcher.SendAsync(new HandleCreditReleasedFactCommand(fact), ct),
        _ => null,
    };
}
