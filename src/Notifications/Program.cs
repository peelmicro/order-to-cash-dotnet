using Microsoft.Extensions.Hosting;
using OrderToCash.Notifications;

// The Notifications service host — the FIRST runnable Notifications host
// (feature notifications_service). The actual composition (AddNotifications,
// AddDispatcher, the ValidateOnBuild/ValidateScopes forcing) lives in
// NotificationsHost.CreateBuilder, factored out the same way
// FulfillmentHost/BillingHost are. The environment-reading configure
// delegate itself lives in NotificationsProgramConfiguration.Configure
// (feature composition_root_env_reads_are_unguarded) so a test can call the
// exact method this file calls.
var builder = NotificationsHost.CreateBuilder(args, configure: NotificationsProgramConfiguration.Configure);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
