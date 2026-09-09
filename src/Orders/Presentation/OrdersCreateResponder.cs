using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Queries;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Presentation;

/// <summary>
/// The Orders NATS RPC responder — ONE <see cref="BackgroundService"/>
/// subscribing to ONE transport (CLAUDE.md: "One BackgroundService per
/// transport"), THREE subjects: <c>orders.create</c> (the inbound half of the
/// RPC pair feature <c>orders_acceptance</c> builds — the outbound half,
/// <c>fulfillment.stock.check</c>, is <c>NatsStockAvailabilityChecker</c> in
/// <c>Infrastructure/Messaging/</c>), since feature
/// <c>orders_catalog_responder</c>, <c>catalog.reference.list</c>, and since
/// feature <c>orders_cancel_responder</c>, <c>orders.cancel</c>. Extended
/// rather than forked into a second responder class — matching
/// <c>BillingRpcResponder</c>'s own precedent (its remarks: "CLAUDE.md's
/// non-negotiable is one BackgroundService per transport"), one concurrent
/// <see cref="Task"/> loop per subject via <see cref="Task.WhenAll(Task[])"/>.
/// </summary>
/// <remarks>
/// <see cref="IDispatcher"/> is registered scoped (a singleton would
/// capture the DI root provider and every handler resolved through it would
/// resolve from root instead of the caller's scope — silent in Production,
/// one captive <c>DbContext</c> per process). This responder is itself a
/// singleton <see cref="BackgroundService"/>, so it creates ONE
/// <see cref="IServiceScope"/> PER inbound request and resolves
/// <see cref="IDispatcher"/> from it — never once at construction. Each
/// subject's own loop processes its requests sequentially (one request
/// handled fully before that loop's next <c>SubscribeAsync</c> iteration is
/// awaited): <c>orders.create</c>'s reasoning is unchanged from before this
/// feature (the order-number allocator already serialises every placing
/// transaction behind its exclusive row lock — design.md's own accepted
/// throughput ceiling, D7 in #7's review), and <c>catalog.reference.list</c>
/// is a read-only query with nothing to serialise, so running its own loop
/// concurrently with (never blocked by) <c>orders.create</c>'s is exactly
/// what two independent <c>Task.WhenAll</c> loops give for free.
/// </remarks>
public sealed class OrdersCreateResponder(
    INatsConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<OrdersCreateResponder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new[]
        {
            SubscribeOrdersCreateLoopAsync(stoppingToken),
            SubscribeCatalogReferenceListLoopAsync(stoppingToken),
            SubscribeOrdersCancelLoopAsync(stoppingToken),
        };

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private async Task SubscribeOrdersCreateLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(RpcSubjects.OrdersCreate, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            await HandleOrdersCreateAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SubscribeCatalogReferenceListLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(RpcSubjects.CatalogReferenceList, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            await HandleCatalogReferenceListAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SubscribeOrdersCancelLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(RpcSubjects.OrdersCancel, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            await HandleOrdersCancelAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task HandleOrdersCreateAsync(NatsMsg<byte[]> message, CancellationToken stoppingToken)
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

    private async Task HandleCatalogReferenceListAsync(NatsMsg<byte[]> message, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        try
        {
            if (message.Data is null)
            {
                throw new InvalidCatalogReferenceListRequestError("catalog.reference.list request carried no payload.");
            }

            var request = RpcJson.Deserialize<CatalogReferenceListRequestPayload>(message.Data);
            CatalogReferenceListRequestValidator.Validate(request);

            var kinds = request.Kinds is { Count: > 0 } ? request.Kinds : CatalogReferenceKinds.All;
            var includeDisabled = request.IncludeDisabled ?? false;

            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var result = await dispatcher
                .QueryAsync<ListCatalogReferenceQuery, CatalogReferenceListResult>(new ListCatalogReferenceQuery(kinds, includeDisabled), stoppingToken)
                .ConfigureAwait(false);

            await message.ReplyAsync(RpcJson.Serialize(ToReplyPayload(result)), cancellationToken: stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "catalog.reference.list failed: {Message}", ex.Message);

            var errorPayload = OrdersCreateErrorMapper.Map(ex, clock.UtcNow);
            await message.ReplyAsync(RpcJson.Serialize(errorPayload), cancellationToken: stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task HandleOrdersCancelAsync(NatsMsg<byte[]> message, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        try
        {
            if (message.Data is null)
            {
                throw new InvalidOrdersCancelRequestError("orders.cancel request carried no payload.");
            }

            var request = RpcJson.Deserialize<OrdersCancelRequestPayload>(message.Data);
            OrdersCancelRequestValidator.Validate(request);

            var command = new CancelOrderCommand(request.OrderId!.Value, request.Note);

            var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
            var result = await dispatcher.SendAsync<CancelOrderCommand, CancelOrderResult>(command, stoppingToken).ConfigureAwait(false);

            await message.ReplyAsync(RpcJson.Serialize(ToReplyPayload(result)), cancellationToken: stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "orders.cancel failed: {Message}", ex.Message);

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

    private static CatalogReferenceListReplyPayload ToReplyPayload(CatalogReferenceListResult result) =>
        new(
            Products: result.Products?.Select(p => new ProductPayload(p.Code, p.Ean, p.Name, p.Description, p.Price.MinorUnits, p.Price.Currency, p.Enabled)).ToList(),
            Retailers: result.Retailers?.Select(ToPartyPayload).ToList(),
            Companies: result.Companies?.Select(ToPartyPayload).ToList(),
            Currencies: result.Currencies?.Select(c => new CurrencyViewPayload(c.Code, c.IsoNumber, c.Symbol, c.DecimalPoints)).ToList());

    private static OrdersCancelReplyPayload ToReplyPayload(CancelOrderResult result) =>
        new(
            OrderId: result.OrderId,
            OrderReference: result.OrderReference,
            Status: OrderToCash.Orders.Domain.OrderStatuses.ToToken(result.Status),
            CompensationPlanned: result.CompensationPlanned,
            CancellationReason: result.CancellationReason is { } reason ? OrderToCash.Orders.Domain.CancellationReasons.ToToken(reason) : null);

    private static PartyPayload ToPartyPayload(PartyCatalogEntry party) =>
        new(party.Code, party.Name, party.Country, party.Vat, party.Gln.Value, party.Currency, party.Enabled);
}
