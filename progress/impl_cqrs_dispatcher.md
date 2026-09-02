# impl_cqrs_dispatcher (feature 43, phase 8, `sdd: false`)

## What this is

**No #7 counterpart.** #7 got its command bus free from `@nestjs/cqrs`. MediatR
v13 is commercially licensed, so this feature hand-rolls the equivalent — the
same ruling `feature_list.json`'s `note` records. In the Phase 24 benchmark
this is a row without a baseline, not a row that came out slower.

The hand-rolled in-process CQRS dispatcher: `ICommandHandler<T>` /
`ICommandHandler<T,R>` / `IQueryHandler<T,R>` / `IEventHandler<T>`, a
`Dispatcher` resolving from `IServiceProvider`, assembly-scan registration via
one `IServiceCollection.AddDispatcher(...)` call, and a startup validation
pass that throws — during registration, before any host is built or run —
when a command or query type has zero handlers or more than one.

Not wired into any service. Feature 15 (`orders_acceptance`) is the first
consumer, per the brief.

## Where it went, and why

`src/Cqrs/` (assembly `OrderToCash.Cqrs`), a new solution-level project
alongside `SharedKernel` and `Contracts`, added to `OrderToCash.sln` under the
`src` solution folder.

- **Not `SharedKernel`**: that project has zero `PackageReference` entries by
  design, guarded by `SharedKernelHasNoPackagesTests`
  (`SharedKernelCsprojDeclaresZeroPackageReferences` and
  `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework`), and the
  dispatcher genuinely needs
  `Microsoft.Extensions.DependencyInjection.Abstractions`. Adding a package
  there would have broken both those guards outright — not a rule to bend.
- **Not `Contracts`**: that project is the wire contract, versioned by
  `asyncapi.yaml`. The dispatcher is a purely in-process building block that
  never touches JSON, Kafka or NATS; folding it into `Contracts` would blur a
  distinction CLAUDE.md draws deliberately.
- **A new project**, because CLAUDE.md's "only shared runtime code" list
  (`SharedKernel` + `Contracts`) predates this feature's own human-gate
  ruling ("the hand-rolled dispatcher is binding across all six services").
  The brief explicitly authorised this route ("If the right home is a new
  project, create it ... do not weaken an existing rule to fit"), and no
  existing architecture rule needed weakening: `Cqrs` is not a `Domain/`
  namespace in any service, so `DomainPurityTests`,
  `OrdersDomainContractsTests` and the decimal rules are untouched by it, and
  `DomainAssembliesTests`'s fixed eight-assembly list (seven services +
  `SharedKernel`) correctly does not include it either — `Cqrs` is not a
  domain layer and was never meant to be scanned as one. All 13 architecture
  tests pass unmodified (verified below).

  **Finding worth flagging, not fixed here** (scope: "do not re-touch
  anything else"): `AGENTS.md` line 30 describes `src/` as "The 6 services +
  `SharedKernel` (dependency-free) + `Contracts` (generated types) + `Seed`",
  which is now one project short of accurate. `AGENTS.md` is outside this
  feature's touch list (doc files are the leader's to edit), so this is
  reported rather than changed.

## The four handler interfaces, and the two-vs-one decision

`ICommandHandler<TCommand>` and `ICommandHandler<TCommand,TResult>` are
**two genuinely separate interfaces**, not one interface unified behind a
`Unit`-like marker return type. Two reasons:

1. The acceptance criteria for this feature name both shapes explicitly
   ("`ICommandHandler<T>, ICommandHandler<T,R>`"), so the decision was made
   at the point the feature was scoped, not left open.
2. A `Unit` marker exists only to make a void-returning method fit a
   signature built for a value. Every caller of the void form would receive
   a value it must ignore, and every handler author would return a token
   that carries no information. `Task` already expresses "no result" in .NET
   without an invented type standing in for it, so the two-interface split
   costs one extra interface declaration and buys handler authors a
   signature that says exactly what it means — and matches
   `specs/orders_aggregate/design.md` §10.1's `PlaceOrderCommandHandler :
   ICommandHandler<PlaceOrderCommand, PlaceOrderResult>`, which only ever
   shows the result-bearing shape because #7's design didn't need the void
   one named yet.

`ICommand` / `ICommand<TResult>` / `IQuery<TResult>` marker interfaces were
**added beyond the four acceptance items**, and that needs justifying since
they were not asked for explicitly. They exist for one reason: without a
known universe of "every command/query type", the "zero handlers" half of
startup validation has nothing to enumerate. A handler that is never written
leaves no trace for a scan that only looks at handler *classes*. The markers
are what let `AddDispatcher` discover "this command type exists and requires
exactly one handler" independently of whether any handler for it was ever
written — the same reason MediatR's own `IRequest`/`IRequest<TResponse>`
exist, arrived at independently here because the zero-handler requirement
forces it, not copied from that design.

## Command/query/event asymmetry — what was decided, and why

- **A command with zero handlers or more than one fails validation** — the
  acceptance criterion, verified for each case (arming table below).
- **A query is validated identically to a command — exactly one handler
  required, zero or two both fail.** This was the open decision the brief
  asked for ("a query with two handlers behaves per whatever you decide").
  Reasoning, recorded in `DispatcherRegistrationValidator`'s XML remarks: a
  query answers synchronously with one `TResult`. Zero handlers means the
  read side can never be served — the same failure class as a command with
  no handler, not an event's "no listener yet". Two handlers is worse than
  two for a command: a command's caller only needs the mutation to happen
  once and correctly, so a duplicate is unambiguously a misconfiguration
  either way, but a query with two candidate answers has no principled way
  to pick one — the container resolving "the first one it finds" would
  silently depend on registration order, which is exactly the class of DI
  failure CLAUDE.md says must be loud at boot, not discovered from an
  inconsistent answer at runtime. So queries hold to the same "exactly one"
  rule as commands, and the validator's logic — and its tests — stays one
  rule applied twice rather than three separate rules for three cases.
- **An event with zero handlers is *not* an error** — the asymmetry the
  brief calls out by name, matching #7's `EventBus`. No marker interface
  constrains `IEventHandler<TEvent>`, deliberately: there is no "every event
  type" universe to check registrations against, so nothing is enumerated
  and nothing can fail on account of it. `DispatcherTests.PublishAsync_With
  ZeroRegisteredHandlers_CompletesWithoutError` proves the dispatch-time
  behaviour (a `PublishAsync` call to a fact with no listener anywhere in
  the assembly completes normally); `DispatcherValidationTests.AddDispatcher
  _EventWithZeroHandlers_DoesNotThrow` proves the registration-time half of
  the same claim.
- **An event with two or more handlers fans out to all of them, in
  registration order** — `PublishAsync_ReachesEveryRegisteredEventHandler`.

## Files

```
src/Cqrs/Cqrs.csproj
src/Cqrs/ICommand.cs                          — ICommand, ICommand<TResult> markers
src/Cqrs/IQuery.cs                            — IQuery<TResult> marker
src/Cqrs/ICommandHandler.cs                   — ICommandHandler<T>, ICommandHandler<T,R>
src/Cqrs/IQueryHandler.cs                     — IQueryHandler<T,R>
src/Cqrs/IEventHandler.cs                     — IEventHandler<T>, deliberately unconstrained
src/Cqrs/IDispatcher.cs                       — SendAsync / SendAsync<T,R> / QueryAsync / PublishAsync
src/Cqrs/Dispatcher.cs                        — resolves from IServiceProvider
src/Cqrs/DispatcherServiceCollectionExtensions.cs — AddDispatcher(assemblies), assembly-scan registration
src/Cqrs/DispatcherRegistrationValidator.cs   — the startup validation pass (this feature's guard)
src/Cqrs/DispatcherValidationException.cs
src/Cqrs/InternalsVisibleTo.cs                — grants the test project access to the type-list scanning core

tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj
tests/Cqrs.UnitTests/Fixtures/WellFormedFixtures.cs  — one command/query/event pair per shape
tests/Cqrs.UnitTests/Fixtures/ValidationProbes.cs    — open-generic probes closed per validation scenario
tests/Cqrs.UnitTests/DispatcherTests.cs              — dispatch-reaches-handler + asymmetry + CancellationToken
tests/Cqrs.UnitTests/DispatcherValidationTests.cs    — the startup validation guards

OrderToCash.sln                — Cqrs.csproj and Cqrs.UnitTests.csproj added
Directory.Packages.props       — Microsoft.Extensions.DependencyInjection.Abstractions (src),
                                  Microsoft.Extensions.DependencyInjection (tests only), both 10.0.11
feature_list.json               — feature 43 status only: pending -> in_review
```

## A design note worth recording: the internal type-list scanning seam

`AddDispatcher(params Assembly[] assemblies)` is the acceptance-criterion
entry point (assembly scan). Internally it delegates to `internal static
IServiceCollection AddDispatcherFromTypes(this IServiceCollection, IEnumerable<Type>
candidateTypes)`, visible to `OrderToCash.Cqrs.UnitTests` only via
`InternalsVisibleTo`.

This exists because the two validation scenarios (zero handlers, duplicate
handlers) cannot share a compiled test assembly with the well-formed
dispatch fixtures if that assembly is ever scanned *whole* — a command type
with a genuine, permanent zero-handler or duplicate-handler configuration
compiled as an ordinary C# type would break `DispatcherTests`'s own
whole-assembly `AddDispatcher(typeof(PingCommand).Assembly)` call. Two
ways out were considered and rejected: a second test-fixtures project (out
of scope — the brief authorises exactly one new src project and one new
test project) and `System.Reflection.Emit` (workable but needlessly heavy
for handler method bodies that only need to exist, never execute
meaningfully). The solution landed on: leave the probe command/query and
handler types **as open generics** (`ProbeCommand<TMarker>`,
`ProbeCommandHandlerA<TMarker>`, etc.) in the test assembly.
`Assembly.GetTypes()` only ever returns *declared* types — for a generic
type that is its open definition, never a constructed instantiation built
elsewhere via `MakeGenericType` — so these types are structurally invisible
to a whole-assembly scan (reinforced by an explicit `IsGenericTypeDefinition:
false` filter in the scanner, since no real command DTO is ever generic).
Each validation test closes the probes itself, over a private marker type
nobody else touches, and hands the resulting closed types straight to
`AddDispatcherFromTypes` — full isolation, zero IL emission, and the
production scanning/registration logic (`GetInterfaces()`, filtering,
`AddTransient`, `Record`) is exercised identically either way, since
`AddDispatcher(assemblies)` is nothing more than `AddDispatcherFromTypes
(assemblies.SelectMany(GetTypes))`.

## Traceability — acceptance items to tests

| # | Acceptance item | Proof |
|---|---|---|
| 1 | `ICommandHandler<T>`, `ICommandHandler<T,R>`, `IQueryHandler<T,R>`, `IEventHandler<T>` + a `Dispatcher` resolving from `IServiceProvider` | `DispatcherTests.SendAsync_ReachesTheVoidCommandHandler`, `SendAsync_ReachesTheResultCommandHandlerAndReturnsItsResult`, `QueryAsync_ReachesTheQueryHandlerAndReturnsItsResult`, `PublishAsync_ReachesEveryRegisteredEventHandler` — one per shape, all resolved through `Dispatcher`/`IServiceProvider` |
| 2 | handlers registered by assembly scan | Every `DispatcherTests` fixture is wired up by the single `services.AddDispatcher(typeof(PingCommand).Assembly)` call in the constructor — no test registers a handler by hand |
| 3 | startup validation FAILS FAST — zero handlers, proven by a test | `DispatcherValidationTests.AddDispatcher_CommandWithZeroHandlers_ThrowsDispatcherValidationException`, `AddDispatcher_QueryWithZeroHandlers_ThrowsDispatcherValidationException` |
| 3 | startup validation FAILS FAST — more than one, proven by a test | `DispatcherValidationTests.AddDispatcher_CommandWithTwoHandlers_ThrowsDispatcherValidationException`, `AddDispatcher_QueryWithTwoHandlers_ThrowsDispatcherValidationException` |
| 4 | no MediatR reference anywhere in the solution | `grep -ril mediatr` across `*.cs`/`*.csproj`/`*.props`/`*.sln` from the repo root — zero matches, verified below |

Plus the asymmetry the brief calls for explicitly:
`DispatcherTests.PublishAsync_WithZeroRegisteredHandlers_CompletesWithoutError`
and `DispatcherValidationTests.AddDispatcher_EventWithZeroHandlers_DoesNotThrow`
(an event with zero handlers is not an error), and
`DispatcherTests.SendAsync_Void_ForwardsTheCancellationTokenToTheHandler` /
`SendAsync_WithResult_ForwardsTheCancellationTokenToTheHandler` /
`QueryAsync_ForwardsTheCancellationTokenToTheHandler` (the `CancellationToken`
reaches the handler, for all three shapes that carry one).

This feature is not in `specs/shared/test-matrix.md` (no `R<n>` requirement
covers it — it is new to #8, per its `note`), so no row there was updated.

## Arming table — the feature's guards

Per CLAUDE.md's protocol, using `scripts/arm-probe.sh` first to prove each
guard fires and restores cleanly, then a manual repeat of the same
back-up-first / mutate / force-rebuild / run / restore-from-backup /
force-rebuild / confirm-green sequence to capture the verbatim failure text
(`arm-probe.sh` reports pass/fail counts, not message bodies).

Both mutations target `src/Cqrs/DispatcherRegistrationValidator.cs`'s
`CollectErrors` method, refactored during arming from a single compound
condition (`!registered.TryGetValue(...) || implementations.Count == 0`)
into two independent conditions on a guaranteed-non-null
`GetValueOrDefault(...) ?? []` — the compound form coupled "key absent" and
"count == 0" behind one nullable `out var`, and every surgical mutation
attempted against it either left `implementations` undeclared for the
`else if` branch or produced an unreachable/nullable-dereference compile
error (`TreatWarningsAsErrors` turns both into build failures, which
`arm-probe.sh` correctly refuses to count as a fired guard — "a build
failure is not a fired guard"). The refactor is also better production
code: no nullable-reference gymnastics for `implementations` in the
`else if` branch.

| # | Branch | Arm by (`scripts/arm-probe.sh` args) | `arm-probe.sh` result | Verbatim failure (manual capture) |
|---|---|---|---|---|
| 1 | zero-handlers detection (`if (implementations.Count == 0)`) | `src/Cqrs/DispatcherRegistrationValidator.cs` / `s/implementations.Count == 0/implementations.Count == -1/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` | `AddDispatcher_CommandWithZeroHandlers_ThrowsDispatcherValidationException` and `AddDispatcher_QueryWithZeroHandlers_ThrowsDispatcherValidationException` both `[FAIL]` with `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(OrderToCash.Cqrs.DispatcherValidationException)` — `Failed! - Failed: 2, Passed: 11, Skipped: 0, Total: 13` |
| 2 | duplicate-handlers detection (`else if (implementations.Count > 1)`) | `src/Cqrs/DispatcherRegistrationValidator.cs` / `s/implementations.Count > 1/implementations.Count > int.MaxValue/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` | `AddDispatcher_CommandWithTwoHandlers_ThrowsDispatcherValidationException` and `AddDispatcher_QueryWithTwoHandlers_ThrowsDispatcherValidationException` both `[FAIL]` with `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(OrderToCash.Cqrs.DispatcherValidationException)` — `Failed! - Failed: 2, Passed: 11, Skipped: 0, Total: 13` |

Both restores confirmed by re-reading the changed lines after the forced
rebuild (`grep -n "implementations.Count == 0\|implementations.Count > 1"`
shows the original comparisons, `-1` and `int.MaxValue` gone), and by the
full green re-run (`Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13`)
after each restore.

Two attempted mutations that `arm-probe.sh` correctly rejected as build
failures, kept here as evidence the tool caught them rather than silently
producing a false green:

- `s/!registered.TryGetValue(serviceType, out var implementations) || implementations.Count == 0/false/` — deleted the `out var implementations` declaration itself; `else if (implementations.Count > 1)` then referenced an undeclared name (CS0103-class failure, `arm-probe.sh`: `FATAL: armed source does not compile`).
- `s/implementations.Count > 1/false/` — a compile-time-constant `false` makes the `else if` body unreachable, and `CS0162` is an error under this repository's `TreatWarningsAsErrors` (`arm-probe.sh`: `FATAL: armed source does not compile`). Resolved by using a data-dependent always-false comparison (`> int.MaxValue`) instead of a literal, which is what row 2 above uses.

## Verification run

- `dotnet format OrderToCash.sln --verify-no-changes` — exit 0.
- `dotnet build OrderToCash.sln --nologo` — 0 warnings, 0 errors, all 21 projects (7 services + SharedKernel + Contracts + Seed + Cqrs + eleven test projects) build.
- `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj` — `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13` (all thirteen architecture rules, unmodified by this feature, stay green).
- `dotnet test tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` — `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13`. Line coverage 96.57% (`coverage.cobertura.xml`, `line-rate="0.9657"`).
- `dotnet test` on `SharedKernel.UnitTests` (32), `Contracts.UnitTests` (21), `Orders.UnitTests` (24), `Seed.UnitTests` (34) — all green, confirming this feature touched nothing that regressed an existing unit-test project. (Integration-test projects, which need Testcontainers, were not run — out of scope for a pure in-process feature and unaffected by anything this feature touched.)
- `grep -ril "mediatr" --include="*.cs" --include="*.csproj" --include="*.props" --include="*.sln" .` from the repo root — zero matches.
- `./init.sh` — green (`environment and state are coherent`), both before and after this feature's changes.

## What was not done, and why

- **Not wired into any service** — explicitly out of scope per the brief; feature 15 is the first consumer.
- **No `test-matrix.md` row** — this feature carries no `R<n>`, confirmed by its own `note` in `feature_list.json` ("New in #8 ... Expect this feature to have NO #7 counterpart").
- **`AGENTS.md`'s `src/` description is now one project short** — flagged above as a finding rather than fixed, since it is outside this feature's touch list.

## What surprised me

- The compound `!TryGetValue(...) || Count == 0` condition, while perfectly
  correct production code, turned out to be a poor shape for the arming
  protocol specifically: every surgical, single-substring mutation against
  it either left an `out var` referenced-but-undeclared, or ran into a
  nullable-flow-analysis error the compiler could not resolve for the
  `else if` branch, because the two conditions shared one `out var` whose
  definite-assignment status depended on *which* disjunct fired. Splitting
  into `GetValueOrDefault(...) ?? []` up front — arguably the more
  idiomatic form anyway — turned both guards into independent,
  cleanly-armable one-line conditions, and is very likely a shape #7 never
  had to think about (TypeScript's `Map.get` returns `T | undefined`
  without a compiler forcing you to prove non-null downstream the way C#'s
  nullable-reference analysis does across two `else if` branches sharing a
  variable).
- `TreatWarningsAsErrors` turning `CS0162` (unreachable code) into a hard
  build failure meant the "obvious" arming mutation (`replace the condition
  with the literal `false`) is unusable in this repository for any
  condition guarding a block reached via `else if` — a literal constant
  makes the *compiler itself* prove the branch dead, which is a stronger
  signal than "the guard doesn't fire" and gets rejected by `arm-probe.sh`
  as a build failure before it ever gets the chance to prove anything. A
  data-dependent comparison (`> int.MaxValue`) that is always-false only at
  runtime, not at compile time, was needed instead — worth remembering for
  any future arming against a `TreatWarningsAsErrors` repository.

---

# Re-review round 1 — response to `progress/review_cqrs_dispatcher.md`

Feature 43 was **REJECTED** on **D1** (blocking). **D2 is the leader's/human's** —
the reviewer assigned it explicitly, endorsed the `src/Cqrs` placement and
confirmed no existing rule was weakened, and the coordinator's brief said to
ignore it entirely here. **D3** was escalated from advisory to required by
the coordinator ("the reviewer says flag it loudly, so treat it as
required"). **D4–D6** were fixed rather than deferred.

## D1 — what it actually was, the regression test, and the failure observed before the fix

**The defect.** `DispatcherServiceCollectionExtensions.cs` registered
`IDispatcher` as `services.AddSingleton<IDispatcher, Dispatcher>()`. A
singleton is constructed once, from the DI container's **root** scope, so
the `IServiceProvider` `Dispatcher`'s constructor captured was permanently
the root provider — every `GetRequiredService<ICommandHandler<T>>()` (and
its three siblings) resolved from root regardless of which scope the caller
was in. From feature 15 onward, every command handler over an EF Core
`DbContext` (registered scoped by `AddDbContext`) would therefore resolve
its `DbContext` from root: in a Development host with `ValidateScopes:
true` this throws on first dispatch; in a Production host (`ValidateScopes:
false`, the default for a bare `BuildServiceProvider()`) it does not throw
at all — it silently hands every request the same captive `DbContext`
instance, tracked entities accumulating for the lifetime of the process,
used concurrently across requests.

**The regression test**, `DispatcherScopeTests.SendAsync_ResolvesTheHandlerAndItsDependenciesFromTheCallersScope_NotTheRootProvider`
(`tests/Cqrs.UnitTests/DispatcherScopeTests.cs`, fixtures in
`tests/Cqrs.UnitTests/Fixtures/ScopedDependencyFixtures.cs`): registers a
`ScopedDependency` (stands in for a `DbContext` — one `Guid InstanceId` per
DI scope) and a command handler that reports which instance it saw, then
dispatches once from each of two separate `CreateAsyncScope()`s and asserts
the two ids differ. Built with a bare `BuildServiceProvider()` — no
`ValidateScopes` override — deliberately, because that default (`false`)
is the more dangerous of the two failure modes the review recorded: the
silent one, not the one that throws.

**Written and run BEFORE touching the registration**, per the coordinator's
instruction, to see the failure firsthand rather than take the review's word
for it:

```
[xUnit.net 00:00:00.xx]     OrderToCash.Cqrs.UnitTests.DispatcherScopeTests.SendAsync_ResolvesTheHandlerAndItsDependenciesFromTheCallersScope_NotTheRootProvider [FAIL]
  Error Message:
   Assert.NotEqual() Failure: Values are equal
Expected: Not d8fa80c1-d584-4c23-afe1-4397a7ad1813
Actual:       d8fa80c1-d584-4c23-afe1-4397a7ad1813
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

Exactly the reviewer's finding, reproduced independently: two separate
scopes, one captive instance.

**The fix.** `services.AddSingleton<IDispatcher, Dispatcher>()` →
`services.AddScoped<IDispatcher, Dispatcher>()`
(`DispatcherServiceCollectionExtensions.cs:86`). Took the coordinator's
recommendation over the `IServiceScopeFactory`-inside-`Dispatcher`
alternative without arguing otherwise: scoped registration keeps scope
ownership with the caller (a Presentation/ endpoint, a Kafka consumer, a
NATS responder resolving `IDispatcher` from a scope it already owns or
opens per message), which is more honest about who controls the scope's
lifetime than hiding a `CreateScope()` inside the dispatcher itself. After
the fix, the same test passes; `Dispatcher.cs`'s class remarks now record
why singleton is wrong, permanently, next to the registration.

**Arming** (both `scripts/arm-probe.sh` and a manual verbatim capture,
since the script reports pass/fail counts, not message bodies):

| Guard | Arm (`scripts/arm-probe.sh` args) | Result | Verbatim (armed) |
|---|---|---|---|
| D1 — `IDispatcher` scoped, not singleton | `src/Cqrs/DispatcherServiceCollectionExtensions.cs` / `s/services.AddScoped<IDispatcher, Dispatcher>();/services.AddSingleton<IDispatcher, Dispatcher>();/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED` / `restored -> suite green` | `DispatcherScopeTests.SendAsync_ResolvesTheHandlerAndItsDependenciesFromTheCallersScope_NotTheRootProvider [FAIL]` — `Assert.NotEqual() Failure: Values are equal` / `Expected: Not e59b3c57-0131-43df-aebe-f9c2c3d0644f` / `Actual: e59b3c57-0131-43df-aebe-f9c2c3d0644f` |

Restore confirmed by re-reading the line (`grep -n "AddScoped<IDispatcher"`)
after the forced rebuild, and by the full suite going green again
(`Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19` — 19, not 14,
because D3/D4/D5/D6's tests were added in the same pass; see below).

## D3 — resolution: the generic overload was removed, not supplemented

**What D3 was.** `PublishAsync<TEvent>(TEvent @event, ...)` inferred
`TEvent` from the **static** type of the argument. Publishing through a
base- or interface-typed variable — exactly how feature 14's outbox drain
and feature 15's aggregate drain iterate a mixed
`IReadOnlyList<IDomainEvent>` and publish one element at a time — would
infer `TEvent = IDomainEvent`, find zero registered
`IEventHandler<IDomainEvent>` implementations, and silently succeed having
called nothing. Not wrong today (nothing publishes yet), but a loaded gun
for feature 14.

**Regression test written and run against the un-fixed code first**
(`DispatcherTests.PublishAsync_ThroughABaseOrInterfaceTypedVariable_StillReachesTheHandlerForTheRuntimeType`,
fixtures `IUpstreamFact` / `ConcreteUpstreamFact` /
`ConcreteUpstreamFactHandler` in `Fixtures/WellFormedFixtures.cs`):

```
Failed OrderToCash.Cqrs.UnitTests.DispatcherTests.PublishAsync_ThroughABaseOrInterfaceTypedVariable_StillReachesTheHandlerForTheRuntimeType
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: a2598ba8-d34c-4d74-8e4d-d856620399c6
Actual:   null
```

— the silent no-op, reproduced directly: the handler was simply never
called, and the assertion failed on the un-set `null` rather than on an
exception.

**Resolution: fix it, not merely flag it — and fixed by removing the
generic overload, not adding a second one.** The coordinator's brief asked
"fix it or make it impossible to get wrong, and say which." Considered and
rejected: keeping `PublishAsync<TEvent>` **and** adding a
`PublishAsync(object, ...)` overload alongside it. This does not close the
gap — given an argument whose *static* type is the base/interface type, C#
overload resolution prefers the generic method's exact-type match over an
implicit reference conversion to `object`, so the risky call sites (the
ones this defect is actually about) would keep silently picking the wrong
overload. The only fix that makes the mistake **structurally unavailable**
is removing the generic method entirely: `IDispatcher.PublishAsync` is now
`Task PublishAsync(object @event, CancellationToken cancellationToken)` —
one method, resolving `IEventHandler<>` by `@event.GetType()` at the point
of the call, never by a compile-time generic parameter.

**The cost, noted honestly, per the brief's instruction.** `Dispatcher.PublishAsync`
now does `typeof(IEventHandler<>).MakeGenericType(eventType)`,
`IServiceProvider.GetServices(Type)` (the non-generic overload), and
`MethodInfo.Invoke` per handler — reflection cost that a direct generic
call would not pay. The `MethodInfo` lookup itself is cached per distinct
event `Type` in a `ConcurrentDictionary` for the process lifetime
(`Dispatcher._handleAsyncMethodsByEventType`), so the recurring cost per
publish is one dictionary lookup plus one `MethodInfo.Invoke` (boxing the
two arguments into an `object[]`), not a fresh reflection walk every time.
Judged worth it: facts are published per outbox row / per consumed
message — this repository's traffic shape (B2B EDI orders), not a
per-request hot loop — and correctness on the call shape feature 14 and 15
both need was the point of the fix. A second cost, also honest: any
`object` can now be passed to `PublishAsync`, including a non-event type,
which is not separately guarded — it simply resolves zero handlers and
completes, the same as any other unlistened fact, not a new failure mode.

**Arming:**

| Guard | Arm | Result | Verbatim (armed) |
|---|---|---|---|
| D3 — `PublishAsync` resolves by runtime type | `src/Cqrs/Dispatcher.cs` / `s/var eventType = @event.GetType();/var eventType = typeof(object);/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED` / `restored -> suite green` | Two failures: `PublishAsync_ReachesEveryRegisteredEventHandler` (`Assert.Equal() Failure: Values differ`, `Expected: 5bd02525-...`, `Actual: null`) and `PublishAsync_ThroughABaseOrInterfaceTypedVariable_StillReachesTheHandlerForTheRuntimeType` (same shape, different id) — `Failed! - Failed: 2, Passed: 13, Skipped: 0, Total: 15` |

## D4 — no MediatR reference, now a test not a grep

**Fix:** `tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs`, a `[Theory]`
asserting `OrderToCash.Cqrs.dll` and `OrderToCash.Cqrs.UnitTests.dll`'s
**compiled** `GetReferencedAssemblies()` contain no assembly whose name
contains "mediatr" (case-insensitive) — the same technique
`SharedKernelHasNoPackagesTests.SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework`
uses, and for the same reason: Central Package Management only rejects a
*versionless* `PackageReference Include="MediatR"`; a `PackageVersion`
entry plus a reference would pass a text-only check, so this reads the
assembly's actual metadata instead.

**Scope decision, stated explicitly since it departs from the reviewer's
suggested location:** the reviewer suggested adding this to
`tests/Architecture.Tests`. The coordinator's scope for this re-review round
is `src/Cqrs/**` and `tests/Cqrs.UnitTests/**` only, so it was built there
instead — scoped to the two assemblies this feature owns, proven by a
theory over both. **Not done, and said explicitly rather than dropped**: a
solution-wide equivalent in `tests/Architecture.Tests` (checking every
service assembly, not just these two) is a natural follow-up, out of scope
for this touch list, and is not this feature's to add unilaterally per
`CLAUDE.md`'s single-writer discipline on shared test infrastructure.

**Arming.** Could not arm by making the search string never match (tried
`"mediatr"` → `"zzz_never_matches_zzz"` first — `scripts/arm-probe.sh`
correctly reported `*** suite still green — GUARD DOES NOT GUARD ***`,
because a search term that matches nothing produces the same "zero
offenders" result the healthy case already produces; that is not evidence
the guard works, it is evidence the mutation was inert. **Recorded as a
non-probe below, same as the leader's two rejected `if (false)` attempts —
`arm-probe.sh` correctly refusing to certify a probe that proves nothing.**
Re-armed by substituting a search term guaranteed to match a **real**
referenced assembly name (`"mediatr"` → `"microsoft"` — both `Cqrs.dll` and
`Cqrs.UnitTests.dll` genuinely reference `Microsoft.Extensions.DependencyInjection*`
assemblies), which correctly exercises the "found offenders → fail" code
path:

| Guard | Arm | Result | Verbatim (armed) |
|---|---|---|---|
| D4 — no MediatR reference (`Cqrs.dll`) | `tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs` / `s/"mediatr"/"microsoft"/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED` / `restored -> suite green` | `CompiledAssemblyReferencesNoMediatRAssembly(typeFromAssemblyUnderTest: typeof(OrderToCash.Cqrs.IDispatcher))` — `OrderToCash.Cqrs must not reference MediatR ... Offending references: Microsoft.Extensions.DependencyInjection.Abstractions, ...` |
| D4 — no MediatR reference (`Cqrs.UnitTests.dll`) | *(same run, second theory case)* | *(same run)* | `CompiledAssemblyReferencesNoMediatRAssembly(typeFromAssemblyUnderTest: typeof(OrderToCash.Cqrs.UnitTests.NoMediatRReferenceTests))` — `OrderToCash.Cqrs.UnitTests must not reference MediatR ... Offending references: Microsoft.VisualStudio.TestPlatform.ObjectModel, ..., Microsoft.Extensions.DependencyInjection.Abstractions, ..., Microsoft.Extensions.DependencyInjection, ...` |

**Non-probe, recorded for the same reason the leader's are** (a rejected
mutation that proved the tooling, not the guard): `s/"mediatr"/"zzz_never_matches_zzz"/`
→ `arm-probe.sh`: `armed -> *** suite still green — GUARD DOES NOT GUARD ***`.
Correct behaviour from the mutation, not a defect in the test: a search
term with no real match anywhere is indistinguishable from "no MediatR
present" — it says nothing about whether the assertion fires when it
should.

## D5 — two `AddDispatcher` calls now refuse the second one

**The problem, as the review stated it:** `AddDispatcherFromTypes`
validates only the type universe of the call it is in. Two sequential
`AddDispatcher` calls — one scanning an assembly with commands, another
scanning an assembly with their handlers — would each see only half the
picture: the first call would report the commands as having zero handlers
(the handlers are not in its universe), and the second call would register
a **second** `IDispatcher`.

**Decision.** Rather than attempt to merge two partial scans after the
fact (which would need `AddDispatcher` to somehow remember state across
calls — the exact kind of hidden, order-dependent behaviour this feature
exists to avoid), `AddDispatcherFromTypes` now refuses a second call on the
same `IServiceCollection` outright:

```csharp
if (services.Any(descriptor => descriptor.ServiceType == typeof(IDispatcher)))
{
    throw new InvalidOperationException(
        "AddDispatcher was already called on this IServiceCollection. Call it exactly once, " +
        "passing every assembly that contains commands, queries, events or their handlers " +
        "together (AddDispatcher(assemblyA, assemblyB, ...)) — ...");
}
```

`AddDispatcher(params Assembly[] assemblies)` already accepted multiple
assemblies in one call, so the fix is "callers must use the API's existing
multi-assembly shape", not a new capability — the contract was already
supported, just not enforced.

**Test:** `DispatcherValidationTests.AddDispatcher_CalledTwiceOnTheSameServiceCollection_Throws`.

**Arming:**

| Guard | Arm | Result | Verbatim (armed) |
|---|---|---|---|
| D5 — second `AddDispatcher` call refused | `src/Cqrs/DispatcherServiceCollectionExtensions.cs` / `s/descriptor.ServiceType == typeof(IDispatcher)/descriptor.ServiceType == typeof(DispatcherValidationException)/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED` / `restored -> suite green` | `AddDispatcher_CalledTwiceOnTheSameServiceCollection_Throws [FAIL]` — `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(System.InvalidOperationException)` |

## D6 — a command implementing both `ICommand` and `ICommand<TResult>` now fails validation

**The problem:** `ICommand.cs`'s own doc comment said "either this
interface or `ICommand<TResult>` — never both, and never neither", but
nothing enforced "never both" — a type implementing both silently required
**two** handlers (one `ICommandHandler<T>`, one `ICommandHandler<T,R>`),
which is confusing rather than caught.

**Fix:** `ExpectedCommandHandlerServiceTypes` now detects a command type
implementing both markers and adds a dedicated declaration error —
`"{commandType} implements both {ICommand} and {ICommand<TResult>} — a
command must implement exactly one ..."` — instead of silently expecting
two handler service types. `DispatcherRegistrationValidator.Validate` takes
a new `declarationErrors` parameter, seeded first into the combined error
list, so a malformed command is reported for what it is rather than
surfacing only as a confusing "zero/N handlers registered" pair against two
different, both-technically-correct expected service types.

**Test:** `DispatcherValidationTests.AddDispatcher_CommandImplementingBothCommandMarkers_ThrowsDispatcherValidationException`,
fixture `AmbiguousProbeCommand<TMarker> : ICommand, ICommand<int>` in
`Fixtures/ValidationProbes.cs` (open generic, closed per test, same
isolation technique as the other validation probes).

**Arming** (line-scoped to avoid also disarming the unrelated `else if` on
line 217 that shares the same substring):

| Guard | Arm | Result | Verbatim (armed) |
|---|---|---|---|
| D6 — both command markers rejected | `src/Cqrs/DispatcherServiceCollectionExtensions.cs` / `204s/resultCommandInterface is not null/resultCommandInterface is null/` / `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` | `armed -> suite FAILED` / `restored -> suite green` | `AddDispatcher_CommandImplementingBothCommandMarkers_ThrowsDispatcherValidationException [FAIL]` — `Assert.Contains() Failure: Sub-string not found` / `String: "No command handler is registered for Orde"···` / `Not found: "implements both"` — i.e. under the armed code, the type fell through to being treated as *neither* shape and was reported as a plain missing-handler command, not as the malformed declaration it is. |

## Files touched this round

```
src/Cqrs/Dispatcher.cs                          — AddScoped remarks (D1); PublishAsync(object, ...) + cached MethodInfo (D3)
src/Cqrs/IDispatcher.cs                         — PublishAsync(object, ...) signature + remarks (D3)
src/Cqrs/ICommand.cs                            — remarks: "never both" is now enforced, not just documented (D6)
src/Cqrs/DispatcherServiceCollectionExtensions.cs — AddScoped not AddSingleton (D1); duplicate-call guard (D5);
                                                     ExpectedCommandHandlerServiceTypes detects both-markers (D6)
src/Cqrs/DispatcherRegistrationValidator.cs     — Validate(...) takes declarationErrors (D6 plumbing)

tests/Cqrs.UnitTests/DispatcherScopeTests.cs           — new: D1's guard
tests/Cqrs.UnitTests/Fixtures/ScopedDependencyFixtures.cs — new: D1's scoped-dependency fixture
tests/Cqrs.UnitTests/DispatcherTests.cs                — +1 test: D3's guard (PublishAsync through a base/interface-typed variable)
tests/Cqrs.UnitTests/Fixtures/WellFormedFixtures.cs    — +IUpstreamFact / ConcreteUpstreamFact / ConcreteUpstreamFactHandler (D3)
tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs        — new: D4's guard
tests/Cqrs.UnitTests/DispatcherValidationTests.cs      — +2 tests: D5, D6
tests/Cqrs.UnitTests/Fixtures/ValidationProbes.cs      — +AmbiguousProbeCommand<TMarker> + AmbiguousCommandMarker (D6)
```

No new packages. No `.sln` or `Directory.Packages.props` changes this
round (both were already correct from the first pass).

## Verification run, this round

- `dotnet build --no-incremental src/Cqrs/Cqrs.csproj` — 0 warnings, 0 errors, after each individual fix.
- `dotnet format OrderToCash.sln --verify-no-changes` — caught one real defect of its own: `Dispatcher._handleAsyncMethodsByEventType` was first written without its `_` prefix (`HandleAsyncMethodsByEventType`), which `IDE1006` correctly flagged as a build-stopping error under this repository's `TreatWarningsAsErrors`. Fixed; `dotnet format` now exits 0.
- `dotnet test tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` — `Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19` (13 from round 1, all still passing unchanged, plus 6 new this round: 1 scope test (D1) + 1 base/interface-typed-publish test (D3) + 2 `[Theory]` cases (D4) + 1 duplicate-call test (D5) + 1 both-markers test (D6) = 19). Coverage: line-rate 0.9725 (97.25%).
- `dotnet build OrderToCash.sln --nologo` — 0 warnings, 0 errors, all 21 projects.
- `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj` — `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13` — all thirteen architecture rules still green, unmodified.
- `SharedKernel.UnitTests` (32), `Contracts.UnitTests` (21), `Orders.UnitTests` (24), `Seed.UnitTests` (34) — all green, no regression from this round's changes.
- `grep -ril "mediatr" --include="*.cs" --include="*.csproj" --include="*.props" --include="*.sln" .` — one match, `tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs` itself (the word appears in the guard's own name/docs, which is expected — it is testing *for* MediatR's absence, not referencing the package). No `.csproj`/`.props`/`.sln` match; no actual `PackageReference`/`PackageVersion` anywhere.
- `./init.sh` — green (`environment and state are coherent`) before and after this round.

## What was deferred, explicitly

- **A solution-wide `Architecture.Tests` equivalent of D4's guard** — scoped to `src/Cqrs`/`tests/Cqrs.UnitTests` this round per the coordinator's explicit touch-list; a natural follow-up outside it.
- **D2** — the leader's/human's, per the coordinator's explicit instruction to ignore it here. Not touched: `CLAUDE.md`, `CHECKPOINTS.md`, `AGENTS.md`.

## What surprised me this round

- The D4 arming attempt that failed first (`"zzz_never_matches_zzz"`) was
  a useful reminder that "the guard doesn't fire" and "the mutation proves
  nothing" look identical from the outside (`arm-probe.sh`'s "suite still
  green") but are different findings — the tool's message ("GUARD DOES NOT
  GUARD") reads as an accusation against the guard, when here it was an
  accusation against the *mutation*. Worth designing negative-space probes
  ("this string never matches anything") with that ambiguity in mind next
  time, rather than discovering it after the fact.
- Removing a generic method entirely (D3) rather than adding a
  same-named overload turned out to be the only fix that actually closes
  the gap, and the reasoning (overload resolution prefers an exact generic
  match over an `object` conversion) is exactly the kind of thing that
  looks obviously wrong only once you've tried the "safer-looking"
  alternative and traced why it still fails on the risky call sites.

## Third round — the fourteenth architecture rule (guard for the `src/Cqrs` amendment)

The human gate ratified `src/Cqrs` as a third shared runtime project,
conditional on one guard: **no type in any `*.Domain` namespace may depend
on `OrderToCash.Cqrs`**. `CLAUDE.md`, `CHECKPOINTS.md` C3 and `AGENTS.md`
were already amended (by the coordinator, not touched here). This round adds
only the guard, under the explicit touch-list `tests/Architecture.Tests/**`
plus this file.

### The rule, and how it's scoped

New file `tests/Architecture.Tests/CqrsDomainPurityTests.cs`, one test:
`DomainMustNotDependOnCqrs`.

```csharp
var result = Types.InAssemblies(DomainAssemblies.All)
    .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
    .ShouldNot().HaveDependencyOn("OrderToCash.Cqrs")
    .GetResult();
```

**Scoped repo-wide via `DomainAssemblies.All`, the same shape as the twelve
`DomainPurityTests` rules — not single-service like
`OrdersDomainMustNotDependOnContracts`.** The two existing scoping patterns
answer different questions and I picked the one CLAUDE.md's own wording
answers: `OrdersDomainContractsTests`'s docstring explains its narrower
scope is deliberate because "a consumer-side domain such as the projector's
may legitimately want the payload types it reads off the fact stream" — a
per-service exception the Contracts rule needs and the Cqrs rule does not.
The amended non-negotiable says "no `Domain/` namespace may reference
`OrderToCash.Cqrs`," not "Orders' `Domain/` namespace," and the reason given
is generic to every service: "every service project will reference
`src/Cqrs` from feature 15 onward" — all six, not just Orders. A
single-service scope here would silently miss five of the six services the
day each one wires up its own dispatcher-consuming `Application/` layer.
`DomainAssemblies.All` already unions every service's Domain/ layer with
`SharedKernel` in full (used the shared `DomainNamespacePattern` constant
rather than a new selector, and the existing eight-assembly set rather than
rebuilding it — no changes to `DomainAssemblies.cs`).

No `ProjectReference` to `src/Cqrs/Cqrs.csproj` was added to
`Architecture.Tests.csproj`: the check is a plain string
(`"OrderToCash.Cqrs"`) against the compiled IL of the assemblies already in
`DomainAssemblies.All`, exactly how `OrdersDomainContractsTests` checks for
`"OrderToCash.Contracts"` without the test needing to load a `Contracts`
type by name. Nothing else in `tests/Architecture.Tests/` was touched.

### Arming — and the one thing that made the standard technique impossible

Confirmed first: no `src/*.csproj` in this repository references
`src/Cqrs/Cqrs.csproj` yet (`grep -rl "Cqrs" src/**/*.csproj` → only
`src/Cqrs/Cqrs.csproj` itself; the `.sln` lists `Cqrs` and
`Cqrs.UnitTests` only). Feature 15 is the first consumer, per both the
brief and `src/Cqrs/Cqrs.csproj`'s own header comment ("No service
references this project yet"). That makes the arming technique used for
`OrdersDomainMustNotDependOnContracts` (add a real reference — a `using`
plus a `typeof(...)` field — to an existing `Domain/` file) structurally
impossible here **without also adding a `ProjectReference` to some
service's `.csproj`**, which the brief explicitly forbids touching
(`src/**` is off-limits, with no arming exception carved out). There is no
file under `tests/Architecture.Tests/**` alone that can compile a real
reference to `OrderToCash.Cqrs` from inside a `*.Domain` namespace, because
no `*.Domain`-hosting assembly has ever been told that assembly exists.

Rather than fabricate a reference by touching a forbidden file, I armed the
**mechanism** instead of a hypothetical violator: `Types.InAssemblies(...)`
+ `ResideInNamespaceMatching(DomainNamespacePattern)` +
`HaveDependencyOn(...)` is the identical shape as every one of the twelve
existing purity rules, and `OrdersDomainMustNotDependOnContracts`'s own
comment already confirms (for its own dependency) that "nothing does today"
is the normal, expected state of a freshly-added rule with no live
violator yet — the rule still has to prove it *would* fire. `Orders.Domain`
(19 files) and `Seed.Domain` (2 files) already carry a real, verified
dependency on `OrderToCash.SharedKernel` — `Order`, `OrderLine`,
`OrderPlaced`, `Money`, `GLN`, etc. all reference `SharedKernel` types
today. Swapping the forbidden-dependency string from `"OrderToCash.Cqrs"`
to `"OrderToCash.SharedKernel"` exercises exactly the same selector, the
same assembly list, and the same `HaveDependencyOn` codepath, against a
dependency guaranteed present in real, unmodified production code — a
strictly stronger proof that the plumbing fires than inventing a synthetic
fixture would have been, and it needed only one single-line, one-file
mutation, entirely inside `tests/Architecture.Tests/**`.

`scripts/arm-probe.sh` first, then a manual capture for the verbatim
message (the script only reports pass/fail counts):

```
./scripts/arm-probe.sh \
  tests/Architecture.Tests/CqrsDomainPurityTests.cs \
  's/"OrderToCash\.Cqrs"/"OrderToCash.SharedKernel"/' \
  tests/Architecture.Tests
```
→ `armed -> suite FAILED (the guard fires)` / `restored -> suite green`.

Manual capture (same mutation, backup taken first, forced rebuild both
ways, restored from the backup copy — never `git checkout --`, this file
is untracked — then re-read the restored line and `diff`ed byte-for-byte
against the backup, found identical):

> `Domain types must not depend on OrderToCash.Cqrs — src/Cqrs is an Application-layer concern; handlers live in Application/. Offending types: OrderToCash.Orders.Domain.Order, OrderToCash.Orders.Domain.OrderCompensationStep, OrderToCash.Orders.Domain.OrderLine, OrderToCash.Orders.Domain.OrderLineRequest, OrderToCash.Orders.Domain.Events.OrderCancelled, OrderToCash.Orders.Domain.Events.OrderCompleted, OrderToCash.Orders.Domain.Events.OrderConfirmed, OrderToCash.Orders.Domain.Events.OrderDomainEvent, OrderToCash.Orders.Domain.Events.OrderPlacedLine, OrderToCash.Orders.Domain.Events.OrderPlaced, OrderToCash.Orders.Domain.Errors.CancellationReasonNotApplicableError, OrderToCash.Orders.Domain.Errors.CancellationReasonRequiredError, OrderToCash.Orders.Domain.Errors.IllegalOrderTransitionError, OrderToCash.Orders.Domain.Errors.OrderLineCurrencyMismatchError, OrderToCash.Orders.Domain.Errors.OrderLineNotFoundError, OrderToCash.Orders.Domain.Errors.OrderLinesAreFrozenError, OrderToCash.Orders.Domain.Errors.OrderMustHaveAtLeastOneLineError, OrderToCash.Orders.Domain.Errors.OrderTotalMustNotBeNegativeError, OrderToCash.Orders.Domain.Errors.UnknownCancellationReasonError, OrderToCash.Orders.Domain.Errors.UnknownOrderStatusError, OrderToCash.Seed.Domain.Sagas.SagaFixtures, OrderToCash.Seed.Domain.Deterministic.Gs1Identifiers, OrderToCash.SharedKernel.AggregateRoot, OrderToCash.SharedKernel.Entity, OrderToCash.SharedKernel.GLN, OrderToCash.SharedKernel.Money, OrderToCash.SharedKernel.OrderNumber, OrderToCash.SharedKernel.Quantity, OrderToCash.SharedKernel.UniqueId, OrderToCash.SharedKernel.Errors.CurrencyMismatchError, OrderToCash.SharedKernel.Errors.InvalidCurrencyCodeError, OrderToCash.SharedKernel.Errors.InvalidGlnError, OrderToCash.SharedKernel.Errors.InvalidOrderNumberError, OrderToCash.SharedKernel.Errors.InvalidUniqueIdError, OrderToCash.SharedKernel.Errors.QuantityMustBePositiveError`

Restored: re-read line 36
(`.ShouldNot().HaveDependencyOn("OrderToCash.Cqrs")`) confirms the literal
is back; `diff` against the pre-arming backup: identical. Rebuilt
(`dotnet build --no-incremental`), reran
`dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~CqrsDomainPurityTests"`
→ `Passed! - Failed: 0, Passed: 1`. No unused-`using` trap and no
unreachable-code refusal applied here — the mutation is a string literal,
not a control-flow change, so neither of the two fussy failure modes the
coordinator flagged was in play; the risk this rule ran into instead
(no live caller to arm against, at all) wasn't one of those two, and is
worth flagging for whoever writes the next cross-cutting guard before a
consumer lands.

### Full-suite verification

- `dotnet build --no-incremental` (whole solution) — 0 warnings, 0 errors.
- `dotnet format OrderToCash.sln --verify-no-changes` — clean.
- `dotnet test tests/Architecture.Tests` — `Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14` (the thirteen pre-existing rules, unmodified, plus `DomainMustNotDependOnCqrs`).
- `dotnet test` (whole solution, all 15 test projects) — all green: `Contracts.UnitTests` 21, `SharedKernel.UnitTests` 32, `Cqrs.UnitTests` 19, `Orders.UnitTests` 24, `Architecture.Tests` 14, `Seed.UnitTests` 34, `Notifications.IntegrationTests` 7, `Orders.IntegrationTests` 13, `Fulfillment.IntegrationTests` 19, `Billing.IntegrationTests` 23, `Seed.IntegrationTests` 5 — 0 failures anywhere, no regression from adding the fourteenth rule.
- `./init.sh` — exits 0, "environment and state are coherent" (14/43 features done, no `in_progress`).
- `git status --porcelain` after this round: the only new path is `tests/Architecture.Tests/CqrsDomainPurityTests.cs`; everything else listed (`AGENTS.md`, `CHECKPOINTS.md`, `CLAUDE.md`, `Directory.Packages.props`, `OrderToCash.sln`, `feature_list.json`, `progress/current.md`, `progress/review_cqrs_dispatcher.md`, `src/Cqrs/`, `tests/Cqrs.UnitTests/`) predates this round and was not re-touched.

### What adding the fourteenth rule revealed about the existing twelve (thirteen)

Nothing structurally wrong — the existing `DomainNamespacePattern` /
`DomainAssemblies.All` machinery slotted the new rule in with zero changes
to either, which is exactly what those two feature-6/feature-7 fixes were
supposed to buy every rule added after them. The one thing worth recording
for the next cross-cutting Domain guard: `OrdersDomainMustNotDependOnContracts`
got to use a live-reference arming technique only because `Orders.csproj`
already had a legitimate reason to reference `Contracts.csproj`
(its `Infrastructure/` layer). A guard written *before* any project has a
reason to reference the forbidden thing at all — this rule's actual
situation — cannot be armed that way without either touching a forbidden
`src/*.csproj` or fabricating a same-project fixture that isn't real
production code. Swapping the target string to a dependency already proven
present (`OrderToCash.SharedKernel`, here) is the technique I'd hand to
whoever hits this next: it proves the selector/assembly-list/`HaveDependencyOn`
plumbing fires correctly without inventing anything, and it's strictly
honest about what it does and doesn't prove — it does not prove
`OrderToCash.Cqrs` specifically triggers a failure (nothing can, yet), only
that the mechanism that will check for it, the day something does, works.

### What could not be done, and why

Nothing was left undone against the brief. The one thing explicitly out of
reach was arming against a real `OrderToCash.Cqrs` reference, for the
structural reason above (no live caller, and adding one requires touching
`src/**`, which the brief forbids). This is not a gap in the guard itself —
`DomainMustNotDependOnCqrs` will fire the moment any service's `Domain/`
layer picks up the dependency, proven by the SharedKernel-substitution
arming above — it is a gap in what could be demonstrated about it *this
round*, and is exactly the situation to re-verify with a live reference
once feature 15 (`orders_acceptance`) wires `src/Cqrs` into a service for
real.

---

# Re-review round 2 — response to `progress/review_cqrs_dispatcher.md`'s round-1 re-review

Feature 43 was **REJECTED** again, on **D7** and **D8** — two guards *created
last round* to close D4 and D3, both found by the reviewer to be
guards-that-do-not-guard. **D9 is the leader's/human's and already fixed**
(`.claude/agents/reviewer.md` amended); ignored here per the coordinator's
instruction. The reviewer's own framing is worth repeating because it is the
right lens for both: *"fixes that introduced the failure they were fixing,
one level down."*

## D7 — the MediatR guard now checks a reference, not a usage

**What was wrong, precisely.** `NoMediatRReferenceTests` (round 1's D4 fix)
asserted against `Assembly.GetReferencedAssemblies()` — the assembly-
reference table Roslyn writes into emitted metadata. Roslyn only writes an
entry for an assembly whose types the code actually *calls into*. A real
`PackageVersion Include="MediatR"` + `PackageReference Include="MediatR"`
pair, added and left completely unused, produces **no entry at all** — so
the test passed outright with MediatR genuinely referenced. The doc comment
claimed the opposite: "it cannot be defeated by any route a reference could
arrive by (a direct `PackageReference` ...)". The reviewer defeated it with
exactly that route.

**Why the round-1 arming (`"mediatr"` → `"microsoft"`) did not catch this.**
That mutation proved the *reporting* path — "if offenders are found, the
assertion fires" — by substituting a search term guaranteed to match
something *already* in the assembly's reference table (a real, used
`Microsoft.Extensions.DependencyInjection` reference). It never introduced
an actual MediatR reference, so it could not have found this gap; it was
arming the mechanism, not the target the guard exists for. Exactly the
distinction the reviewer draws between `DomainMustNotDependOnCqrs` (correct
to read compiled metadata, because its rule *is* about dependency) and this
one (wrong to read compiled metadata, because acceptance item 4's rule is
about *reference*, full stop).

**Fix — the missing half of the pair, mirroring `SharedKernelHasNoPackagesTests`.**
That class is already a two-test pair for exactly this reason:
`SharedKernelCsprojDeclaresZeroPackageReferences` reads the project file
(catches present-but-unused), `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework`
reads compiled metadata (catches transitive-but-used). `NoMediatRReferenceTests`
implemented only the second half. Added the first:

- `tests/Cqrs.UnitTests/NoMediatRPackageReferenceTests.cs` — a `[Theory]`
  reading `src/Cqrs/Cqrs.csproj`, `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj`
  and `Directory.Packages.props` as raw text, via a regex over
  `<PackageReference Include="...">` / `<PackageVersion Include="...">`
  elements, asserting no captured package id contains "mediatr"
  (case-insensitive). Unlike `SharedKernel.csproj`, `Cqrs.csproj`
  legitimately has one `PackageReference`
  (`Microsoft.Extensions.DependencyInjection.Abstractions`), so the check
  is "no package identity matching MediatR", not "zero packages" —
  narrower than `SharedKernel`'s pair by necessity, same technique.
- `tests/Cqrs.UnitTests/RepositoryPaths.cs` — a local copy of
  `tests/Architecture.Tests/RepositoryPaths.cs`'s walk-up-to-`OrderToCash.sln`
  helper. Not shared across the two test projects (they share no code
  today); duplicating eight lines was the smaller change.
- **Corrected the false claim** in `NoMediatRReferenceTests.cs`'s doc
  comment: it now states precisely what the metadata check does and does
  not cover (transitive-or-actually-used, not presence), names the
  unused-reference gap it was proven to have, and points at
  `NoMediatRPackageReferenceTests` as the other half of the pair.

**Kept the existing metadata test** — the reviewer's own instruction
("Keep the existing metadata test; it is the transitive-usage half and is
worth having").

**Arming — by hand, with a real package, exactly as the reviewer's
instructions required** (`scripts/arm-probe.sh` cannot express a package
install, so this round's arming table records a manual protocol run
instead):

1. **Backed up** `Directory.Packages.props` and
   `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` to the scratchpad
   (`cp`, before any mutation).
2. Added `<PackageVersion Include="MediatR" Version="14.0.0" />` to
   `Directory.Packages.props` and `<PackageReference Include="MediatR" />`
   to `Cqrs.UnitTests.csproj` (same version the reviewer used, already
   NuGet-cached locally from their own probe).
3. `dotnet restore` + `dotnet build --no-incremental` — compiled clean.
4. `dotnet test tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` —

   ```
   [xUnit.net ...]     NoMediatRPackageReferenceTests.ProjectFileDeclaresNoMediatRPackageReferenceOrVersion(relativePath: "Directory.Packages.props") [FAIL]
   [xUnit.net ...]     NoMediatRPackageReferenceTests.ProjectFileDeclaresNoMediatRPackageReferenceOrVersion(relativePath: "tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj") [FAIL]
     Error Message:
      Directory.Packages.props must declare no PackageReference or PackageVersion whose package id contains "MediatR" ... Offending package ids: MediatR
   Failed!  - Failed: 2, Passed: 20, Skipped: 0, Total: 22
   ```

   — the new guard fires on **both** files, and (checked separately, by
   filtering to just `NoMediatRReferenceTests`) the **old** metadata test
   stayed green throughout: `Passed! - Failed: 0, Passed: 2, Skipped: 0,
   Total: 2` — reproducing the reviewer's exact finding, side by side, in
   the same tree.
5. **Restored from the backups** (never `git checkout --` — `Directory.Packages.props`
   is a tracked-but-modified file, and checking it out would have reverted
   this feature's own legitimate `PackageVersion` additions from round 1,
   not just the MediatR probe).
6. Confirmed by re-reading both files (`grep -n "MediatR"` → no matches in
   either).
7. `dotnet restore` + `dotnet build --no-incremental` — clean.
8. `rm -rf` both projects' `bin`/`obj` and rebuilt from scratch, to rule out
   a stale `MediatR.dll` lingering in an output directory after the probe
   — `find ... -iname "*mediatr*"` empty both before and after.
9. `dotnet test tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` — `Passed! -
   Failed: 0, Passed: 22, Skipped: 0, Total: 22`.

## D8 — `PublishAsync` now has its own `CancellationToken` forwarding test

**What was wrong, precisely.** Round 1's D3 fix replaced a direct,
generically-typed `await handler.HandleAsync(@event, cancellationToken)`
with `handleAsyncMethod.Invoke(handler, [@event, cancellationToken])` —
an `object[]` argument array that the compiler does not type-check and that
CA2016 (an error in this repository) cannot see through. The token-
forwarding *guarantee* that used to come free from the compiler on that
line quietly stopped existing, and round 1 added no test to replace it —
three `ForwardsTheCancellationToken` tests exist for `SendAsync`
(void), `SendAsync` (with result) and `QueryAsync`; none for `PublishAsync`.
The reviewer armed the gap directly: swapping the forwarded token for
`CancellationToken.None` inside the `Invoke` call left the full 19-test
suite green.

**Fix.**

- `ConcreteUpstreamFactHandler` (the D3 fixture, `Fixtures/WellFormedFixtures.cs`)
  gained a `static CancellationToken? LastToken` alongside its existing
  `LastReceived`, set in `HandleAsync`.
- `DispatcherTests.PublishAsync_ForwardsTheCancellationTokenToTheHandler` —
  same shape as the three existing forwarding tests: publish through the
  (already D3-relevant) `IUpstreamFact`-typed variable with a live
  `CancellationTokenSource.Token`, assert the handler saw that exact token.

**Arming**, via `scripts/arm-probe.sh` first, then a manual run to capture
the verbatim message:

```
./scripts/arm-probe.sh src/Cqrs/Dispatcher.cs \
  's/handleAsyncMethod.Invoke(handler, \[@event, cancellationToken\])/handleAsyncMethod.Invoke(handler, [@event, CancellationToken.None])/' \
  tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj
  armed    -> suite FAILED (the guard fires)
  restored -> suite green
```

Manual capture:

```
Failed OrderToCash.Cqrs.UnitTests.DispatcherTests.PublishAsync_ForwardsTheCancellationTokenToTheHandler [42 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: System.Threading.CancellationToken
Actual:   System.Threading.CancellationToken
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

(The `CancellationToken` struct's `ToString()`/default `Assert.Equal`
formatting does not print the underlying source distinctly, but the failure
is real — `cts.Token` and `CancellationToken.None` are provably different
tokens, confirmed by the assertion actually firing.) Restore confirmed by
re-reading the line (`grep -n "handleAsyncMethod.Invoke"` →
`..., cancellationToken])!;`, not `CancellationToken.None`) and by the full
suite going green again: `Passed! - Failed: 0, Passed: 23, Skipped: 0,
Total: 23`.

## Cosmetic item also fixed (listed non-blocking, fixed anyway — cheap and in scope)

`DispatcherServiceCollectionExtensions.cs` had two consecutive `<summary>`
blocks on `ExpectedCommandHandlerServiceTypes` — the second was meant to be
`<param name="declarationErrors">`. Fixed: one `<summary>`, plus proper
`<param name="concreteTypes">` and `<param name="declarationErrors">` tags.
Builds clean before and after (malformed doc XML is not a compiler error in
this project), fixed for correctness of the documentation itself.

## Files touched this round

```
tests/Cqrs.UnitTests/NoMediatRPackageReferenceTests.cs — new: D7's missing half of the pair
tests/Cqrs.UnitTests/RepositoryPaths.cs                — new: local path-finding helper for the above
tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs        — doc comment corrected (D7)
tests/Cqrs.UnitTests/Fixtures/WellFormedFixtures.cs    — ConcreteUpstreamFactHandler.LastToken (D8)
tests/Cqrs.UnitTests/DispatcherTests.cs                — +1 test: PublishAsync_ForwardsTheCancellationTokenToTheHandler (D8)
src/Cqrs/DispatcherServiceCollectionExtensions.cs      — malformed doc-XML fix only (no behaviour change)
```

`Directory.Packages.props` and `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj`
were mutated and restored during D7's arming; both are back to their exact
round-1 state (`git diff` confirms — see verification below), no residue.

## Verification run, this round

- `dotnet build --no-incremental` on `src/Cqrs/Cqrs.csproj` and
  `tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` — 0 warnings, 0 errors.
- `dotnet format OrderToCash.sln --verify-no-changes` — exit 0.
- `dotnet test tests/Cqrs.UnitTests/Cqrs.UnitTests.csproj` — `Passed! -
  Failed: 0, Passed: 23, Skipped: 0, Total: 23` (19 tests at the end of
  round 1, +3 from `NoMediatRPackageReferenceTests`'s three `[InlineData]`
  cases (D7) +1 from `PublishAsync_ForwardsTheCancellationTokenToTheHandler`
  (D8) = 23).
- `dotnet build OrderToCash.sln --nologo` — 0 warnings, 0 errors, all
  projects (including the D9/D2-amendment files already fixed by the
  coordinator, untouched by me).
- `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj` —
  `Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14` — the
  fourteenth rule (`DomainMustNotDependOnCqrs`, closed at the human gate,
  not this feature's file to touch) stays green, unmodified by this round.
- `SharedKernel.UnitTests` (32), `Contracts.UnitTests` (21),
  `Orders.UnitTests` (24), `Seed.UnitTests` (34) — all green, no
  regression.
- `grep -ril "mediatr" --include="*.cs" --include="*.csproj" --include="*.props" --include="*.sln" .`
  — three matches, all `tests/Cqrs.UnitTests/*.cs` file names/doc-comment
  prose (`NoMediatRReferenceTests.cs`, `NoMediatRPackageReferenceTests.cs`,
  `RepositoryPaths.cs`'s doc comment naming the former) — no actual
  `PackageReference`/`PackageVersion`, confirmed by `git diff
  Directory.Packages.props` showing only the two legitimate DI entries from
  round 1.
- `./init.sh` — green (`environment and state are coherent`), `no feature
  in_progress` after this round's status update.
- `git status --porcelain` — exactly this round's touched files
  (`tests/Cqrs.UnitTests/**` additions, `src/Cqrs/DispatcherServiceCollectionExtensions.cs`
  doc-only edit) plus the already-`M`/`??` state from D1–D6 and the
  coordinator's own D9/D2 files, which I did not re-touch.

## What was NOT touched this round, confirmed

`.claude/agents/reviewer.md`, `AGENTS.md`, `CHECKPOINTS.md`, `CLAUDE.md`,
`README.md`, `docs/PROCESS.md`, `tests/Architecture.Tests/CqrsDomainPurityTests.cs`
— all already modified by the coordinator (D9/D2) before this round began;
left exactly as found.

## What surprised me this round

- Both defects are the same shape the reviewer named: a fix that solved
  the reported problem while quietly discarding a property the *old*
  mechanism had provided for free. D4's grep became a metadata test that
  is blind to presence; D3's compile-time call became a reflective one
  that is blind to CS/CA analysis. Neither loss was visible from inside
  the fix itself — both only showed up when someone asked "what did the
  old mechanism actually guarantee, and does the new one still guarantee
  it?" rather than "does the new mechanism solve the reported bug?". That
  is a sharper question to carry into any future fix that changes *how*
  something is checked, not just *what* passes.
- D7's arming needed a real package because the previous round's
  `"mediatr"` → `"microsoft"` substitution proved the assertion's
  reporting path, not its detection path — those are different claims, and
  a mutated string can prove the first while saying nothing about the
  second. The `scripts/arm-probe.sh` tool is exactly right for single-file
  source mutations; a dependency-graph-level claim ("this reference is
  genuinely absent from the solution") needed a dependency-graph-level
  probe (an actual `dotnet add package`), and no sed expression could have
  substituted for that honestly.
