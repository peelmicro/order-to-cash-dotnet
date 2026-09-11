using OrderToCash.Gateway;

// The Gateway service's runnable host. GatewayHost.Build wires the
// composition (endpoints, middleware, DI graph); the environment-reading
// configure delegate itself lives in GatewayProgramConfiguration.Configure
// (feature composition_root_env_reads_are_unguarded) so a test can call the
// exact method this file calls — a lambda written inline here would be
// unreachable from any test project.
var app = GatewayHost.Build(args, configure: GatewayProgramConfiguration.Configure, configureTelemetry: GatewayProgramConfiguration.ConfigureTelemetry);

await app.RunAsync().ConfigureAwait(false);
