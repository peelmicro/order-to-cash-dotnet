using Microsoft.Extensions.Hosting;
using OrderToCash.Orders;

// The Orders service host — the FIRST Program.cs this repository builds
// (outbox_and_idempotency design.md §2.3: "there is no Program.cs yet, and
// this feature does not write one" — feature orders_acceptance is that
// feature). The actual composition (AddOrdersOutbox, AddOrdersAcceptance,
// AddDispatcher, and the ValidateOnBuild/ValidateScopes forcing — review
// D3/D6) lives in OrdersHost.CreateBuilder, factored out so
// OrdersDispatcherRegistrationTests can drive the SAME method this file
// calls rather than reconstructing its own copy of the wiring. The three
// environment-reading configure delegates themselves live in
// OrdersProgramConfiguration (feature
// composition_root_env_reads_are_unguarded) so a test can call the exact
// methods this file calls.
var builder = OrdersHost.CreateBuilder(
    args,
    configureOutbox: OrdersProgramConfiguration.ConfigureOutbox,
    configureAcceptance: OrdersProgramConfiguration.ConfigureAcceptance,
    configureSaga: OrdersProgramConfiguration.ConfigureSaga,
    configureTelemetry: OrdersProgramConfiguration.ConfigureTelemetry,
    configureHealth: OrdersProgramConfiguration.ConfigureHealth);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
