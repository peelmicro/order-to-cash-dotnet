using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Presentation;

/// <summary>
/// The <c>orders.create</c> NATS responder — ONE <see cref="BackgroundService"/>
/// subscribing to ONE transport (CLAUDE.md: "One BackgroundService per
/// transport"), the inbound half of the RPC pair this feature builds (the
/// outbound half, <c>fulfillment.stock.check</c>, is
/// <c>NatsStockAvailabilityChecker</c> in <c>Infrastructure/Messaging/</c>).
/// </summary>
/// <remarks>
/// <see cref="IDispatcher"/> is registered scoped (a singleton would
/// capture the DI root provider and every handler resolved through it would
/// resolve from root instead of the caller's scope — silent in Production,
/// one captive <c>DbContext</c> per process). This responder is itself a
/// singleton <see cref="BackgroundService"/>, so it creates ONE
/// <see cref="IServiceScope"/> PER inbound request and resolves
/// <see cref="IDispatcher"/> from it — never once at construction.
/// Processing is deliberately sequential (one request handled fully before
/// the next <c>SubscribeAsync</c> iteration is awaited): the order-number
/// allocator already serialises every placing transaction behind its
/// exclusive row lock (design.md's own accepted throughput ceiling, D7 in
/// #7's review), so a concurrent responder would not raise placement
/// throughput — it would only let unrelated requests (a future
/// <c>orders.cancel</c>, say) interleave, which this feature does not yet
/// have. Revisit if a later feature adds a second concurrent RPC subject
/// this responder must not block.
/// </remarks>
public sealed class OrdersCreateResponder(
    INatsConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<OrdersCreateResponder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(RpcSubjects.OrdersCreate, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            await HandleAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(NatsMsg<byte[]> message, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        try
        {
            if (message.Data is null)
            {
                throw new InvalidOperationException("orders.create request carried no payload.");
            }

            var request = RpcJson.Deserialize<OrdersCreateRequestPayload>(message.Data);
            OrdersCreateRequestValidator.Validate(request);
            var command = ToCommand(request);

            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var result = await dispatcher.SendAsync<PlaceOrderCommand, PlaceOrderResult>(command, stoppingToken).ConfigureAwait(false);

            await message.ReplyAsync(RpcJson.Serialize(ToReplyPayload(result)), cancellationToken: stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "orders.create failed: {Message}", ex.Message);

            var errorPayload = OrdersCreateErrorMapper.Map(ex, clock.UtcNow);
            await message.ReplyAsync(RpcJson.Serialize(errorPayload), cancellationToken: stoppingToken).ConfigureAwait(false);
        }
    }

    private static PlaceOrderCommand ToCommand(OrdersCreateRequestPayload request) =>
        new(
            request.RequestId,
            request.RetailerCode,
            request.CompanyCode,
            request.Currency,
            request.Lines
                .Select(line => new PlaceOrderRequestLine(line.ProductCode, new Quantity(line.Quantity), line.UnitPrice, line.LineDiscount))
                .ToList(),
            request.OrderDiscount,
            request.Notes);

    private static OrdersCreateReplyPayload ToReplyPayload(PlaceOrderResult result) =>
        new(
            OrderId: result.OrderId.Value,
            OrderReference: result.OrderReference.Value,
            Status: "placed",
            Currency: result.Currency,
            InitialAmount: result.InitialAmount.MinorUnits,
            InitialDiscount: result.InitialDiscount.MinorUnits,
            TotalAmount: result.TotalAmount.MinorUnits,
            OrderDate: result.OrderDate);
}
