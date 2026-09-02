# review_cqrs_dispatcher (feature 43, phase 8, `sdd: false`)

## Verdict: REJECTED

One blocking defect (**D1**), empirically demonstrated twice, in `src/Cqrs/Dispatcher.cs` + `src/Cqrs/DispatcherServiceCollectionExtensions.cs:59`. It is not covered by the four acceptance items, and every acceptance item does pass — but it reintroduces, in the dispatcher itself, the exact failure class this feature exists to eliminate: a DI misconfiguration invisible at boot and discovered at first dispatch, or silently never discovered at all in Production. Rejecting on a defect outside the acceptance list is deliberate: the acceptance list describes the mechanism, `CLAUDE.md`'s non-negotiable ("DI failures must be loud at boot") describes what the mechanism is *for*, and this one defeats it.

Plus one process defect (**D2**) that is the leader's/human's to close, not the implementer's, and which keeps `CHECKPOINTS.md` C3 open regardless of D1.

Everything else is good, and several parts are notably good: the guards are genuinely armed (five independent probes, three of them mine, all fired), the marker-interface argument is sound, and the asymmetry is implemented exactly as the report describes.

---

## Conventions checked against the file on disk, not the injected copy

`grep`-ed `CLAUDE.md` at review time (`AGENTS.md:46` standing rule). Relevant lines as they actually stand:

- **`CLAUDE.md:92`** — "The hand-rolled dispatcher is binding (human gate ruling, Phase 8 — ratified across all six services ...)". Amended since the injected copy; the dispatcher's mandate is now explicit and this feature is squarely inside it.
- **`CLAUDE.md:98`** — "The only shared runtime code is `src/SharedKernel` (zero `PackageReference`) and `src/Contracts` (generated types). **Nothing else is shared.**" — **not** amended. See D2.
- **`CLAUDE.md:136`** — arming protocol, three clauses, as the injected copy had it.

No rule was enforced from a stale cache, and no rule in the repository was weakened by this feature.

---

## CHECKPOINTS.md — boxes walked

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (+ suite_runner).
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — re-run by me, `environment and state are coherent`, wall-clock `0:01.18`.

### C2 — State is coherent

- [x] At most one feature `in_progress` — feature 43 was `in_review`; set back to `in_progress` by this rejection, and it is the only one.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it — the full solution suite is green (below).
- [x] `progress/current.md` describes the active session.
- [x] No `blocked` features.

### C3 — Architecture is respected

- [x] No `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` inside any `Domain/` folder — **verified by running the NetArchTest suite**, not by eye: `OrderToCash.Architecture.Tests.dll` `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13`. All thirteen rules unmodified by this feature (`git diff --stat -- tests/` is empty).
- [x] No cross-service database access — this feature touches no database.
- [ ] **No shared runtime code beyond `src/SharedKernel` and `src/Contracts`.** — **FALSE as the rule stands on disk.** `src/Cqrs` is a third shared runtime project. See **D2**. The *placement* is right; the *rule* was left unamended.
- [x] `src/SharedKernel` still has zero `PackageReference` entries — `SharedKernelHasNoPackagesTests` green in the run above; the implementer explicitly declined to put the dispatcher there for this reason, which was the correct call.
- [x] No `decimal` in domain arithmetic — `DomainDecimalTests` green; `src/Cqrs` contains no arithmetic at all.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — this feature adds no inter-service interaction. The in-process dispatcher is explicitly *not* a transport (`Dispatcher.cs:7-10` records that durability stays with `outbox` / `saga_commands`).
- [x] No stray debug logging, no context-free TODOs — `src/Cqrs` has neither.

### C4 — Verification is real

- [x] `./quality.sh` passes — **re-run by me in full**, `EXIT=0`, wall-clock **`2:16.42`**. Format check clean, build clean, all test projects green including the six Testcontainers integration suites (Notifications 7, Fulfillment 19, Orders 13, Billing 23, Seed 5) which the implementer did **not** run.
- [x] Domain tests are pure — `Cqrs.UnitTests` uses only xUnit + `Microsoft.Extensions.DependencyInjection`; no DB, no broker, no mock of infrastructure. (`Cqrs` is not a domain layer, so `DomainAssemblies.All`'s fixed eight-assembly list correctly excludes it.)
- [x] Integration tests use real containers — unchanged by this feature, and all six ran green.
- [x] Coverage collected: `Cqrs.UnitTests` line coverage 96.6%. (The gate itself is feature 34; `quality.sh:6-9` is explicit that it reports rather than enforces, so no inert-gate claim is made here.)
- [x] No Jest anywhere — `grep -ril jest apps/` returns nothing.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — the 18 untracked paths are exactly this feature's source, tests and report. No `*.tmp`, no build output outside `.gitignore`. **No arming residue**: `grep -n "CancellationToken.None\|Take(1)" src/Cqrs/Dispatcher.cs` returns nothing after all three of my probes, and all four `cancellationToken` forwards are intact.
- [ ] `progress/history.md` has an entry for the feature — **not applicable / deliberately not written.** Rejected features do not get a history entry.
- [x] `feature_list.json` reflects the true state — set to `in_progress` by this review.
- [x] The human will be told what was done and how to test it.
- [x] Claude did not commit — `git log` unchanged, working tree still has the 7 uncommitted changes `init.sh` reports.

### C6 — Spec-Driven Development

**N/A** — feature 43 is `sdd: false`, correctly so (no `R<n>` covers it; it is new to #8 per its own `note`). No `specs/cqrs_dispatcher/` is required and none was created. No `specs/shared/test-matrix.md` row was added, which is right — adding one would have been a false parity claim.

### C7 — Spec-reuse fidelity

Not exercised by this feature (it touches no `specs/shared/` artifact, no n8n workflow and no API script), except the honesty clause, which is satisfied: the report opens by stating there is **no #7 counterpart**, and does not dress a new-in-#8 feature as a reuse win.

---

## Acceptance-item-by-item walk

| # | Acceptance item | Verdict | Evidence I checked myself |
|---|---|---|---|
| 1 | `ICommandHandler<T>`, `ICommandHandler<T,R>`, `IQueryHandler<T,R>`, `IEventHandler<T>` + a `Dispatcher` resolving from `IServiceProvider` | **PASS** | All four exist (`src/Cqrs/ICommandHandler.cs:21,30`, `IQueryHandler.cs:6`, `IEventHandler.cs:21`); `Dispatcher.cs:16-20` takes `IServiceProvider` and resolves through it on all four paths. Four tests, one per shape, all dispatching through `IDispatcher`. |
| 2 | Handlers registered by assembly scan | **PASS** | `DispatcherServiceCollectionExtensions.cs:28-35` → `SafeGetTypes` → `GetInterfaces()` filtering. `DispatcherTests.cs:22` wires every fixture with the single call `services.AddDispatcher(typeof(PingCommand).Assembly)`; no test registers a handler by hand — I confirmed by reading, not by taking the claim. |
| 3 | Startup validation FAILS FAST — zero handlers **and** more than one, proven by a test for each | **PASS** | `DispatcherRegistrationValidator.cs:67` (zero) and `:71` (>1), reached from `AddDispatcherFromTypes:102` — i.e. **inside `AddDispatcher`, during registration**, before `BuildServiceProvider` is ever called. Four tests, two per case (command + query). Both branches armed by the leader; I re-derived the fail-at-boot claim independently — see "Is the validation really at boot?" below. |
| 4 | No MediatR reference anywhere in the solution | **PASS** | `grep -ril mediatr --include=*.cs --include=*.csproj --include=*.props --include=*.sln .` → zero matches (excluding the word in `feature_list.json`'s own `note` and the progress report). Advisory **D4** below. |

All four acceptance items pass. The rejection is on D1, which sits underneath them.

### Is the validation really at boot, not at first dispatch?

Yes, for the thing it validates, and I checked this rather than reading it. `AddDispatcher` throws from inside the `IServiceCollection` extension itself — the exception escapes `builder.Services.AddDispatcher(...)` in a `Program.cs`, i.e. before `builder.Build()`, before `app.Run()`. Nothing is deferred to a `IHostedService`, an `IStartupFilter`, or a lazy field. My scope probe (below) confirms `AddDispatcher` runs the validator eagerly: it threw during `services.AddDispatcher(...)`, not on `BuildServiceProvider` and not on first `SendAsync`.

The sting is that **D1 is a boot-invisible DI defect that this very validation pass cannot see**, and that is why it is blocking rather than advisory.

---

## Defects

### D1 — BLOCKING. `IDispatcher` is registered `Singleton`, so it captures the **root** provider and every handler is resolved outside the request scope

**Where:** `src/Cqrs/DispatcherServiceCollectionExtensions.cs:59` — `services.AddSingleton<IDispatcher, Dispatcher>();` — in combination with `src/Cqrs/Dispatcher.cs:16-20`, which stores the injected `IServiceProvider`, and `:26`, `:34`, `:42`, `:49`, which resolve handlers from it.

**Why it matters.** A singleton is constructed in the root scope, so the `IServiceProvider` handed to `Dispatcher`'s constructor is the **root provider**, permanently. Every `GetRequiredService<ICommandHandler<T>>()` therefore resolves from root, no matter which request scope the caller is in. From feature 15 onwards every command handler in Orders, Fulfillment and Billing will depend on a repository over an EF Core `DbContext`, which `AddDbContext` registers **scoped**. `Dispatcher` is a service-locator, so the built-in call-site validator cannot see through it — `ValidateOnBuild` passes and boot succeeds.

**Evidence — I built a standalone probe against the real `src/Cqrs/Cqrs.csproj`, with a scoped dependency standing in for a `DbContext`.** Two runs, the two host configurations that matter:

Development defaults (`ValidateScopes: true, ValidateOnBuild: true`), dispatching from inside a proper `CreateScope()`:

```
IDispatcher lifetime = Singleton
BuildServiceProvider(ValidateOnBuild:true) -> OK, BOOT SUCCEEDED
FIRST DISPATCH THREW: InvalidOperationException: Cannot resolve 'OrderToCash.Cqrs.ICommandHandler`1[DoThingCommand]' from root provider because it requires scoped service 'FakeDbContext'.
```

Production defaults (`ValidateScopes: false`), two consecutive request scopes:

```
req1: scoped-direct=da60e7ef-...  handler-saw=23462559-...  same=False
req2: scoped-direct=aa577e9e-...  handler-saw=23462559-...  same=False
handler saw the SAME instance across two separate scopes: True  (captive dependency)
```

So: **in Development it is a first-dispatch crash the boot did not catch; in Production it is silent** — one captive `DbContext` shared by every request for the lifetime of the process, never disposed, used concurrently. `CLAUDE.md:96`-ish ("DI failures must be loud at boot ... turns 'a handler is missing' from a runtime surprise into a boot failure") is the reason this feature exists; a dispatcher that boots clean and then either throws on first use or silently corrupts request isolation is that lesson un-learned inside the very component built to teach it.

This is also not something feature 15 can be trusted to notice: an integration test that dispatches once per test, in a fresh host, would go green in Production configuration while the captive `DbContext` quietly accumulates tracked entities.

**What must change.** Register `IDispatcher` as **scoped** (`services.AddScoped<IDispatcher, Dispatcher>()`), so the injected `IServiceProvider` is the *scoped* provider and handlers resolve inside the caller's scope; background services (Kafka consumers, the outbox relay, NATS responders) then resolve `IDispatcher` from a scope they create per message, which they must do anyway. If a singleton `IDispatcher` is genuinely wanted for a hosted-service caller, the alternative is to inject `IServiceScopeFactory` and open a scope per dispatch inside `Dispatcher` — but that hides scope ownership from the caller and is the worse of the two. Either way, **the fix needs its own test**, and it must be one that fails on the current code: a handler with a scoped dependency, dispatched from two separate scopes, asserting the handler saw two different instances. Under the current registration that test fails; that is the arming proof.

### D2 — BLOCKING for C3, and it is the leader's to close, not the implementer's. `src/Cqrs` is a third shared runtime project and `CLAUDE.md:98` still forbids one

**Where:** `CLAUDE.md:98` on disk — "The only shared runtime code is `src/SharedKernel` (zero `PackageReference`) and `src/Contracts` (generated types). **Nothing else is shared.**" — and `CHECKPOINTS.md` C3's matching box.

**Why it matters.** The implementer's placement reasoning is **correct and I endorse it**: `SharedKernel` cannot take `Microsoft.Extensions.DependencyInjection.Abstractions` without breaking two architecture tests that exist for a good reason, and `Contracts` is the wire contract — an in-process dispatcher does not belong in either. The brief authorised a new project and told it not to weaken an existing rule to fit, and **it did not weaken any rule**: I verified `git diff --stat -- tests/` is empty and all thirteen architecture rules pass unmodified. That is the right behaviour.

But the consequence is that a non-negotiable in `CLAUDE.md` and a checkpoint box in `CHECKPOINTS.md` are now literally false, with nothing recording the exception. `CLAUDE.md:59` says this repository amends its own conventions at human gates *on purpose*, and `CLAUDE.md`'s own commit discipline says an amendment is explicit, human-gated and recorded. The Phase 8 gate ratified the dispatcher (`CLAUDE.md:92` proves that much landed); it did not ratify a third shared project.

The implementer flagged `AGENTS.md:30`'s now-incomplete `src/` description, which is the *cosmetic* one. It missed the two with teeth. That is a reporting gap, but the fix is a doc edit outside the implementer's touch list, so I record it against the feature rather than against the implementer.

**What must change.** Before feature 43 can close: amend `CLAUDE.md:98` at the human gate to name `src/Cqrs` as the third shared runtime project (with the one-line reason: the dispatcher is binding across all six services per the Phase 8 ruling, and it needs a DI package `SharedKernel` may not have), update `CHECKPOINTS.md` C3's wording to match, and fix `AGENTS.md:30`. Ideally add an architecture test asserting `src/Cqrs` has exactly the one `PackageReference` and that no service `Domain/` namespace references `OrderToCash.Cqrs` — the dispatcher is an Application-layer concern and nothing should stop a future feature from `using OrderToCash.Cqrs` inside a `Domain/` folder today.

### D3 — Advisory, but flag it loudly to feature 14/15. `PublishAsync<TEvent>` binds `TEvent` at **compile time**, so publishing through an interface-typed variable is a silent no-op

**Where:** `src/Cqrs/Dispatcher.cs:46-49` — `PublishAsync<TEvent>(TEvent @event, ...)` resolving `GetServices<IEventHandler<TEvent>>()`.

**Why it matters.** `TEvent` is inferred from the *static* type of the argument. The natural way to drain an aggregate or an outbox row is `IReadOnlyList<IDomainEvent> events = order.DomainEvents; foreach (var e in events) await dispatcher.PublishAsync(e, ct);` — which infers `TEvent = IDomainEvent`, finds no `IEventHandler<IDomainEvent>`, and, because zero handlers is deliberately not an error, **does nothing and reports success**. Verified:

```
(a) publish with concrete static type      -> handler calls = 1
(b) publish through IDomainEvent variable  -> handler calls = 1  (unchanged => SILENT NO-OP)
```

The events asymmetry (D-below: correct and wanted) is what converts this from a loud "no handler" into silence. It is not wrong today — nothing publishes yet — but it is a loaded gun aimed at feature 14's outbox relay and feature 15's aggregate drain. Fix when a consumer arrives: overload `PublishAsync(object @event, CancellationToken)` that closes `IEventHandler<>` over `@event.GetType()` via `MakeGenericType`, or document the constraint on `IDispatcher.PublishAsync` in a way the first consumer cannot miss. Not blocking for this feature; **must not be discovered by feature 14 the hard way**.

### D4 — Advisory. Acceptance item 4 is proven by a grep, not by a guard

"No MediatR reference anywhere in the solution" is true today and I verified it, but nothing *keeps* it true — there is no architecture test, and this repository's own recurring lesson is that a rule without a failing test is a rule that stops holding without telling you. Central Package Management gives partial cover (a versionless `PackageReference Include="MediatR"` fails the build), but a `PackageVersion` entry plus a reference passes. Cheap fix, one NetArchTest-adjacent assertion over the referenced assembly names, in the existing `Architecture.Tests` project. Not blocking — the acceptance item does not say "proven by a test" (item 3 does, and item 3 has them).

### D5 — Advisory. Two `AddDispatcher` calls validate two disjoint universes

`AddDispatcherFromTypes` validates only the type set of the call it is in. A service whose commands live in one assembly and handlers in another must pass **both** to a **single** `AddDispatcher(a, b)` call; two sequential calls would raise a false "No command handler is registered". Also, calling it twice adds a duplicate `AddSingleton<IDispatcher, Dispatcher>()` (`:59`). Documented implicitly by the XML remarks ("every command and every query type discovered in those same assemblies"), but worth an explicit sentence, since `CLAUDE.md:94` now mandates the dispatcher in the Gateway too, where the handler/DTO split across assemblies is most likely.

### D6 — Advisory. `ICommand` and `ICommand<TResult>` are not mutually exclusive in code

`ICommand.cs:5-6` says "either this interface or `ICommand<TResult>` — never both, and never neither", but nothing enforces it; a type implementing both is silently required to have two handlers. A one-line check in `ExpectedCommandHandlerServiceTypes` (`:144-158`) turning that into a validation error would make the doc comment true.

---

## Where the brief asked me to concentrate — findings

### 1. The three types beyond the acceptance list: the argument is SOUND. Not scope creep.

`ICommand`, `ICommand<TResult>` and `IQuery<TResult>` are load-bearing, not decoration. The reasoning holds up under pressure: acceptance item 3 requires detecting a command with **zero** handlers, and "zero" is only decidable against a known universe of command types. A scan over handler *classes* can detect one-or-more and can detect duplicates, but it can never detect an absence — an unwritten handler leaves no artifact. Without markers, half of acceptance item 3 is not merely harder, it is **unsatisfiable**.

I looked for a cheaper way to get the same enumeration and did not find a better one:

- **An attribute** (`[Command]`) — same declaration cost, strictly weaker: no compile-time link from the command to its `TResult`, and no way to write the `where TCommand : ICommand` constraints that make `Dispatcher.SendAsync<TCommand, TResult>` type-safe at all.
- **A naming convention** (`*Command` suffix) — fewer lines, but a string-matching guard silently stops guarding on the first rename. Precisely the guard-that-does-not-guard pattern this harness exists to catch.
- **Deriving the universe from handler signatures** — circular; cannot detect zero, which is the requirement.
- **A hand-maintained list passed to `AddDispatcher`** — ruled out by `CLAUDE.md:92`, "Registration is by assembly scan", and it drifts.

So: three empty interfaces, zero runtime cost, and they additionally buy the generic constraints on `ICommandHandler<>`/`ICommandHandler<,>`/`IQueryHandler<,>` that make the dispatcher's signatures type-safe. "MediatR has `IRequest` too" is indeed not a justification, and the report does not lean on it — it derives the need from the zero-handler requirement and only then observes the convergence. That is the right order of argument. **Accepted.**

### 2. The command/query/event asymmetry: implemented, tested, and the report matches the code.

Checked all three, not just the prose:

- **Events are genuinely unconstrained.** `IEventHandler<in TEvent>` (`IEventHandler.cs:21`) has no `where` clause, and there is no `IEvent` marker anywhere in `src/Cqrs`. Nothing enumerates an event universe, so nothing can fail on account of one.
- **Registration-time**: `AddDispatcher_EventWithZeroHandlers_DoesNotThrow` (`DispatcherValidationTests.cs:93`) hands the validator a fact type with no handler and asserts no throw. **Dispatch-time**: `PublishAsync_WithZeroRegisteredHandlers_CompletesWithoutError` (`DispatcherTests.cs:113`) publishes `UnlistenedFact`, which has no `IEventHandler` implementation anywhere in the test assembly. Both halves covered — that is the right pairing, because either alone would leave the other half unproven.
- **The other direction is safe too** — the failure the brief warns about (booting-fails-because-nobody-listens-yet, which would make the outbox unusable from feature 14) cannot happen: the event branch at `DispatcherServiceCollectionExtensions.cs:91-98` registers and returns, and never calls `Record`, so events never enter either dictionary the validator reads.
- **Fan-out is real and armed** — my probe C (below) proves `PublishAsync_ReachesEveryRegisteredEventHandler` fails if the loop only reaches the first handler.
- **The query decision** (same "exactly one" rule as commands, both directions) is implemented as one rule applied twice (`CollectErrors` called for both kinds at `:41-42`) and covered by four tests, and the reasoning in `DispatcherRegistrationValidator.cs`'s remarks matches what the code does. The report's account is accurate throughout — I found no place where the prose claimed something the code does not do.

### 3. Boot, not first dispatch: **YES for what it validates**, but see D1.

Validation is eager, inside the `IServiceCollection` extension, before `BuildServiceProvider`. Nothing is deferred. My probe confirmed the throw happens during `AddDispatcher(...)` itself.

The uncomfortable finding is D1: the one DI defect actually present in this feature is exactly the kind this validation pass is structurally blind to, because a service-locator hides its resolutions from both the validator and the built-in call-site checker. The feature satisfies the letter *and* the spirit of item 3 and then trips over the same class of bug one level down.

### 4. Async correctness: clean, and the token tests are genuinely armed.

- Zero `async void`, zero `.Result`, zero `.Wait()`, zero `.GetAwaiter().GetResult()` in `src/Cqrs` — checked, not assumed.
- Three of the four dispatcher methods return the handler's `Task` directly (no needless async state machine, and no CS1998); `PublishAsync` is the only `async` method and it needs to be, because it loops — and it correctly `ConfigureAwait(false)`s (`Dispatcher.cs:53`).
- `CancellationToken` is forwarded on all four paths, and — the part the brief asked me to check — **the tests would fail if it were dropped, rather than merely passing one in**. Probes A and B below replace the forwarded token with `CancellationToken.None` and the suite goes red. They are real assertions, not decoration: the fixtures capture the received token into a static and the tests compare it against a live `CancellationTokenSource.Token`, which is not equal to `CancellationToken.None`.
- Build is 0 warnings under `TreatWarningsAsErrors` with CS1998/CS4014/CA2016/CA2213 as errors — re-verified by `quality.sh` step 2.

### 5. Placement: right on the merits, wrong on the paperwork. See D2.

`src/Cqrs/` is the correct home and no existing rule was weakened to fit it — I verified `tests/` is untouched and all thirteen architecture rules pass unmodified. But the new project has **no architecture rule of its own**, and one existing rule (`CLAUDE.md:98` / C3) is now false without an amendment. Both recorded in D2.

### 6. `InternalsVisibleTo.cs`: justified, and this is the "testing internals is right" case — narrowly.

It exposes exactly **one** member, `AddDispatcherFromTypes` (`DispatcherServiceCollectionExtensions.cs:54`), to exactly **one** assembly, `OrderToCash.Cqrs.UnitTests`. It is not a blanket grant and it is not covering for a weak public surface.

The reason it is genuinely necessary rather than convenient: the validation scenarios need *closed* generic probe types, and a closed generic instantiation built via `MakeGenericType` is not declared in any assembly, so `Assembly.GetTypes()` never returns it — there is therefore **no assembly you could hand to the public `AddDispatcher(params Assembly[])`** to reach those scenarios. The open-generic trick (`ValidationProbes.cs`) is what keeps the deliberately-broken fixtures from poisoning the well-formed whole-assembly scan, and it works only in combination with the type-list seam. The alternatives the report names (a second fixtures project; `Reflection.Emit`) are correctly weighed and correctly rejected.

The mild smell is on the other side: `IEnumerable<Type>` is arguably the *better* public overload — a service may legitimately want to restrict its scan to one namespace — and if it were public, no `InternalsVisibleTo` would be needed at all. Not a defect; worth a thought when feature 15 wires the first service.

### 7. Not wired into any service: CONFIRMED.

`git diff --stat -- src/ tests/ apps/` on tracked files is **empty**. No `.csproj` under `src/*/` references `Cqrs.csproj`. `src/Orders/**` and all six services are untouched. The only tracked changes in the tree are `Directory.Packages.props` (two `PackageVersion` entries, both commented, both `10.0.11`, in the same band as the EF Core packages), `OrderToCash.sln` (two project entries), `feature_list.json` (status only) and `progress/current.md`.

### 8. Suite results and wall-clock.

| Command | Result | Wall-clock |
|---|---|---|
| `./quality.sh` (format + build + **full solution** test + coverage) | **exit 0**, all green | **`2:16.42`** |
| `./init.sh` | exit 0, `environment and state are coherent` | `0:01.18` |
| `tests/Architecture.Tests` (inside `quality.sh`) | `Failed: 0, Passed: 13, Total: 13` | 972 ms |
| `tests/Cqrs.UnitTests` (5 separate runs via `arm-probe.sh`) | `Failed: 0, Passed: 13, Total: 13` when restored | — |

`dotnet format --verify-no-changes` clean (step 1 of `quality.sh`). Build 0 warnings, 0 errors. Coverage for `Cqrs.UnitTests` 96.6%.

---

## My own arming table

Three probes, all on branches the leader did **not** cover, all via `scripts/arm-probe.sh` (backup-first, forced rebuild both ways, restore from backup never `git checkout`).

| # | Branch under test | Mutation | Result |
|---|---|---|---|
| A | `CancellationToken` forwarding on **both** `SendAsync` overloads (`Dispatcher.cs:27,35` — one sed hits both) | `s/return handler.HandleAsync(command, cancellationToken);/return handler.HandleAsync(command, CancellationToken.None);/` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` |
| B | `CancellationToken` forwarding on `QueryAsync` (`Dispatcher.cs:43`) | `s/return handler.HandleAsync(query, cancellationToken);/return handler.HandleAsync(query, CancellationToken.None);/` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` |
| C | Event fan-out reaching **every** handler, not just the first (`Dispatcher.cs:49`) | `s/GetServices<IEventHandler<TEvent>>();/GetServices<IEventHandler<TEvent>>().Take(1);/` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` |

Residue check after all three: `grep -n "CancellationToken.None\|Take(1)" src/Cqrs/Dispatcher.cs` returns nothing, and all four `cancellationToken` forwards plus the unqualified `GetServices` are back on disk. Confirmed by re-reading the lines, and by the subsequent full-solution green in `quality.sh`.

Note on probe C's mutation choice: `.Take(1)` rather than `.Take(0)` deliberately — `.Take(0)` would also have been caught, but `.Take(1)` is the *plausible* regression (someone "simplifying" a fan-out to a single resolve), and it proves the test asserts **both** handlers received the fact rather than merely that one did.

**Plus two non-probes worth recording**, both showing `arm-probe.sh` doing its job by refusing: the leader's `if (false)` attempt, rejected because CS0162 under `TreatWarningsAsErrors` means the armed source does not compile, and a build failure is not a fired guard. I hit the same wall reasoning about probe C and chose a runtime-false mutation from the start.

## Guards I did NOT re-run, and why

- **The leader's two validation probes** (zero-handler `Count == 0` → `Count == -1`, duplicate `Count > 1` → an always-false comparison) — explicitly established in the brief, both fired, both restored clean. Minor discrepancy worth recording: the brief names the duplicate mutation as `Count > 9999`, the implementer's report as `Count > int.MaxValue`. Both are the same mutation class — runtime-false rather than compile-time-false, which is what keeps CS0162 from turning the probe into a build failure — so the guard proof stands either way, but the two accounts should not disagree on what was run. Re-running them would be duplicated cost with no new information. I did independently verify that both branches exist on disk in their un-armed form (`DispatcherRegistrationValidator.cs:67,71`) and that the four tests asserting them are real assertions on the exception **message content**, not bare `Assert.Throws` — they check `"No command handler is registered"`, `"2 command handlers are registered"`, and the offending type name, so a validator throwing the right exception for the wrong reason would still be caught.
- **The six Testcontainers integration suites individually** — I did not run them one at a time; `quality.sh` ran all of them as part of the full-solution pass, which is the claim being tested ("`quality.sh` green"), so a full run was the right instrument here. All six green.
- **The web suite (Vitest/Playwright)** — `apps/web` does not exist yet and this feature does not touch it. Checked only that no Jest appears anywhere.

## What I ran that the implementer did not

- The **full** `quality.sh`, including the six Testcontainers integration suites the implementer explicitly skipped. All green — the skip turned out to be harmless, but it was an unverified claim until now.
- Three independent arming probes (A, B, C) on the token-forwarding and fan-out branches, none of which had been armed by anyone.
- Two scope-lifetime probes against the real `Cqrs.csproj` from a standalone consumer, which is how D1 was found. Neither the implementer's tests nor any existing suite exercises `Dispatcher` from inside a DI scope with a scoped handler dependency — which is exactly why the defect survived a 96.6%-covered, fully-armed test suite. Line coverage is not scope coverage.
- The `PublishAsync` static-type-inference probe that produced D3.

---

## What must change before re-review

1. **D1 (blocking).** Change `src/Cqrs/DispatcherServiceCollectionExtensions.cs:59` from `AddSingleton<IDispatcher, Dispatcher>()` to `AddScoped<IDispatcher, Dispatcher>()` (or inject `IServiceScopeFactory` and open a scope per dispatch, if a singleton is genuinely required — but justify it if so). **Add a test that fails on the current code**: register a scoped dependency, resolve `IDispatcher` from two separate `CreateScope()`s, dispatch once in each, and assert the handler saw two *different* instances. Then arm it — revert to `AddSingleton`, confirm that named test fails, record the verbatim message, restore with a forced rebuild — and put the row in the arming table in `progress/impl_cqrs_dispatcher.md`. A test that merely passes under the fix is not enough here; this defect's whole character is that it passes everything that does not specifically look for it.
2. **D2 (blocking, human gate — leader's action, not the implementer's).** Amend `CLAUDE.md:98` to name `src/Cqrs` as the third shared runtime project with its one-line reason, bring `CHECKPOINTS.md` C3's wording into line, and fix `AGENTS.md:30`'s `src/` description. Consider an architecture test asserting `src/Cqrs` carries exactly its one `PackageReference` and that no service `Domain/` namespace references `OrderToCash.Cqrs`.
3. **D3 (not blocking this feature, but do not let feature 14 find it).** Either add the runtime-typed `PublishAsync` overload now or record the constraint somewhere feature 14's implementer will be briefed with. My preference: record it in the brief for feature 14 and fix it there, where a real consumer can prove the fix.
4. **D4–D6** are advisory and may be deferred; if deferred, say so explicitly in `progress/impl_cqrs_dispatcher.md` rather than dropping them.

Everything else stands as submitted. The mechanism, the marker-interface justification, the asymmetry, the eager validation, the arming discipline and the report's honesty about "no #7 counterpart" are all good work — D1 is a single-line registration bug with a disproportionate blast radius, not a sign that the feature was built badly.

## On the benchmark record (deferred until this closes)

Not writing a `progress/history.md` entry, because the feature is rejected. When it does close, the effort field must read **"no counterpart"** rather than blank: #7 got its bus from `@nestjs/cqrs`, and MediatR v13's commercial licence is why #8 wrote one. Recording it as blank would let the Phase 24 table silently grow a comparison that does not exist.

On whether a fair comparison to #7's feature 16 can be drawn: **partly, and it should be stated as a bounded claim, not a number.** #7's feature 16 was *adopting and configuring* a bus, not writing one, so the two are not the same task and a straight ratio would be dishonest. What is comparable, and worth saying in one line: #7 inherited a bus plus the framework's assumptions about it, and #8 spent roughly 150 lines to get a dispatcher it fully controls — and this review is itself evidence of the trade in both directions, since D1 is a bug `@nestjs/cqrs` would never have had (its DI scoping was someone else's solved problem) while the marker interfaces and the boot-time validation are guarantees #7's bus does not provide at all. That two-sided sentence is the honest reading; a speed-up figure is not.

---

*Reviewed against `CLAUDE.md` as it stands on disk at review time, `feature_list.json` feature 43's four acceptance items, and `CHECKPOINTS.md` C1–C5 (C6 N/A, `sdd: false`; C7 not exercised beyond its honesty clause).*

---

# Re-review round 1

## Verdict: REJECTED

D1 is genuinely fixed, and so are D3, D5 and D6 — I verified all four independently from outside the test assembly rather than taking the arming table's word for it. The D2 amendment is honest. This round was largely very good work.

It is rejected on two new defects, both created *this round*, and one omission the D2 sweep should have caught:

- **D7 (blocking)** — D4's replacement guard **does not guard**. A real `MediatR` `PackageReference` leaves the suite green. I proved it with an actual MediatR package, not a mutated search string, and the test's own doc comment claims the opposite in as many words.
- **D8 (blocking)** — the D3 fix turned a compile-time-checked call into `MethodInfo.Invoke`, and **nothing tests that the `CancellationToken` still reaches an event handler**. I armed it: the token can be replaced with `CancellationToken.None` on that path and the suite stays green. CA2016 cannot see through reflection, so the D3 fix silently removed the analyzer guarantee that previously covered this and did not replace it with a test.
- **D9 (blocking, leader's)** — `.claude/agents/reviewer.md:33` still instructs every future reviewer that shared runtime code is limited to `SharedKernel` and `Contracts`. That is the "with teeth" one the sweep missed.

D7 is the reason this is a rejection rather than an advisory. The coordinator asked exactly the right question — "confirm it would fail if a MediatR reference appeared, not merely that it passes today" — and the answer is **no, it would not**.

---

## D2 — the amendment is honest. Sweep found one more with teeth.

**Checked the three claims against the tree, not against the diff's own description:**

- **`CLAUDE.md:98`** now reads "`src/SharedKernel` (zero `PackageReference`), `src/Contracts` (generated types) and `src/Cqrs` (the in-process dispatcher). Nothing else is shared." — matches the tree exactly: `src/` contains six services, `SharedKernel`, `Contracts`, `Seed` and `Cqrs`, and `Cqrs` is the only addition. ✅
- **`CLAUDE.md:100-102`'s reasoning is factually true**, not just plausible. It claims `SharedKernel` "may not have" a package because an architecture test asserts zero — `SharedKernelCsprojDeclaresZeroPackageReferences` and `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework` both exist and both pass. It claims `src/Cqrs` needs `Microsoft.Extensions.DependencyInjection.Abstractions` — `Cqrs.csproj:26` has exactly that one `PackageReference`, and nothing else. ✅
- **"An architecture test enforces that [no `Domain/` may reference `OrderToCash.Cqrs`], because nothing else would."** — `tests/Architecture.Tests/CqrsDomainPurityTests.cs`'s `DomainMustNotDependOnCqrs` exists, is scoped over `DomainAssemblies.All` (every service's `Domain/` union `SharedKernel`) rather than one service, and runs in the normal pass. The claim is not writing a cheque the tree does not cash. ✅
- **`CHECKPOINTS.md` C3** gained the two boxes, worded to match the rules that actually exist. ✅
- **`AGENTS.md:30`** updated. ✅

**On the fourteenth rule.** The coordinator's own arming — a real `ProjectReference` plus a real `IDispatcher` property in `Orders.Domain`, failing by name 1-of-14 — is the right standard, and it closes the gap the implementer disclosed. I confirm the residue is gone: `grep -rn "OrderToCash.Cqrs" src/Orders/` returns nothing and no service `.csproj` references `Cqrs`. I also confirm 14/14 green in my own full run.

Worth stating precisely, because it bears directly on D7: `DomainMustNotDependOnCqrs` reads compiled metadata, so it too is blind to a *present but unused* reference. For **this** rule that is correct — the prohibition is on the domain *depending on* the dispatcher, and an unused package reference is not a dependency. For D7's rule it is **not** correct, because acceptance item 4 forbids a *reference*, not a *usage*. Same technique, opposite verdicts, and the difference is what the rule is about.

### Sweep — what still asserts the two-project world

| Where | Text | Severity |
|---|---|---|
| **`.claude/agents/reviewer.md:33`** | "No shared runtime code beyond `src/SharedKernel` and `src/Contracts`." | **With teeth — D9.** This is a reviewer's standing instruction. The next reviewer to walk C3 on feature 15, when six services all reference `src/Cqrs`, reads this and rejects correct code. It is exactly the failure `AGENTS.md:46` describes — a guard firing on something no longer true — except worse than a stale injected cache, because this one is on disk and `grep`-ing it confirms the wrong answer. |
| `README.md:69` | "`OrderToCash.sln` the six services + SharedKernel + Contracts + Seed" | Cosmetic — same class as the `AGENTS.md:30` line, which was fixed. |
| `README.md:95` | "13 armed architecture rules" | Stale count, now 14. Wrap-up material, not blocking. |
| `docs/PROCESS.md:241` | "Thirteen architecture rules" | Same. Wrap-up material. |

`.claude/agents/implementer.md:65` was checked and is **fine** — it forbids adding a `PackageReference` to `SharedKernel`, which is still true and unaffected.

`specs/shared/` was checked and says nothing about project layout, so the read-only boundary is not implicated. No amendment needed there.

---

## Defect-by-defect: what I verified myself

I did **not** re-run the implementer's arming table. Instead I re-derived each fix's behaviour from outside the test assembly, with a standalone console app referencing the real `src/Cqrs/Cqrs.csproj` — the same instrument that found D1 last round, which matters because a test living inside `Cqrs.UnitTests` and the fixture it shares can both be wrong together.

### D1 — fixed. Not re-proven (established by the coordinator), but independently corroborated.

`DispatcherServiceCollectionExtensions.cs:86` is `services.AddScoped<IDispatcher, Dispatcher>()`. `DispatcherScopeTests` is the strong version: `CreateAsyncScope`, a real dispatch through two scopes, asserting two distinct `ScopedDependency` instances, built on a bare `BuildServiceProvider()` so it reproduces the **silent** Production mode rather than the noisy Development one. That is the right choice and it is the harder of the two to write.

Corroborated as a by-product of my own probe: `D1  two scopes -> distinct scoped instances: True`, from an external consumer, with `ValidateScopes` off.

### D3 — fixed, and fixed the right way. Verified against the exact call shape that was broken.

The resolution removed `PublishAsync<TEvent>` rather than adding an `object` overload beside it. **That reasoning is correct and I checked it rather than accepting it**: with both present, an argument whose static type is the base/interface would bind to the generic method by exact match in preference to an implicit reference conversion to `object`, so every risky call site would have kept silently picking the broken overload. Removing the generic method is what makes the mistake unavailable rather than merely avoidable. Good call.

My independent probe, run against the fixed project through the same code path feature 14's outbox drain will use:

```
D3  publish via IDomainEvent variable -> h1=1 h2=1  (both 1 => fixed, fan-out intact)
D3  publish via object variable       -> h1=2 h2=2  (both 2)
D3  unlistened fact -> no-op, no throw (asymmetry preserved)
```

Three things confirmed at once: the runtime-type resolution works through an interface-typed variable, the fan-out to *multiple* handlers survived the rewrite, and the command/query-versus-event asymmetry is intact — an unlistened fact published through the new reflective path is still a silent no-op, not a throw. That last one mattered: a reflective rewrite is exactly where a `MakeGenericType` on an unregistered type could have started throwing and quietly broken the asymmetry.

**My own arming, probe D** (the round-1 probe C target no longer exists, so this is new information, not a repeat): `s/GetServices(handlerServiceType))/GetServices(handlerServiceType).Take(1))/` → `armed -> suite FAILED (the guard fires)` / `restored -> suite green`. The fan-out is genuinely guarded on the new code.

The performance honesty in the report is fair and the `ConcurrentDictionary` `MethodInfo` cache is the right shape. Facts are per-outbox-row, not per-request; the trade is correctly judged and correctly disclosed.

### D5 — enforced, not merely documented.

`DispatcherServiceCollectionExtensions.cs:70-79` refuses a second call outright. Verified externally:

```
D5  second call -> InvalidOperationException: "AddDispatcher was already called on this IServiceCol..."
D5  IDispatcher descriptors registered: 1 (must be 1)
```

The second assertion is the one I added beyond the implementer's test: the refused call leaves exactly **one** `IDispatcher` descriptor, so the guard rejects before mutating the collection rather than after. The chosen semantics — exactly-once per `IServiceCollection`, with the error message naming the fix — are right; merging two partial scans would have needed cross-call state, which is the order-dependent hidden behaviour this feature exists to avoid.

### D6 — unrepresentable in practice, and it fires through the *public* path.

Verified, and better than the implementer's own test proves. Their test drives the internal `AddDispatcherFromTypes` seam. My probe declared `public sealed record AmbiguousCommand : ICommand, ICommand<int>` as an ordinary type in a scratch assembly and called the **public** `AddDispatcher(assembly)` — the real production entry point:

```
OrderToCash.Cqrs.DispatcherValidationException: AmbiguousCommand implements both OrderToCash.Cqrs.ICommand and OrderToCash.Cqrs.ICommand`1[System.Int32] — a command must implement exactly one of ICommand (no result) or ICommand<TResult> (a result), never both.
   at ...DispatcherServiceCollectionExtensions.AddDispatcher(...)
```

Thrown from registration, with a message that names the type and the fix. `ICommand`'s doc comment ("never both") is now enforced rather than aspirational. ✅

---

### D7 — BLOCKING, NEW. The "no MediatR" guard passes with MediatR genuinely referenced, and its comment says it cannot.

**Where:** `tests/Cqrs.UnitTests/NoMediatRReferenceTests.cs:39-42`, and the claim at `:15-18`.

**What I did.** Not a mutated search string — a real package. Added `<PackageVersion Include="MediatR" Version="14.0.0" />` to `Directory.Packages.props` and `<PackageReference Include="MediatR" />` to `Cqrs.UnitTests.csproj`, exactly the CPM-legal route the test's own comment says it defends against. Result:

```
Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19 - OrderToCash.Cqrs.UnitTests.dll
```

**Green, with MediatR referenced.** And the package is not notionally present — it physically ships:

```
=== Is MediatR.dll shipped into the test output?
MediatR.Contracts.dll
MediatR.dll
```

**Root cause, confirmed by introspecting the compiled assembly:**

```
OrderToCash.Cqrs.UnitTests.dll GetReferencedAssemblies():
   Microsoft.Extensions.DependencyInjection
   Microsoft.Extensions.DependencyInjection.Abstractions
   Microsoft.VisualStudio.TestPlatform.ObjectModel
   OrderToCash.Cqrs
   System.Collections / System.ComponentModel / System.Linq / System.Runtime
   xunit.assert / xunit.core
   -> any name containing 'mediatr': False
```

`Assembly.GetReferencedAssemblies()` reads the **assembly-reference table in the emitted metadata**, and Roslyn only writes an entry for an assembly whose types the code actually *uses*. A referenced-but-unused package produces no entry. So the guard is blind to the single most likely way a MediatR dependency arrives and lingers: somebody adds the package and it sits there.

**To be precise and fair, the guard is not wholly inert.** I re-armed with MediatR actually used (`typeof(MediatR.IMediator)`) and it fires: `Failed! - Failed: 1, Passed: 18, Total: 19`. So it guards *usage*, not *reference*.

**Why that distinction is the whole defect.** Acceptance item 4 is "no MediatR **reference** anywhere in the solution". The guard tests usage. The gap between them is the entire case the guard was added for. Contrast `DomainMustNotDependOnCqrs`, which uses the same metadata technique and is *correct*, because its rule genuinely is about dependency rather than presence.

**And the comment makes it worse, not better.** `NoMediatRReferenceTests.cs:15-18` states the test asserts against compiled metadata "rather than a text search, **so it cannot be defeated by any route a reference could arrive by (a direct `PackageReference`, a `GlobalPackageReference`, or a transitive dependency that itself pulls MediatR in)**". I defeated it with the first item on that list. A guard with honestly documented limits is fine; a guard that tells the next reader not to bother checking is `CLAUDE.md`'s own named anti-pattern — "a tick that stops anyone re-checking is the guard-that-does-not-guard pattern, which is the exact failure class this harness exists to catch."

**The repository already contains the correct template, and the test cites it while implementing half of it.** `SharedKernelHasNoPackagesTests` is a **pair**: `SharedKernelCsprojDeclaresZeroPackageReferences` (reads the project file — catches a present-but-unused reference) *and* `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework` (reads metadata — catches a transitive one). Both are needed, and that is exactly why there are two. `NoMediatRReferenceTests` cites the second by name at `:13-14` and omits the first.

**Why the implementer's arming did not catch this.** Substituting `"mediatr"` → `"microsoft"` proves the *reporting* path fires when offenders are found. It cannot prove the *detection* path finds a real MediatR reference, because it never introduces one. This is the same class of gap the coordinator identified and closed in the fourteenth architecture rule — arming the mechanism rather than the target — and here it was not closed. The `"zzz_never_matches_zzz"` non-probe is correctly reasoned about in the report, but it was the near-miss: it showed the mutation space was awkward, which was the signal to arm against a real target instead.

**What must change.** Add the missing half of the pair: a test reading `Cqrs.csproj` and `Cqrs.UnitTests.csproj` (or the generated `project.assets.json`, which also catches transitive packages that never get used) and asserting no package identity matches MediatR — mirroring `SharedKernelCsprojDeclaresZeroPackageReferences`. Then **arm it against a real MediatR `PackageReference`**, exactly as I did, and record that in the arming table. And correct the doc comment at `:15-18` so it describes what the guard does rather than what it wishes it did.

### D8 — BLOCKING, NEW. Nothing tests that the `CancellationToken` reaches an event handler, on a path that is now reflective.

**Where:** `src/Cqrs/Dispatcher.cs:92` — `handleAsyncMethod.Invoke(handler, [@event, cancellationToken])`.

**My arming, probe E:**

```
--- PROBE E: cancellation token still forwarded through the reflected Invoke
  armed    -> *** suite still green — GUARD DOES NOT GUARD ***
  restored -> suite green
```

`s/Invoke(handler, [@event, cancellationToken])/Invoke(handler, [@event, CancellationToken.None])/` — the token is silently dropped on every published fact and all 19 tests still pass. Confirmed by inventory: three `ForwardsTheCancellationToken` tests exist, for `SendAsync` void, `SendAsync` with result and `QueryAsync`. None for `PublishAsync`.

**Why this is a defect now and was tolerable before.** In round 1, `PublishAsync` was `await handler.HandleAsync(@event, cancellationToken)` — a direct, generically-typed call the compiler type-checked and CA2016 (an **error** in this repository) watched. The absence of a test was covered by the compiler. The D3 fix replaced it with an untyped `object[]` argument array passed through `MethodInfo.Invoke`, where argument order, count and value are checked by nothing at all, and CA2016 is structurally blind. **The fix removed a compile-time guarantee and did not replace it with a test.** An `object[]` is precisely the construct that silently takes the wrong element.

This is not the "fact emission must be guarded" rule — that rule is satisfied, and probe D proves the emission itself is guarded. It is `CLAUDE.md`'s async convention ("Forward every `CancellationToken`", CA2016 as an error) losing its enforcement mechanism on this one path without anyone noticing.

**What must change.** One test — `PublishAsync_ForwardsTheCancellationTokenToTheHandler`, in the shape of the three that already exist: give `ConcreteUpstreamFactHandler` (or a dedicated fixture) a `static CancellationToken? LastToken`, publish with a live `CancellationTokenSource.Token`, assert equality. Then arm it with the mutation above and record that it fires.

### D9 — BLOCKING, leader's. `.claude/agents/reviewer.md:33` still carries the pre-amendment rule.

**Where:** `.claude/agents/reviewer.md:32-34` — "**Architecture.** No cross-service DB access. No shared runtime code beyond `src/SharedKernel` and `src/Contracts`."

**Why it matters, and why it is not cosmetic.** This is not documentation, it is a **standing instruction to every future reviewer**. From feature 15 onward all six services reference `src/Cqrs`. A reviewer session that follows its own agent definition — which is what it is supposed to do — will find shared runtime code beyond the two named projects and reject correct, human-gated work. The amendment updated `CLAUDE.md`, `CHECKPOINTS.md` and `AGENTS.md` but not the file that tells the reviewer what to enforce, which is the one place the rule is actually *executed*.

`README.md:69` should be fixed in the same pass (cosmetic, same class as the `AGENTS.md:30` line already corrected). `README.md:95` and `docs/PROCESS.md:241` still say thirteen architecture rules where there are now fourteen — wrap-up material, not blocking, but note them so the count is corrected once rather than drifting.

---

## Regression checks

| Check | Result |
|---|---|
| `./quality.sh` — format + build + **full solution** test + coverage | **exit 0**, all green, wall-clock **`2:11.04`** |
| `tests/Architecture.Tests` | `Failed: 0, Passed: 14, Total: 14` — the thirteen prior rules **plus** `DomainMustNotDependOnCqrs`, all green |
| `tests/Cqrs.UnitTests` | `Failed: 0, Passed: 19, Total: 19` |
| Prior unit suites | SharedKernel 32, Contracts 21, Orders 24, Seed 34 — all green, no regression |
| Integration suites (real Testcontainers) | Notifications 7, Fulfillment 19, Orders 13, Billing 23, Seed 5 — all green |
| `./init.sh` | exit 0 |
| `dotnet format --verify-no-changes` | clean (step 1 of `quality.sh`) |
| Build warnings | 0, under `TreatWarningsAsErrors` |
| Services untouched | `git diff --stat -- src/ tests/ apps/` empty; no service `.csproj` references `Cqrs`; `grep -rn "OrderToCash.Cqrs" src/Orders/` returns nothing — the coordinator's arming residue is fully gone |
| Probe residue | `src/Cqrs/Dispatcher.cs` clean after probes D and E; no MediatR left in `Directory.Packages.props`, `Cqrs.UnitTests.csproj`, `DispatcherTests.cs` or the build output; `git status --porcelain` identical to pre-probe |

## My arming table, round 2

| # | Branch under test | Mutation | Result |
|---|---|---|---|
| D | Rewritten `PublishAsync` still fans out to **every** handler (`Dispatcher.cs:90`) | `s/GetServices(handlerServiceType))/GetServices(handlerServiceType).Take(1))/` | `armed -> suite FAILED (the guard fires)` / `restored -> suite green` |
| E | `CancellationToken` reaches an event handler through the reflected `Invoke` (`Dispatcher.cs:92`) | `s/Invoke(handler, [@event, cancellationToken])/Invoke(handler, [@event, CancellationToken.None])/` | **`armed -> *** suite still green — GUARD DOES NOT GUARD ***`** → **D8** |
| F | "No MediatR reference" against a **real** MediatR package (not a mutated search string) | `PackageVersion` + `PackageReference` for `MediatR` 14.0.0, unused | **suite green, `MediatR.dll` shipped to output** → **D7** |
| F′ | Same, with a MediatR type actually used | `+ typeof(MediatR.IMediator)` | `Failed: 1, Passed: 18` — fires, so the guard covers *usage* only |

Probes D–F are all new; none repeats round 1's A–C, whose target code was rewritten or whose claims the coordinator established.

## What I did NOT re-run, and why

- **D1's arming** (`AddScoped` → `AddSingleton`) — established by the coordinator, who ran it. Re-running is duplicated cost. I corroborated the *behaviour* from an external consumer instead, which is different information.
- **The fourteenth rule's arming against its real target** — the coordinator did this himself with a real `ProjectReference` and a real domain property, and reported it failing by name 1-of-14. I verified the residue is gone and the rule is green in my own run.
- **The implementer's five arming rows for D1/D3/D4/D5/D6** — I ran my own probes on the same code instead. That is how D7 and D8 surfaced: repeating someone's probe confirms their probe, not their code.
- **Round 1's probes A–C** — A and B (token forwarding on `SendAsync`/`QueryAsync`) target unchanged code and fired last round; C's target no longer exists and was replaced by probe D.

---

## What must change before re-review

1. **D7 (blocking).** Add the missing half of the `SharedKernelHasNoPackagesTests` pair — a project-file (or `project.assets.json`) check that catches a present-but-unused MediatR package — and **arm it against a real `PackageReference`**, not a mutated search string. Correct the false claim at `NoMediatRReferenceTests.cs:15-18`. Keep the existing metadata test; it is the transitive-usage half and is worth having.
2. **D8 (blocking).** Add `PublishAsync_ForwardsTheCancellationTokenToTheHandler` and arm it with the probe-E mutation. Record the verbatim failure.
3. **D9 (blocking, leader's).** Update `.claude/agents/reviewer.md:33` to the amended shared-runtime-code list. Fix `README.md:69` in the same pass; correct the "thirteen architecture rules" counts in `README.md:95` and `docs/PROCESS.md:241` at wrap-up.
4. **Cosmetic, non-blocking.** `DispatcherServiceCollectionExtensions.cs:164-180` has two consecutive `<summary>` blocks on `ExpectedCommandHandlerServiceTypes`; the second should be `<param name="declarationErrors">`. It builds clean, but it is malformed doc XML.

Not required, but worth considering when feature 15 lands: `AddDispatcher`'s exactly-once contract (D5) is enforced per `IServiceCollection`, which is right — just make sure the first consumer passes every relevant assembly to the single call, since the failure mode is now a loud `InvalidOperationException` at boot rather than a confusing validation error.

## Why this is a rejection and not an approval with advisories

Two of the three blockers are guards added *this round* to close review findings, shipped with arming tables asserting they work. One of them does not fire against the case it was written for, and says in a comment that it cannot be defeated that way. The other silently replaced a compiler-enforced guarantee with an unchecked `object[]` and no test.

If a reviewer waves those through, the arming table becomes decoration — and the arming table is the single mechanism this repository uses to distinguish a guard from a comment. The code is good; the guards over it are not yet honest, and that is a smaller fix than it is a smaller problem.

The benchmark accounting question from the first brief is deliberately **not** answered here — it belongs with the approval, and answering it now would bake in a conclusion drawn from an incomplete arc. Two of its inputs (D7, D8) are still open, and both are evidence for the same side of it.

---

# Re-review round 2 (third pass)

## Verdict: APPROVED

D7 and D8 are genuinely fixed and I proved both myself rather than re-running the implementer's table. D9 is closed, and my own independent sweep for the superseded rule now returns nothing outside the historical `progress/` files. All four acceptance items hold, the fourteen architecture rules are green, no service is touched, `./quality.sh` exits 0 in **2:02.24** and `./init.sh` exits 0.

One new finding, **D10**, is recorded below and is **not** blocking. I came close to rejecting on it and the reasoning for not doing so is written out, because a bar that moves without explanation is worse than a bar in the wrong place.

---

## 1. D7's new check — closed for the route that matters, with one hole

**The coordinator's test, run exactly as specified.** `dotnet add src/Orders/Orders.csproj package MediatR --version 14.0.0` — a third project, not one of the two armed at the gate:

```
info : PackageReference for package 'MediatR' added to '.../src/Orders/Orders.csproj'
        and PackageVersion added to central package management file '.../Directory.Packages.props'.
--- guard result:
  NoMediatRPackageReferenceTests.ProjectFileDeclaresNoMediatRPackageReferenceOrVersion(relativePath: "Directory.Packages.props") [FAIL]
Failed!  - Failed: 1, Passed: 22, Skipped: 0, Total: 23
```

**It fires.** Not because `Orders.csproj` is inspected — it is not — but because `ManagePackageVersionsCentrally` is `true` solution-wide, so `dotnet add package` is *forced* to route the version through `Directory.Packages.props`, which **is** inspected. Central package management is a genuine chokepoint, and guarding the chokepoint covers all 21 project files for the realistic route. That is the right instinct and it works.

**D10 — the hole.** The chokepoint is not airtight. `VersionOverride` is CPM-legal and needs no central entry:

```xml
<!-- src/Orders/Orders.csproj — no PackageVersion in Directory.Packages.props -->
<PackageReference Include="MediatR" VersionOverride="14.0.0" />
```

```
--- does it restore/build?
  Restored .../src/Orders/Orders.csproj (in 484 ms).
--- guard result:
Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23
```

A MediatR reference that restores cleanly, in a real service project, with the suite green. The guard reads three hardcoded `[InlineData]` paths out of 21 project files, so anything that reaches a version without touching `Directory.Packages.props` is invisible to it.

I checked the other bypass and it is **closed**: a project setting `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` locally and pinning an inline version fails to restore (`NU1015` — its *other* packages then have no version), so that route cannot ship.

**Why this is not a rejection.** Four reasons, and the first is the decisive one:

1. **Acceptance item 4 requires a state, not a guard.** Its wording is "no MediatR reference anywhere in the solution", and unlike item 3 it does **not** say "proven by a test". That state is true — verified by my own grep and by both halves of the pair. The guard exists because I raised D4 as an *advisory*; it is defence-in-depth over an item that was already satisfied.
2. **The guard now catches what actually happens.** `dotnet add package` is how a dependency arrives. `VersionOverride` hand-written into a csproj, with no central entry, is not a plausible accident — it is a deliberate act.
3. **The overclaim is gone.** Round 2's defect was as much the comment as the code: a guard that told the next reader it "cannot be defeated by any route". The corrected doc comment on `NoMediatRReferenceTests` is now precise about being the transitive-or-used half and names the unused-reference gap it was proven to have, citing the arming table. `NoMediatRPackageReferenceTests` makes no completeness claim it cannot support.
4. **The right home is already named and already deferred on an explicit boundary.** The implementer has twice recorded that the solution-wide equivalent belongs in `tests/Architecture.Tests`, and twice declined to build it there because the coordinator's touch list was `src/Cqrs/**` and `tests/Cqrs.UnitTests/**`. That is a scoped implementer behaving correctly, not evading.

**What D10 needs, so it cannot be silently dropped.** When the solution-wide check lands in `tests/Architecture.Tests`, it should enumerate project files by **globbing** `src/**/*.csproj` and `tests/**/*.csproj` rather than listing them, and match `VersionOverride` as well as `Include`. A glob is what makes it closed; a list is what made this one a whitelist. Carry it into the feature-15 brief or open it as its own item — it must not live only in this paragraph.

**On the shape of the fix, credit where due.** Pairing a project-file check with the metadata check is exactly right, and the implementer identified the correct precedent unprompted: `SharedKernelHasNoPackagesTests` is a two-test pair for precisely this reason. Adding `Directory.Packages.props` as a third case — which the coordinator notes was his own catch — is what turns three files into coverage of twenty-one for the CPM route.

## 2. D8's test asserts the token *arrives*, not that one was passed

`DispatcherTests.cs:161` — `Assert.Equal(cts.Token, ConcreteUpstreamFactHandler.LastToken)`, against a live `CancellationTokenSource.Token`, not a null-check and not `Assert.NotEqual(default, …)`. `CancellationToken` is a struct whose equality is tied to its source, so `cts.Token.Equals(CancellationToken.None)` is `false` and the coordinator's arming (replacing the forwarded token with `CancellationToken.None`) is the mutation this assertion is built to catch. The fixture stores the token in a dedicated `static CancellationToken? LastToken` with a comment at `WellFormedFixtures.cs:102-108` explaining that CA2016 cannot see through the `Invoke` — the reason the test has to exist at all.

Same shape as the three pre-existing forwarding tests, so the four now cover all four dispatch paths. The analyzer guarantee the D3 rewrite destroyed is replaced by a test, which was the requirement.

## 3. D3 still sound after D8's change

Re-ran my external probe against the current tree — a standalone consumer of the real `src/Cqrs/Cqrs.csproj`, not a test inside the assembly under test:

```
D3  publish via IDomainEvent variable -> h1=1 h2=1  (both 1 => fixed, fan-out intact)
D3  publish via object variable       -> h1=2 h2=2  (both 2)
D3  unlistened fact -> no-op, no throw (asymmetry preserved)
```

The base/interface-typed publish still reaches the handler for the runtime type, the fan-out to multiple handlers is intact, and the command/query-versus-event asymmetry survives — an unlistened fact through the reflective path is still a silent no-op rather than a `MakeGenericType` throw.

## 4. D5 and D6 held through two further passes

From the same external probe, through the **public** `AddDispatcher(assembly)` entry point:

```
D5  second call -> InvalidOperationException: "AddDispatcher was already called on this IServiceCol..."
D5  IDispatcher descriptors registered: 1 (must be 1)
D1  two scopes -> distinct scoped instances: True
```

and D6, from a scratch assembly declaring `public sealed record AmbiguousCommand : ICommand, ICommand<int>` as an ordinary type:

```
DispatcherValidationException: AmbiguousCommand implements both OrderToCash.Cqrs.ICommand and
OrderToCash.Cqrs.ICommand`1[System.Int32] — a command must implement exactly one of ICommand
(no result) or ICommand<TResult> (a result), never both.
   at ...DispatcherServiceCollectionExtensions.AddDispatcher(...)
```

D5's second assertion is mine, beyond the implementer's test: the refused call leaves exactly **one** `IDispatcher` descriptor, so the guard rejects before mutating the collection rather than after. D1 is corroborated as a by-product, from outside, with `ValidateScopes` off.

## 5. Regressions — none

| Check | Result |
|---|---|
| `./quality.sh` — format + build + **full solution** test + coverage | **exit 0**, wall-clock **`2:02.24`** |
| `tests/Architecture.Tests` | `Failed: 0, Passed: 14, Total: 14` |
| `tests/Cqrs.UnitTests` | `Failed: 0, Passed: 23, Total: 23` (19 → 23: +3 `NoMediatRPackageReferenceTests` cases, +1 `PublishAsync_ForwardsTheCancellationTokenToTheHandler`) |
| Prior unit suites | SharedKernel 32, Contracts 21, Orders 24, Seed 34 — green |
| Integration suites (real Testcontainers) | Notifications 7, Fulfillment 19, Orders 13, Billing 23, Seed 5 — green |
| Solution total | **215 passed, 0 failed** across eleven projects |
| `./init.sh` | exit 0 |
| `dotnet format --verify-no-changes` | clean |
| Build warnings | 0 under `TreatWarningsAsErrors` |
| Dispatcher wired into a service? | **No.** No service `.csproj` references `Cqrs`; `grep -rn "OrderToCash.Cqrs" src/Orders/` empty; `git diff --stat -- src/ tests/ apps/` empty. Feature 15 is still the first consumer. |
| My probe residue | `src/Orders/Orders.csproj` byte-restored (`git diff --stat` clean); `Directory.Packages.props` shows only round 1's two legitimate DI `PackageVersion` entries; no MediatR in any file or build output; `dotnet restore` re-run to clear the probe's assets file |

## D9 — closed, and the sweep confirms it

`.claude/agents/reviewer.md:32-34` now reads "`src/SharedKernel`, `src/Contracts` and `src/Cqrs` (the third ratified at the Phase 8 human gate — **check `CLAUDE.md` on disk, this list changes**)". The parenthetical is the better half of the fix: it does not just correct the list, it tells the next reader the list is not authoritative here. `README.md:69` and both "thirteen rules" counts are corrected.

My independent sweep for the superseded text returns matches only in historical `progress/review_*.md` files, which is correct — those record the state at the time they were written and must not be rewritten.

---

## D10 — recorded, non-blocking

**`tests/Cqrs.UnitTests/NoMediatRPackageReferenceTests.cs:36-39`.** The `[Theory]` inspects three hardcoded paths of the solution's twenty-one project files. It is closed for the CPM route (`dotnet add package` must write to `Directory.Packages.props`, which is inspected — proven), and open for `VersionOverride`, which is CPM-legal, restores cleanly and needs no central entry — proven, in `src/Orders/Orders.csproj`, suite green at 23/23. Fix when the solution-wide equivalent lands in `tests/Architecture.Tests`: glob the project files rather than listing them, and match `VersionOverride` alongside `Include`.

---

## On the `docs/PROCESS.md` §11.2 framing — asked for, and it is right, with one thing missing

Both conclusions are correct and the second is the more valuable: *agent definitions are rule-bearing files*, and *an amendment is finished when nothing anywhere still asserts the old rule*. The diagnosis is also right that this was worse than a stale cache — the failure mode was a reviewer performing the checking ritual correctly and being confirmed in the wrong answer, which is the guard-that-does-not-guard with the verification step intact.

**What is missing is that the sweep, as written, is a habit rather than a guard** — and this feature spent three rounds establishing that the difference between those two is the whole game. The stale `CLAUDE.md:98` was caught by a reviewer's grep; the stale `reviewer.md:33` was caught by a reviewer's grep one round later, *after* the first finding had already put everyone on notice. A discipline that fails twice in two rounds under maximum attention is not going to hold at phase 20 under normal attention.

The concrete form: when a non-negotiable is amended, the amendment commit should leave behind a mechanical check that the superseded phrasing appears nowhere outside `progress/` — an `init.sh` clause or a test, seeded with the exact old text. It costs one line per amendment and it converts "remember to sweep" into something that fails. I would add that as a third bullet. It is also the same lesson as D7, one level up: the check must read the whole surface, not the files someone remembered.

---

## CHECKPOINTS.md — final walk

### C1 — harness complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist.
- [x] `progress/current.md`, `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (+ suite_runner).
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — re-run by me.

### C2 — state coherent
- [x] At most one feature `in_progress` — feature 43 moves to `done` with this approval; none left `in_progress`.
- [x] Every status in `rules.valid_status`.
- [x] Every `done` feature has passing tests — solution suite 215/215 green.
- [x] `progress/current.md` describes the active session.
- [x] No `blocked` features.

### C3 — architecture respected
- [x] No infrastructure reference inside any `Domain/` folder — **verified by running the NetArchTest suite**: 14/14.
- [x] No cross-service database access — this feature touches no database.
- [x] **No shared runtime code beyond `src/SharedKernel`, `src/Contracts` and `src/Cqrs`** — now true against the amended rule, and the amendment is honest: I checked `CLAUDE.md:98-102`'s new text against the tree, not against its own description. (This box was `[ ]` in round 1 and is the one D2 existed to close.)
- [x] **No `Domain/` namespace references `OrderToCash.Cqrs`** — the new C3 box, enforced by `DomainMustNotDependOnCqrs`, armed against its real target at the gate, green here, and `grep -rn "OrderToCash.Cqrs" src/Orders/` is empty.
- [x] `src/SharedKernel` still has zero `PackageReference` entries — both guard tests green; the dispatcher was deliberately kept out of it for this reason.
- [x] No `decimal` in domain arithmetic — `DomainDecimalTests` green; `src/Cqrs` contains no arithmetic.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — this feature adds no inter-service interaction; `Dispatcher.cs:7-12` records that durability stays with `outbox` / `saga_commands` and the in-process hop is only the fast path.
- [x] No stray debug logging, no context-free TODOs.

### C4 — verification real
- [x] `./quality.sh` passes — re-run in full by me, exit 0, `2:02.24`.
- [x] Domain tests pure — `Cqrs.UnitTests` uses xUnit + `Microsoft.Extensions.DependencyInjection` only; no DB, no broker, no infrastructure mock.
- [x] Integration tests use real Testcontainers — unchanged by this feature; all six suites ran green.
- [x] Coverage collected (gate itself is feature 34, and `quality.sh:6-9` is explicit that it reports rather than enforces — no inert-gate claim made).
- [x] No Jest anywhere.

### C5 — session closed cleanly
- [x] No suspicious untracked files — the untracked set is exactly this feature's source, tests and reports; no `*.tmp`, no build output outside `.gitignore`; all four of my probe targets byte-restored.
- [x] `progress/history.md` has an entry for the feature, **including its effort record** — appended with this approval, `#7 baseline: no counterpart`.
- [x] `feature_list.json` reflects true state — feature 43 set `done`.
- [x] The human will be told what was done and how to test it.
- [x] Claude did not commit.

### C6 — SDD
**N/A** — `sdd: false`, correctly. No `R<n>` covers this feature (new in #8 per its `note`), no `specs/cqrs_dispatcher/` required, and no `specs/shared/test-matrix.md` row was added — adding one would have been a false parity claim.

### C7 — spec-reuse fidelity
Not exercised (no `specs/shared/` artifact, n8n workflow or API script touched) except the honesty clause, which is satisfied: the feature is recorded as having **no #7 counterpart** in three places — `feature_list.json`'s `note`, the implementation report's opening, and the history entry's baseline field — so the Phase 24 table cannot silently gain a comparison that does not exist.

---

## What I did NOT re-run, and why

- **D7's and D8's arming** — both established by the coordinator, both with a real package / a real token mutation. I ran my own probes on the same code instead: the `src/Orders/Orders.csproj` route the coordinator asked about, plus the two CPM-bypass routes nobody had tried. That is how D10 surfaced. Repeating someone's probe confirms their probe, not their code.
- **Round 1's probes A–C and round 2's D–F** — A/B target unchanged code and fired previously; C's and F's targets were rewritten and were re-probed in the round that rewrote them.
- **The six Testcontainers integration suites individually** — `quality.sh` ran all of them in the full-solution pass, which is the claim under test.

## The arc, for the record

| Round | Verdict | Found | By what |
|---|---|---|---|
| 1 | REJECTED | **D1** singleton dispatcher captures the root provider — silent captive dependency in Production; D2–D6 | A standalone consumer of the real project, run in two host configurations |
| 2 | REJECTED | **D7** the "no MediatR" guard passes with MediatR referenced; **D8** the D3 fix destroyed CA2016's coverage and added no test; **D9** `reviewer.md` still carried the superseded rule | A real MediatR package install; `arm-probe.sh` on the rewritten publish path; a sweep of rule-bearing files |
| 3 | **APPROVED** | **D10** the new guard is a three-file whitelist, open to `VersionOverride` — non-blocking, home named | The coordinator's own `Orders.csproj` question, plus the two bypass routes |

Every one of D1, D7 and D8 passed a green suite with a complete arming table beside it. That is the single most transferable fact this feature produced.
