using Microsoft.Extensions.Hosting;
using OrderToCash.Projector;

// The Projector service host — the fifth Kafka fact consumer, the only
// runtime writer of otc_read_model.order_timeline. Reads exactly what every
// other #8 host reads: the shared infrastructure coordinates from the
// environment, nothing new (design.md §2.2 — no .env.example change). The
// environment-reading configure delegate itself lives in
// ProjectorProgramConfiguration.Configure (feature
// composition_root_env_reads_are_unguarded) so a test can call the exact
// method this file calls.
var builder = ProjectorHost.CreateBuilder(
    args,
    configure: ProjectorProgramConfiguration.Configure,
    configureTelemetry: ProjectorProgramConfiguration.ConfigureTelemetry,
    configureHealth: ProjectorProgramConfiguration.ConfigureHealth);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
