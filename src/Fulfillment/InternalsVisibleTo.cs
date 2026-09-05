using System.Runtime.CompilerServices;

// Grants OrderToCash.Fulfillment.UnitTests access to StockRpcResponder's
// internal per-subject dispatch methods, so FS3's header-validation theory
// and the wire tests can drive them against a fake IDispatcher registered in
// a real ServiceCollection/ServiceProvider — no NATS connection, no host —
// the same seam Cqrs.UnitTests already established for AddDispatcherFromTypes.
[assembly: InternalsVisibleTo("OrderToCash.Fulfillment.UnitTests")]
