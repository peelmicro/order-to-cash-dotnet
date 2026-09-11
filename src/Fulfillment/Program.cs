using Microsoft.Extensions.Hosting;
using OrderToCash.Fulfillment;

// The Fulfillment service host — the FIRST runnable Fulfillment host (feature
// fulfillment_stock). The actual composition (AddFulfillment, AddDispatcher,
// the ValidateOnBuild/ValidateScopes forcing) lives in
// FulfillmentHost.CreateBuilder, factored out the same way OrdersHost is. The
// environment-reading configure delegate itself lives in
// FulfillmentProgramConfiguration.Configure (feature
// composition_root_env_reads_are_unguarded) so a test can call the exact
// method this file calls.
var builder = FulfillmentHost.CreateBuilder(
    args,
    configure: FulfillmentProgramConfiguration.Configure,
    configureTelemetry: FulfillmentProgramConfiguration.ConfigureTelemetry,
    configureHealth: FulfillmentProgramConfiguration.ConfigureHealth);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
