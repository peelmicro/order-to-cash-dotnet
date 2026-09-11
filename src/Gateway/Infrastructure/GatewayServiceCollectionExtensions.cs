using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Application.Stream;
using OrderToCash.Gateway.Domain.Auth;
using OrderToCash.Gateway.Domain.Orders;
using OrderToCash.Gateway.Infrastructure.Auth;
using OrderToCash.Gateway.Infrastructure.Clock;
using OrderToCash.Gateway.Infrastructure.Health;
using OrderToCash.Gateway.Infrastructure.Messaging;
using OrderToCash.Gateway.Infrastructure.Persistence;
using OrderToCash.Gateway.Infrastructure.RateLimiting;

namespace OrderToCash.Gateway.Infrastructure;

/// <summary><c>AddGateway(IServiceCollection, GatewayOptions)</c> — one explicit registration line per port, no assembly scan (CLAUDE.md: "Explicit DI registration"). Takes the already-built <see cref="GatewayOptions"/> (rather than a configure delegate, unlike the other services' <c>AddXxx</c> shape) because <c>GatewayHost</c> also needs the SAME instance to register the login rate limiter, which is not a port this method owns.</summary>
public static class GatewayServiceCollectionExtensions
{
    public static IServiceCollection AddGateway(this IServiceCollection services, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IOptions<NatsOptions>>(Options.Create(options.Nats));
        services.AddSingleton<IOptions<JwtOptions>>(Options.Create(options.Jwt));
        services.AddSingleton(options.LoginThrottle);
        services.AddSingleton(options.Operator);

        services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts { Url = options.Nats.Url }));

        services.AddSingleton<IMongoClient>(_ => new MongoClient(options.Mongo.ConnectionUri));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>()
            .GetDatabase(options.Mongo.Database)
            .GetCollection<BsonDocument>(GatewayReadModelCollection.Name));

        // R60/OR6, design.md §8.2 — the Gateway checks rpcTransport (NATS)
        // and readModel (MongoDB), mapped into the EXISTING WebApplication
        // pipeline by HealthEndpoints.MapHealthEndpoints (GatewayHost.Configure),
        // never a separate port the other five services need.
        services.AddSingleton(options.Mongo);
        services.AddSingleton<Application.Ports.IHealthCheck, NatsHealthCheck>();
        services.AddSingleton<Application.Ports.IHealthCheck, MongoHealthCheck>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(sp =>
        {
            var clock = sp.GetRequiredService<IClock>();
            return new IssuedOrderWindow(() => clock.UtcNow, options.IssuedOrderWindowTtl, options.IssuedOrderWindowCapacity);
        });

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IRpcClient, NatsRpcClient>();
        services.AddSingleton<IOrderReadModel, MongoOrderReadModel>();

        services.AddSingleton<IOptions<GatewaySseOptions>>(Options.Create(options.Sse));
        services.AddSingleton(sp =>
        {
            var clock = sp.GetRequiredService<IClock>();
            return new StreamHub(() => clock.UtcNow, options.Sse.BufferCapacity);
        });
        services.AddHostedService<NatsStreamSignalSubscriber>();

        return services;
    }
}
