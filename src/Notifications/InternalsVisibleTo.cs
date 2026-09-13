using System.Runtime.CompilerServices;

// Review round 1, A2 (backlog id 73) — grants OrderToCash.Notifications.UnitTests
// access to DegradingNotificationSender.Inner, so NotificationSenderBindingTests
// can tell which real sender a SenderKind binding resolves to, rather than
// only that SOME instance of the expected wrapper type was resolved.
[assembly: InternalsVisibleTo("OrderToCash.Notifications.UnitTests")]
