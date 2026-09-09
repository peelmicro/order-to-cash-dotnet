using Xunit;

// Found while verifying feature `gateway_sse_push`: `dotnet test OrderToCash.sln`
// (the full solution, five heavy *.IntegrationTests projects booting real
// Testcontainers concurrently) reproduced 8 failures in THIS project, twice
// in a row, on a freshly-quiesced system (no leftover ephemeral containers
// between the two runs) — always the SAME eight cases
// (AuthAndRateLimitHttpTests/DocsAndAnonymousRouteHttpTests/OrdersHttpTests),
// always `Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.BindAsync`
// throwing `TaskCanceledException` within the first ~2.5 seconds of the run,
// never a business-logic assertion. None of the classes this feature added
// (StreamHttpTests, StreamHeartbeatHttpTests, StreamProjectorEndToEndTests)
// were ever among the failures — all three already opt into a named,
// `DisableParallelization = true` collection (NatsCollection /
// StreamProjectorEndToEndCollection).
//
// The five classes that DID fail carry no `[Collection]` attribute at all —
// each is therefore its OWN implicit, unnamed xUnit collection, and
// DIFFERENT collections run IN PARALLEL by default. Every `[Fact]` in those
// five classes calls `GatewayTestHost.StartAsync`, which binds a REAL
// Kestrel listener on an ephemeral port — so the default behaviour was
// several unrelated Kestrel instances all starting within the same instant,
// competing for the CPU with FOUR OTHER *.IntegrationTests projects' own
// Testcontainers starting at the exact same moment under `dotnet test
// $SLN`. `KestrelServerImpl.BindAsync` has no unusually short timeout of
// its own; it was starved, not broken.
//
// `[CollectionBehavior(DisableTestParallelization = true)]` serialises
// EVERY collection in this assembly — including the five implicit
// per-class ones — closing the race outright rather than tuning a timeout
// that would only narrow the window. This project's own tests already pay
// a real-infrastructure cost per test; running them one at a time inside
// THIS assembly (still in parallel with every OTHER *.IntegrationTests
// project) is the correct trade, not a performance regression this
// project did not already have implicitly whenever the machine was busy.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
