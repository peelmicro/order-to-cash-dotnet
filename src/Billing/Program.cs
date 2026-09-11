using Microsoft.Extensions.Hosting;
using OrderToCash.Billing;

// The Billing service host — the FIRST runnable Billing host (design.md
// §14.3). The actual composition (AddBilling, AddDispatcher, the
// ValidateOnBuild/ValidateScopes forcing) lives in BillingHost.CreateBuilder,
// factored out the same way FulfillmentHost/OrdersHost are. The
// environment-reading configure delegate itself lives in
// BillingProgramConfiguration.Configure (feature
// composition_root_env_reads_are_unguarded) so a test can call the exact
// method this file calls — a lambda written inline here would be unreachable
// from any test project.
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry,
    configureHealth: BillingProgramConfiguration.ConfigureHealth);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
