using System.Runtime.CompilerServices;

// Grants OrderToCash.Cqrs.UnitTests access to AddDispatcherFromTypes — the
// type-list scanning core behind the public, assembly-scanning AddDispatcher
// — so validation tests can hand it a small, hand-picked type universe per
// scenario instead of this test assembly's own full reflection surface. See
// the remarks on AddDispatcherFromTypes for why that isolation matters.
[assembly: InternalsVisibleTo("OrderToCash.Cqrs.UnitTests")]
