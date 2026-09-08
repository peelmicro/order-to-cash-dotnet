using Microsoft.Extensions.Hosting;
using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure;

// The Projector service host — the fifth Kafka fact consumer, the only
// runtime writer of otc_read_model.order_timeline. Reads exactly what every
// other #8 host reads: the shared infrastructure coordinates from the
// environment, nothing new (design.md §2.2 — no .env.example change).
var builder = ProjectorHost.CreateBuilder(
    args,
    configure: options =>
    {
        options.Kafka.BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS")
            ?? $"localhost:{Environment.GetEnvironmentVariable("KAFKA_HOST_PORT") ?? "9092"}";

        options.Nats.Url = Environment.GetEnvironmentVariable("NATS_URL")
            ?? $"nats://localhost:{Environment.GetEnvironmentVariable("NATS_CLIENT_HOST_PORT") ?? "4222"}";

        options.Mongo = ProjectorMongoOptions.FromEnvironment();
    });

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
