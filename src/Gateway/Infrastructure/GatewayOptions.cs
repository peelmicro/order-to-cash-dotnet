using OrderToCash.Gateway.Domain.Auth;
using OrderToCash.Gateway.Infrastructure.Auth;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Persistence;
using OrderToCash.Gateway.Infrastructure.RateLimiting;

namespace OrderToCash.Gateway.Infrastructure;

/// <summary>Every configuration knob <see cref="GatewayServiceCollectionExtensions.AddGateway"/> needs — the <c>NotificationsOptions</c>/<c>ProjectorOptions</c> shape.</summary>
public sealed class GatewayOptions
{
    public NatsOptions Nats { get; } = new();

    public GatewayMongoOptions Mongo { get; set; } = new();

    public JwtOptions Jwt { get; set; } = new();

    public LoginThrottleOptions LoginThrottle { get; set; } = new();

    public OperatorIdentity Operator { get; set; } = OperatorIdentityLoader.FromEnvironment();

    /// <summary>How long an order id this gateway just handed out is remembered for the 202-vs-404 distinction (R55) — <see cref="OrderToCash.Gateway.Domain.Orders.IssuedOrderWindow"/>.</summary>
    public TimeSpan IssuedOrderWindowTtl { get; set; } = TimeSpan.FromMinutes(5);

    public int IssuedOrderWindowCapacity { get; set; } = 10_000;

    /// <summary><c>GET /orders/stream</c>'s replay-buffer capacity and ping interval (feature <c>gateway_sse_push</c>, R55) — <see cref="OrderToCash.Gateway.Application.Stream.StreamHub"/>.</summary>
    public GatewaySseOptions Sse { get; set; } = new();
}
