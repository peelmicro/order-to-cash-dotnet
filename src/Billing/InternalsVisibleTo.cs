using System.Runtime.CompilerServices;

// Grants OrderToCash.Billing.UnitTests access to BillingRpcResponder's
// internal per-subject dispatch methods, so BC1's header-validation theory
// and the wire tests can drive them against a fake IDispatcher registered in
// a real ServiceCollection/ServiceProvider — no NATS connection, no host —
// the same seam src/Fulfillment/InternalsVisibleTo.cs already established.
[assembly: InternalsVisibleTo("OrderToCash.Billing.UnitTests")]
