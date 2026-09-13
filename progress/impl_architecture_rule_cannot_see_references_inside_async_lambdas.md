# impl: architecture_rule_cannot_see_references_inside_async_lambdas (backlog id 83)

## Status header

- **Where we are: fix round 2 complete, see "Fix round 2" at the end of
  this file for the current state.** Round 1's REJECTED verdict is
  superseded there; do not read §1 (below) as current without it — §1 was
  edited in place in round 2 to carry the corrected population (11 files,
  12 sites, 4 services), and "Fix round 2" explains why and records what
  round 1's `grep -c "async ct =>"` sweep missed. `Architecture.Tests` is
  still **36/36 green** after round 2 (A1/A2/A3 folded into the SAME
  existing assertions/test, no new `[Fact]`).
- **Needs approval:** the reviewer's round-2 pass.
- **Recommendation:** accept. D1 is now record-only and closed (population
  corrected in §1, closing count of zero unchanged and re-justified); A1 is
  fixed and armed (not filed) per the leader's ruling; A2, A3, A4 and A5 are
  folded in. See "Fix round 2" for the full account.

### Round-1 status (superseded, kept for history)

- **Where we were:** feature complete, `Architecture.Tests` 36/36 green
  (35 baseline + 1 new population-guard fact). The new closure-aware half
  of `ApplicationMustNotDependOnInfrastructure` is armed with the exact
  missed shape (a real Infrastructure construction inside
  `unitOfWork.ExecuteAsync(async ct => …)`, capturing the command
  parameter) in **two services** simultaneously, and the failure names
  both offending types verbatim. `./quality.sh` was re-run in full after
  the fix landed (log referenced in §6) to reconcile against the stated
  1907/1906 baseline.
- **Needs approval:** the reviewer's normal round. One thing beyond the
  brief's literal ask is called out below because it changes the fix's
  own premise mid-flight: the dispatch's framing ("async lambda
  specifically") is not what my own probe found — see §2.
- **Recommendation:** accept. The population (§1) is enumerated fresh
  with `bin`/`obj` excluded by path; the boundary (§2) is re-derived by
  probe, not assumed from the dispatch or from id 76's review; the fix
  (§3) is a Roslyn semantic-model scan folded into the SAME test method
  the acceptance bullets name, with a measured cost (§4, +1 test,
  ≈4s → ≈7s); the defeat list (§5) is run row by row against the new
  instrument, including two rows the fix's own first draft failed; and
  §6 states the real violation count in the newly-visible population
  (zero) as a reading, not an assurance.

---

## 1. Population — a search result, `bin`/`obj` excluded by path

**Corrected in fix round 2 — see "Fix round 2" below for why.** The
command and table in this section were re-run against a predicate
matching bullet 1's claim (`async ct =>` **and any sibling shape**)
rather than the dominant literal. The corrected figures are **11 files,
12 sites, 4 of the 6 services**; the section below is the corrected
version, kept in place (not appended) so a reader of §1 sees the true
population rather than the superseded one. Round 1's original
`grep -c "async ct =>"` command and its 10-file/11-site table are
preserved verbatim in "Fix round 2" §D1 below, for the record of what was
wrong and why.

Command:

```
find src -path '*/Application/*' -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -nE "async \(?[a-zA-Z_][a-zA-Z0-9_]*(, *[a-zA-Z_][a-zA-Z0-9_]*)*\)? *=>" \
  | sort
```

Output:

```
src/Billing/Application/CreditHoldService.cs:26:            async ct =>
src/Billing/Application/CreditReleaseService.cs:22:            async ct =>
src/Billing/Application/InvoiceIssueService.cs:44:            async ct =>
src/Billing/Application/PaymentRegisterService.cs:80:            async ct =>
src/Fulfillment/Application/DespatchCreationService.cs:53:            async ct =>
src/Fulfillment/Application/StockReplenishService.cs:16:            async ct =>
src/Fulfillment/Application/StockReservationService.cs:27:            async ct =>
src/Fulfillment/Application/StockReservationService.cs:91:            async ct =>
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:133:            async ct =>
src/Orders/Application/Commands/PlaceOrderCommandHandler.cs:98:                async ct =>
src/Orders/Application/Sagas/SagaFactHandler.cs:59:            async ct =>
src/Projector/Application/ProjectionApplyService.cs:27:            async (document, ct) =>
```

**11 files, 12 sites, across 4 of the 6 services** (`StockReservationService.cs`
has two: `ReserveAsync` and `ReleaseAsync`). Classified one line per hit,
by reading the async-lambda BODY of each site (not the whole file) for any
construction of, or call into, that service's own `Infrastructure`
namespace:

| File | Site(s) | Infrastructure reference inside the lambda? |
|---|---|---|
| `Billing/Application/CreditHoldService.cs` | `HoldAsync` | none |
| `Billing/Application/CreditReleaseService.cs` | `ReleaseAsync` | none |
| `Billing/Application/InvoiceIssueService.cs` | `IssueAsync` | none |
| `Billing/Application/PaymentRegisterService.cs` | `RegisterAsync` | none |
| `Fulfillment/Application/DespatchCreationService.cs` | `CreateAsync` | none |
| `Fulfillment/Application/StockReplenishService.cs` | `ReplenishAsync` | none |
| `Fulfillment/Application/StockReservationService.cs` | `ReserveAsync`, `ReleaseAsync` | none (both) |
| `Orders/Application/Commands/CancelOrderCommandHandler.cs` | (saga command handling) | none |
| `Orders/Application/Commands/PlaceOrderCommandHandler.cs` | (saga command handling) | none |
| `Orders/Application/Sagas/SagaFactHandler.cs` | (fact handling) | none |
| `Projector/Application/ProjectionApplyService.cs` | `ApplyAsync`'s post-apply callback (`async (document, ct) =>`) | none — calls `signalPublisher.PublishAsync` (a port) and `logger.LogError`; no `OrderToCash.Projector.Infrastructure` construction or call anywhere in the lambda body |

Confirmed independently by grepping each of the 11 files whole for the
literal text `Infrastructure` — every one returns **zero** hits, not just
zero hits inside the lambda:

```
$ for f in <the 11 files>; do grep -n "Infrastructure" "$f" || echo "(no Infrastructure mention at all)"; done
=== all eleven: (no Infrastructure mention at all) ===
```

This is the population the guard could not see into before this feature,
and its size (**11 files, 12 sites, 4 of the 6 services**) is the entry's
own justification. It is also the answer to §6's closing count, read
directly: **zero real violations exist in this population today** — the
count is unchanged by the correction, because the twelfth site (Projector)
carries no Infrastructure reference and review round 1's S3 probe already
proved the guard reaches it (a real Infrastructure construction placed in
that same lambda was named by the guard). This is a record correction, not
a coverage hole: nothing under `src/` or `tests/` changed for D1.

## 2. Re-establishing the boundary by probe — not the dispatch's framing

The dispatch's own text is explicit that the entry originally named a
cause ("walk Cecil's nested types") that id 76's review round 1 measured
false, and that the review's OWN replacement framing ("async lambda
specifically") is the thing to re-verify, not inherit. I re-derived it
independently with Mono.Cecil directly — the library `NetArchTest.Rules`
1.3.2 wraps — against a minimal reproduction, before writing any fix.

**Setup.** A throwaway console project (`/tmp/.../probe83/ProbeTarget`)
declaring an `Infrastructure` namespace with an `RpcErrorPayload` record
and an `Application` namespace with several shapes of reference to it,
each isolated in its own class so one shape's result cannot mask
another's:

| Class | Shape | Cecil-visible dependency? |
|---|---|---|
| `OnlySyncLambda` | non-capturing/`this`-capturing sync lambda in `unitOfWork.ExecuteAsync(...)` | **yes** — compiles directly onto the outer type (no closure needed) |
| `OnlyAsyncMethod` | plain `async Task Do()` method, reference in its body | **yes** — state machine nested ONE level below the outer type |
| `OnlyAsyncLambda` | async lambda capturing ONLY an instance/primary-ctor field (`this`) | **yes** — state machine nested ONE level below the outer type (no display class needed; `this` is already on the state machine) |
| `OnlyAsyncLambdaNoCapture` | async lambda capturing NOTHING | **no** — cached in the compiler's `<>c` type (depth one), state machine nested INSIDE `<>c` (depth two) |
| `CapturesLocalParam` | async lambda capturing a METHOD PARAMETER (the real repository's dominant shape: `unitOfWork.ExecuteAsync(async ct => { … reference … })` always closes over `command`) | **no** — a `<>c__DisplayClassN_M` at depth one, state machine nested inside IT at depth two |

Read directly off the compiled IL with a small Cecil walker
(`ModuleDefinition.ReadModule` → recursive `TypeDefinition.NestedTypes`
dump): every depth-one nested type (a sync lambda's closure method living
directly on the outer type, a `this`-only async lambda's state machine, a
plain async method's state machine) has `Namespace == ""` but is still
INSIDE `NetArchTest`'s dependency walk for the population-matched outer
type. Every depth-TWO nested type (the state machine living inside a
`<>c__DisplayClassN_M` or inside the compiler's shared `<>c`) is outside
it. Confirmed with `NetArchTest.Rules.Types.InAssembly(...).That()
.ResideInNamespaceMatching(...).ShouldNot().HaveDependencyOnAny(...)`
itself, run against the compiled probe assembly (not just theorised from
the IL dump): `OnlyAsyncLambda`, `OnlySyncLambda` and `OnlyAsyncMethod`
were reported FAILING (caught); `OnlyAsyncLambdaNoCapture` and
`CapturesLocalParam` were NOT in the failing list (missed).

**The real boundary is NESTING DEPTH, not "async" as such**, and the
review's own framing ("a reference inside an async lambda is invisible")
is a correct DESCRIPTION of every case that mattered in this repository
(because every real `unitOfWork.ExecuteAsync(async ct => …)` site closes
over the command parameter, which forces a display class and therefore
depth two) but not the CAUSE — an async lambda that captures nothing but
`this` is caught, just like a sync one, and a NON-capturing async lambda
is ALSO missed for a completely different reason (the `<>c` cache class,
not a display class) at the same measured depth. Both halves of this are
recorded because the ledger rule (`CLAUDE.md`) treats a confidently wrong
mechanism as worse than an absent one, and the review's own framing —
while much closer than the entry's original — was still a description of
symptoms rather than the underlying rule NetArchTest's population filter
actually applies (`ResideInNamespaceMatching` never matches a type whose
own `Namespace` is empty, which is every compiler-generated nested type at
ANY depth; NetArchTest's dependency WALK nonetheless reaches depth one but
not depth two of a population-matched type's own nested types).

## 3. The fix — a Roslyn semantic-model scan, folded into the same test

Chosen over the dispatch's other listed options (Cecil nested-type
walking, `<>c__DisplayClass`/state-machine name filtering) because both of
those close the CURRENT depth-two gap and open a new depth-N gap the
moment a defeat nests one level deeper (a lambda inside a local function
inside a lambda, for instance) — the class this repository has already
paid for once, in id 68's three text-scanner rounds, is exactly "patch the
observed depth, get defeated at the next one". Roslyn's `SemanticModel`
retires the whole class structurally: a lambda body is not a separate type
at the SYNTAX level, whatever the compiler later lowers it to, so a symbol
reference inside one is exactly as visible to
`SemanticModel.GetSymbolInfo` as a reference in the outer method's own
body, at ANY nesting depth — there is no "next depth" to be defeated by.
`Microsoft.CodeAnalysis.CSharp` is already a `PackageReference` in
`tests/Architecture.Tests/Architecture.Tests.csproj` (added by id 68); no
new package.

**Where it lives.** Folded into the SAME `[Fact]
ApplicationMustNotDependOnInfrastructure` the acceptance bullets name
(bullet 4 says the arming must make THIS test fail, not a new one). The
existing Cecil/NetArchTest check runs first; a new
`FindClosureConfinedInfrastructureReferences()` runs second; both
contribute to one combined `offendingTypeNames` list and one `Assert.True`.

**How it works** (`tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`,
`FindOffendingApplicationTypes`): for each of the six services, every
`.cs` file under its `src/<Service>/` tree (all layers — a full
compilation needs Domain/Infrastructure/Presentation source too, for
symbol resolution to succeed; only types DECLARED in an
Application-namespace are ever reported) is parsed fresh with
`CSharpSyntaxTree.ParseText` and compiled into one `CSharpCompilation`
per service. For every `BaseTypeDeclarationSyntax` whose declared symbol's
namespace matches `ApplicationNamespacePattern`, every `SimpleNameSyntax`
descendant (covers plain identifiers, generic names, the right-hand side
of a qualified/member-access chain — i.e. every syntactic position a type,
method, constructor or namespace segment can be named from) is resolved
via `model.GetSymbolInfo`, and its `ContainingNamespace` (or, for a
namespace segment referenced directly, the namespace symbol itself) is
checked against that service's own Infrastructure root, exact match or a
`.`-bounded prefix.

**Three infrastructure problems this needed, none of them the guard's own
logic:**

1. **Implicit usings.** Fresh-parsed source carries none of the SDK's
   `<ImplicitUsings>enable</ImplicitUsings>` (`Directory.Build.props`) —
   those are written by the SDK into an `obj/<Config>/<TFM>/
   <AssemblyName>.GlobalUsings.g.cs` file at build time, never by hand.
   Compiling without it failed on ordinary BCL types (`Exception`,
   `DateTimeOffset`). Fixed by reading that SAME generated file back
   (`FindGeneratedGlobalUsingsFile`) rather than hand-duplicating the
   SDK's implicit-usings list — it differs by SDK (Gateway's
   `Microsoft.NET.Sdk.Web` adds seven ASP.NET Core/hosting usings the
   other five services' `Microsoft.NET.Sdk` does not), and this test
   cannot run without `dotnet` having already built every
   `ProjectReference` it holds (including the six services themselves),
   so the file is guaranteed to exist.
2. **Shared-framework types.** `Microsoft.Extensions.Hosting`,
   `Microsoft.AspNetCore.*`, `IServiceCollection`, `IHostedService` and
   similar are supplied by `FrameworkReference`s (Billing's own
   `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, cited in
   its `.csproj`), never copied as local DLLs. Resolved via
   `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` — which lists them
   correctly ONLY because THIS test host's own
   `OrderToCash.Architecture.Tests.runtimeconfig.json` already declares
   `Microsoft.AspNetCore.App` as a framework, itself only because a
   `ProjectReference` to Gateway (`Microsoft.NET.Sdk.Web`) pulls it in
   transitively — confirmed by reading that file directly, not assumed.
3. **Self-reference ambiguity.** Including a service's own already-built
   `OrderToCash.<Service>.dll` (present in this test project's own output
   directory) as a metadata reference WHILE ALSO recompiling that
   service's full source fresh produces a genuine `CS0121` ambiguity for
   ordinary static extension-method calls (Gateway's
   `app.MapHealthEndpoints()`) — two structurally identical candidates,
   one from source and one from metadata, neither taking precedence.
   Fixed by excluding each service's own assembly file from its own
   compilation's reference set (`ownAssemblyFileName` filter) — safe
   because no service references another's compiled assembly (CLAUDE.md:
   "database per service … never FKs", the same boundary applies to the
   assemblies).

Residual diagnostics after both fixes: 8 in Billing + 2 in Fulfillment,
all `CS8795` ("partial method must have an implementation part") on
`[GeneratedRegex]` partial methods in `Presentation/Rpc/*RequestValidator.cs`
— a SOURCE GENERATOR gap (constructing a bare `CSharpCompilation` from
syntax trees does not run source generators; running the real one was out
of scope for a scan that never reports on Presentation-layer types).
Verified harmless to this guard specifically: `grep -rln
"CreditRequestValidator\|InvoiceRequestValidator\|PaymentRegisterRequestValidator\|StockRequestValidator"
src/*/Application/` returns exactly one hit, a `<c>` PROSE mention (not a
`cref`) in an XML doc comment — trivia, never a `SimpleNameSyntax` node —
so it plays no role in symbol resolution. Gateway, Orders, Notifications
and Projector compile with **zero** diagnostic errors.

## 4. Measured cost — never an estimate

`Architecture.Tests` before this feature: **35** tests. After: **36**
(the new `ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable`
population guard for the `_closureProbeServices`/`_infrastructureNamespaceRoots`
table pairing — `ApplicationMustNotDependOnInfrastructure` itself is the
SAME test, not a new one, per bullet 4).

```
$ dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build
Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 7 s   (x2, stable)
```

Baseline (task brief): 35 tests, ≈4s. Measured: 36 tests, ≈7s reported
duration (≈9.3–9.5s wall time including the VSTest host). The added ≈3s is
the closure-aware scan's own cost — six per-service `CSharpCompilation`s,
each pulling ~260 metadata references (dominated by gathering/loading
those references once, shared via `Lazy<IReadOnlyList<MetadataReference>>`
across all six, per the class doc comment on `_metadataReferences`).

## 5. Defeat list — run row by row against the NEW instrument

Per `CLAUDE.md`'s "changing a guard's instrument is a new implementation"
rule: the ten-row defeat list re-run here is a re-run for THIS scan, not
inherited from id 76's own defeat-list pass over the Cecil half.

| # | Attack | Result |
|---|---|---|
| 1 | Delete the behaviour | Ran as the required arming (§ below): removing the two injected constructions restores green, `cmp`-clean, forced rebuild |
| 2 | Corrupt a payload field the test supplied | N/A as literally stated (this guard asserts absence + names the offending TYPE, not a payload field) — the closest analogue, corrupting the resolved SYMBOL'S identity via ambiguity, is row 7 below |
| 3 | Substitute a valid sibling identifier | **Ran.** Swapped Billing's `_closureProbeServices` entry to Fulfillment's infrastructure root — `ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable` FAILED by name (verbatim below), confirming the table pairing itself is guarded, not just assumed correct |
| 4 | Shadow the pattern from a comment | **Ran.** Added a comment inside the async lambda containing the exact violating C# text (`new OrderToCash.Billing.Infrastructure...RpcErrorPayload(...)`) — guard stayed GREEN (2/2), because a comment is TRIVIA and produces no `SimpleNameSyntax` node for `GetSymbolInfo` to resolve. Structurally immune, not merely untested |
| 5 | Hide the real thing in a dead region (`#if false`) | **Ran.** Wrapped a real construction in `#if false`/`#endif` inside the lambda — guard FAILED LOUDLY, but not silently-missed: `ParseSourceFile`'s region-liveness rejection fired first, naming the file and the exact directives found (verbatim below). A defeat attempt of this shape cannot pass unnoticed; it fails a different, louder way |
| 6 | Hide in a raw/verbatim string | **Ran** (combined with row 4's probe). A raw string literal containing the same violating text as its CONTENT — guard stayed GREEN, because a `LiteralExpressionSyntax`'s content is never itself parsed as code by `GetSymbolInfo` |
| 7 | Drop an optional element entirely | **Found and fixed pre-emptively**, not merely probed: the first draft took only `symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()` — for a genuinely ambiguous reference, that checks ONE candidate and silently ignores the rest. Fixed to check every candidate (`ReferencesInfrastructureRoot`'s `foreach` over `candidates`) before the arming was captured, so the GREEN/FAIL evidence below is against the fixed version |
| 8 | Compare a literal to a literal | N/A — the check resolves REAL symbols from a REAL `CSharpCompilation` built from the actual source on disk; there are no two literals being compared |
| 9 | Satisfy the closer half of a two-part claim, leave the premise half stale | N/A in the form this row names (no two-part textual claim this guard partially satisfies) — the closest structural analogue, the Cecil half staying green while the closure half is the one doing the work, is exactly what the required arming (§ below) measures directly: Cecil alone reports `IsSuccessful=True` on the armed mutation (captured via a temporary instrumentation line, then removed), so the combined assertion's OWN premise (`netArchResult.IsSuccessful && closureOffenders.Count == 0`) is never trivially satisfied by one half alone on this mutation |
| 10 | Let a build-output copy join the population | **Ran.** Placed a real Application-namespace type with a real Infrastructure reference under `src/Billing/bin/Debug/net10.0/PhantomApplicationFile.cs` — guard stayed GREEN (2/2): `Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories).Where(path => !IsUnderBuildOutputDirectory(path))` excludes it BY PATH SEGMENT, the same shape `CompositionRootDelegationWiringTests.IsUnderBuildOutputDirectory` already uses, not a content-based filter. File removed after the probe |

### Verbatim evidence for the probes that must fail

**Row 3 (sibling substitution):**

```
Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable [11 ms]
  Error Message:
   _closureProbeServices' infrastructure roots [OrderToCash.Gateway.Infrastructure, OrderToCash.Orders.Infrastructure, OrderToCash.Fulfillment.Infrastructure, OrderToCash.Fulfillment.Infrastructure, OrderToCash.Notifications.Infrastructure, OrderToCash.Projector.Infrastructure] no longer match _infrastructureNamespaceRoots [OrderToCash.Gateway.Infrastructure, OrderToCash.Orders.Infrastructure, OrderToCash.Fulfillment.Infrastructure, OrderToCash.Billing.Infrastructure, OrderToCash.Notifications.Infrastructure, OrderToCash.Projector.Infrastructure] — the two tables drifted apart.
```

**Row 5 (dead region):**

```
Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [4 s]
  Error Message:
   /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet/src/Billing/Application/CreditHoldService.cs contains region-liveness directive(s) [#if false, #endif] — this parser is not fed the build's preprocessor symbols, so a service source file under this guard may not carry conditional compilation of this kind. Remove the directive(s).
```

All four "ran" mutations (rows 3, 4/6, 5, 10) were made on temporary
backups (`cp` before, restore-and-`cmp` after, forced rebuild before the
confirming green run) — none is the official arming bullet 4 requires
(that one is below, on real service files, with both offending types
named). `git status --short -- src/ tests/` before and after each probe
confirmed no residual change beyond the pre-existing dirty state already
in this working tree from other uncommitted phase-14 features (this
feature's own scope note in §7).

## 6. Official arming — two services, the exact missed shape, both named

Per bullet 4: a real Infrastructure construction inside
`unitOfWork.ExecuteAsync(async ct => …)`, **capturing the command
parameter** (§2's `CapturesLocalParam` shape — the one genuinely missed,
not the `this`-only shape that Cecil already catches), in TWO services.

**Mutation** (Billing's `CreditReleaseService.ReleaseAsync`, Fulfillment's
`StockReservationService.ReserveAsync`, applied together, one build, one
test run):

```csharp
// Billing/Application/CreditReleaseService.cs — added using + inside the lambda:
var armingProbeId83 = new RpcErrorPayload("PROBE", command.OrderReference);
_ = armingProbeId83;
```

```csharp
// Fulfillment/Application/StockReservationService.cs — added using + inside the lambda:
var armingProbeId83 = new RpcErrorPayload("PROBE", command.OrderReference);
_ = armingProbeId83;
```

`dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental`
— succeeded (legal C#, only a convention violation).

**Control — Cecil alone, on the SAME mutated state** (temporary
instrumentation, removed before the real run below):

```
IsSuccessful=True Failing=[]
```

Confirms the mutation is genuinely invisible to the pre-existing rule —
this is not a case where the Cecil half happens to also catch it.

**The combined test, same mutated state:**

```
Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [4 s]
  Error Message:
   Application types must not depend on any service's Infrastructure namespace — Infrastructure implements the ports Application declares, never the reverse (CLAUDE.md). RPC/Kafka wire payload records belong in src/Contracts; anything else crosses through a port. A reference confined to a lambda closure or async state machine nested two or more levels below its declaring type is invisible to the NetArchTest/Cecil scan above (id 83's own finding) and is caught here instead, by a Roslyn semantic model reading the real source. Offending types: OrderToCash.Billing.Application.CreditReleaseService, OrderToCash.Fulfillment.Application.StockReservationService
```

Both offending types named exactly, per id 82's rule.

**Restore:** `cp` back from backups taken before mutation, `cmp` both
(byte-identical), `touch` both (forced-rebuild timestamp),
`dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental`
succeeded, then:

```
$ dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build
Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 7 s
```

`git diff -- src/Billing/Application/CreditReleaseService.cs
src/Fulfillment/Application/StockReservationService.cs` shows only the
PRE-EXISTING one-line diff each carried before this session started (id
76's own uncommitted move of `RpcErrorPayload` construction sites off the
old `using`, per CLAUDE.md's own warning that `git diff` on a file with
prior uncommitted state is not proof of a clean restore — `cmp` against my
own pre-mutation backup is, and both are byte-identical).

**Count of real violations in the newly visible population, at the moment
of closing: zero** — read directly off §1's enumeration (12 sites, 11
files, across 4 of the 6 services, none constructing or calling into that
service's own Infrastructure namespace from inside the lambda), and
independently confirmed live: the
unmutated, restored source produces `closureOffenders.Count == 0` on every
run (36/36 green with no offending types listed).

## 7. Scope

Touched: `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`
(rewritten — the existing Cecil `[Fact]` kept verbatim in logic, the
closure-aware half added, folded into the same test), `progress/impl_application_layer_depends_on_infrastructure_unguarded.md`
§5 only (gap-closed note appended, nothing else in that file edited),
this record. No new `PackageReference` — `Microsoft.CodeAnalysis.CSharp`
was already added to `Architecture.Tests.csproj` by id 68. Nothing under
`src/` remains modified by this feature: the three arming/defeat-list
target files (`CreditReleaseService.cs`, `StockReservationService.cs`,
`CreditHoldService.cs`) were each `cmp`-restored to their own pre-session
backup and forced-rebuilt before the confirming green run.
`feature_list.json` not touched.

## 8. Full-suite evidence

`./quality.sh`, full run (format clean, build succeeded, all tests
passed), log at
`/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/quality_feature83.log`.

`counted:` all eighteen `Passed!` lines, `Failed: 0` on every one:

```
23 (Cqrs.UnitTests) + 50 (SharedKernel.UnitTests) + 24 (Contracts.UnitTests)
+ 134 (Fulfillment.UnitTests) + 111 (Notifications.UnitTests)
+ 211 (Gateway.UnitTests) + 242 (Billing.UnitTests) + 465 (Orders.UnitTests)
+ 44 (Seed.UnitTests) + 6 (Seed.IntegrationTests) + 36 (Architecture.Tests)
+ 59 (Projector.IntegrationTests) + 22 (Notifications.IntegrationTests)
+ 64 (Fulfillment.IntegrationTests) + 90 (Billing.IntegrationTests)
+ 61 (Gateway.IntegrationTests) + 146 (Orders.IntegrationTests)
+ 120 (Projector.UnitTests)
= 1908
```

18 projects, all `Failed: 0` — **1908/1908 passing**, including
`Orders.IntegrationTests` at 146/146 (the id 87 `OutboxRelayConcurrencyTests.OI4_…`
SQL Server deadlock the brief names as a pre-existing, unrelated
intermittent did not reproduce in this run — it is a timing-dependent
flake, not a deterministic failure, so its absence here is not evidence it
is fixed, only that this particular run did not hit it).

Reconciles against the brief's stated baseline (1907 total, 1906 passing)
as **1908 = 1907 + 1** — the one new fact this feature adds
(`ThePerServiceFolderTableMatchesTheInfrastructureNamespaceRootTable`;
`ApplicationMustNotDependOnInfrastructure` itself is the SAME test as
before, extended in place, not a new one). `Architecture.Tests` moved from
35 to 36, matching exactly; no other project's count moved (this feature
touches no other project's source or tests).

---

## Fix round 2 — D1 (record-only), A1 (fixed and armed), A2/A3/A4/A5 (folded in)

Dispatched against `progress/review_architecture_rule_cannot_see_references_inside_async_lambdas.md`
round 1 (REJECTED — one blocking record-only defect, five advisories). Per
the leader's bounds for this round: no re-arming of the existing guard, no
`./quality.sh` re-run, `feature_list.json` untouched, no `git checkout`/
`reset`/`restore`/`clean`/`stash` run.

### D1 — the population corrected in place, in §1 above

The leader's dispatch is explicit that the wrong predicate — `grep -c
"async ct =>"`, the dominant literal, not bullet 1's claim ("`async ct =>`
**and any sibling shape**") — came from the dispatch brief itself, not
from an independent misreading in this record. Re-run with a predicate
matching the claim:

```
$ find src -path '*/Application/*' -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -nE "async \(?[a-zA-Z_][a-zA-Z0-9_]*(, *[a-zA-Z_][a-zA-Z0-9_]*)*\)? *=>" \
    | sort
src/Billing/Application/CreditHoldService.cs:26:            async ct =>
src/Billing/Application/CreditReleaseService.cs:22:            async ct =>
src/Billing/Application/InvoiceIssueService.cs:44:            async ct =>
src/Billing/Application/PaymentRegisterService.cs:80:            async ct =>
src/Fulfillment/Application/DespatchCreationService.cs:53:            async ct =>
src/Fulfillment/Application/StockReplenishService.cs:16:            async ct =>
src/Fulfillment/Application/StockReservationService.cs:27:            async ct =>
src/Fulfillment/Application/StockReservationService.cs:91:            async ct =>
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:133:            async ct =>
src/Orders/Application/Commands/PlaceOrderCommandHandler.cs:98:                async ct =>
src/Orders/Application/Sagas/SagaFactHandler.cs:59:            async ct =>
src/Projector/Application/ProjectionApplyService.cs:27:            async (document, ct) =>
```

Independently confirmed the file/service counts by counting distinct
files and services rather than eyeballing the list:

```
$ find src -path '*/Application/*' -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -lE "async \(?[a-zA-Z_][a-zA-Z0-9_]*(, *[a-zA-Z_][a-zA-Z0-9_]*)*\)? *=>" \
    | sort | wc -l
11
$ <same file list> | sed -E 's#^src/([^/]+)/.*#\1#' | sort -u
Billing
Fulfillment
Orders
Projector
```

**11 files, 12 sites, 4 of the 6 services** — matching the reviewer's own
figure exactly. The twelfth site, `src/Projector/Application/
ProjectionApplyService.cs:27` (`async (document, ct) =>`, the post-apply
callback passed to `writer.ApplyAsync`), is its own classification row in
the corrected §1 table: it references `signalPublisher.PublishAsync` (a
port) and `logger.LogError` inside the lambda body, and constructs or
calls nothing under `OrderToCash.Projector.Infrastructure` — confirmed the
same way as the other ten, by grepping the whole file for the literal text
`Infrastructure` (zero hits).

§1 above was **edited in place**, not left as a superseded table beside a
correction, so a reader of §1 sees the true population directly; the
original `grep -c "async ct =>"` command and its 10-file/11-site table are
preserved verbatim in this section (above) as the record of what round 1's
sweep actually ran and why it under-counted.

**This is not a coverage hole.** Review round 1's own S3 probe placed a
real Infrastructure construction inside this exact lambda and the guard
named `OrderToCash.Projector.Application.ProjectionApplyService` —
confirming the closure-aware scan already reaches this twelfth site. So
**the closing count of zero is unchanged**: 12 sites, 11 files, 4
services, none constructing or calling into that service's own
Infrastructure namespace from inside the lambda today. The propagated
sentence in `progress/impl_application_layer_depends_on_infrastructure_unguarded.md:403`
(originally: *"the 11 sites across Billing, Fulfillment and Orders"* — both
the wrong count and the wrong service set) is corrected there too, citing
this record.

No file under `src/` or `tests/` changed for D1 — record-only, exactly as
the leader specified.

### A1 — fixed and armed, not filed

`ApplicationInfrastructureLayeringTests.cs` previously contained no
`GetDiagnostics()` call anywhere, so a compilation that failed to resolve
a symbol for any reason (a broken reference, a parse gap, a future SDK
change) would silently shrink the population `FindOffendingApplicationTypes`
scans, and nothing would notice. Two assertions now close it, both folded
into the SAME `FindOffendingApplicationTypes` method the existing
`ApplicationMustNotDependOnInfrastructure` test already calls (no new
`[Fact]`):

1. **`AssertCompilationPremiseHolds`** — every error-severity diagnostic on
   the service's `CSharpCompilation` must be on a literal, explained
   allow-list (`_allowedErrorDiagnosticIds = ["CS8795"]`, the
   `[GeneratedRegex]` source-generator gap the review measured: 8 in
   Billing, 2 in Fulfillment). Anything else fails the test naming the
   service, the diagnostic id, its location and its message.
2. **The unresolved-symbol assertion** — every `SimpleNameSyntax` node
   inside an Application-namespace type declaration, excluding the
   `nameof` contextual keyword itself (which never resolves to a symbol by
   construction — the review measured 11 such nodes, all `nameof`, across
   the six services), must resolve to a `Symbol` or at least one
   `CandidateSymbol`. A node that resolves to neither fails the test
   naming the declaring type, the node's text and its file/line span.

**Armed** by removing the `nameof` exclusion (a one-line comment-out,
`// ARMING PROBE ID83-A1: nameof exclusion removed.`) — this reintroduces
the review's own measured, naturally-occurring unresolved nodes rather
than requiring a synthetic one (a genuinely unresolved `SimpleNameSyntax`
with no compiler error is otherwise hard to construct by hand; ordinary
undefined identifiers produce a `CS0103`/`CS0246` error diagnostic first,
which `AssertCompilationPremiseHolds` would catch instead).

```
$ cp tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs <backup>
$ sha256sum tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
6a23b4c333816a278b7e0de75ceea4c93f3401e0275689434e7c1a3a63d2efe8  ...
# mutation: removed `.Where(nameNode => nameNode.Identifier.Text != "nameof")`
$ dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental
Build succeeded.
$ dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build
Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [7 s]
  Error Message:
   Orders's closure-probe compilation left 8 SimpleNameSyntax node(s) inside an Application-namespace type
   unresolved (GetSymbolInfo returned neither a Symbol nor a CandidateSymbols entry, excluding the 'nameof'
   contextual keyword) — a silently unresolved symbol is a false green for the guard above, whose whole
   purpose is that a violation cannot hide (advisory A1, id 83 review round 1).
   OrderToCash.Orders.Application.Ports.ConsumerNames — 'nameof' at
   src/Orders/Application/Ports/ConsumerName.cs: (27,51)-(27,57); OrderToCash.Orders.Application.Ports.
   SagaIgnoredFactMarkers — 'nameof' at src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs: (21,51)-
   (21,57); OrderToCash.Orders.Application.Sagas.SagaCommandKinds — 'nameof' at src/Orders/Application/
   Sagas/SagaCommandKind.cs: (47,51)-(47,57); [... 5 more, all `nameof`, all in Orders ...]
Failed!  - Failed: 1, Passed: 35, Skipped: 0, Total: 36, Duration: 7 s
```

Named the file and the symbol, as the leader's brief required. **Restore:**

```
$ cp <backup> tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
$ cmp <backup> tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
(clean — no output, no difference)
$ sha256sum tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
6a23b4c333816a278b7e0de75ceea4c93f3401e0275689434e7c1a3a63d2efe8  ...   (matches pre-mutation)
$ touch tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
$ dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental
Build succeeded.
$ dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build
Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 10 s
```

The A1 fix itself (the two permanent assertions, `nameof` exclusion
intact) was then re-applied, rebuilt and re-run green — reported in
"Confirming state after all fix-round-2 changes" below.

### A2 — deterministic candidate selection, and an agreement assertion

`FindGeneratedGlobalUsingsFile` took `candidates[0]` from an unordered
`Directory.GetFiles`. Confirmed the live instance the review found:

```
$ find src/Notifications -name "*.GlobalUsings.g.cs"
src/Notifications/obj/Release/net10.0/Notifications.GlobalUsings.g.cs
src/Notifications/obj/Debug/net10.0/Notifications.GlobalUsings.g.cs
$ diff <(sort .../obj/Debug/.../Notifications.GlobalUsings.g.cs) <(sort .../obj/Release/.../Notifications.GlobalUsings.g.cs)
(no difference — identical today)
```

Fixed: candidates are now ordered (`OrderBy(path, StringComparer.Ordinal)`)
before `candidates[0]` is chosen, **and** every other candidate's content
is asserted to match the chosen one's — a future SDK/configuration
difference that would make the choice matter now fails loudly by name
instead of silently picking whichever candidate sorted first.

### A3 — the assertion message narrowed to what the Roslyn half actually checks

The Roslyn/closure-aware half checks each service's Application source
only against that SAME service's own Infrastructure root
(`_closureProbeServices` pairs one root per service), while the combined
assertion's message previously said "any service's Infrastructure
namespace" for both halves — true of the Cecil/NetArchTest half
(`HaveDependencyOnAny(_infrastructureNamespaceRoots)`, all six), not of
the closure-aware half. Widening the check itself was not done: verified
again that no service's `.csproj` holds a `ProjectReference` to another
service, so a cross-service Infrastructure reference cannot compile in the
real build today, and widening a scan that already costs ≈3s per the
measured §4 cost for a state that cannot occur was not warranted without a
human decision to spend that budget. The assertion message in
`ApplicationMustNotDependOnInfrastructure` now states the asymmetry
explicitly — the Cecil half checks against all six roots, the closure-aware
half checks each service's own root only, unreachable today and why — so a
future reader cannot infer wider coverage than the code provides.

### A4 — the ported-idiom ledger row

**"#7 relied on X; in #8 that property is supplied by Y."** #7 enforced
Clean Architecture layering with import-path linting over SOURCE TEXT —
`order-to-cash-nestjs/eslint.config.mjs:250-287` at commit `63f130e`, a
`no-restricted-imports` ESLint rule with `**/infrastructure/**` patterns,
scoped to `apps/*/src/domain/**`. An import-path rule over source text
cannot have a depth blind spot at all: ESLint reads the `import` statement
itself, never the compiled output, so there is no lowering to hide behind
— the property (seeing a reference regardless of its lexical or emitted
nesting) was free there. #8's id 76 rendered the same kind of rule as a
Cecil/IL walk over a COMPILED assembly, where the property is not free —
the compiler's own closure/state-machine lowering puts some references two
or more levels below where the source text puts them, and Cecil never
reaches depth two of a matched type's own nested types. This feature (id
83) is what supplies the property back, via a source-level Roslyn
`SemanticModel` scan — reading syntax, like ESLint did, rather than IL.
**#9**, targeting a language whose likely layering-guard idiom is also an
AST/import-path check (Python's `import` statements, checked structurally
rather than after compilation), is very likely to get this property for
free the way #7 did; if it instead reaches for a bytecode-level check, it
inherits exactly the same depth blind spot #8 first paid for here.

### A5 — the two depth-two, fully-synchronous rows added to §2

Credited to the review round 1, §1 of `progress/review_architecture_rule_cannot_see_references_inside_async_lambdas.md`.
§2's probe table above is amended with two additional rows so the
"nesting depth, not `async`" conclusion rests on evidence that excludes
`async` entirely, not merely evidence that excludes CAPTURE while every
remaining case still contains the word `async`:

| Class | Shape | Cecil-visible dependency? |
|---|---|---|
| `SyncIteratorLocalFunction` | a **fully synchronous** iterator local function (`IEnumerable<T>` via `yield return`, no `async`/`await` anywhere) capturing a method parameter, declared inside the outer method | **no** — depth two: a `<>c__DisplayClassN_M` at depth one (the capture) with the iterator's own state machine nested inside it at depth two, exactly like an async lambda's |
| `SyncIteratorInsideCapturingSyncLambda` | the same sync iterator local function, this time declared **inside** a capturing sync lambda rather than directly in the method body | **no** — also depth two, for the same reason one level further in |

Both are **missed** by the Cecil/NetArchTest scan, and neither contains
`async` anywhere in its declaration — which is the decisive result the
review's own table (§1 above) already draws out: **nesting depth is the
mechanism**, independently of whether the code happens to be async at all.
Also recorded from the review, because a reader who skips it would
otherwise re-derive the wrong rule from the brief's own proposed
falsifier: a sync lambda nested one, two or three lexical levels deeper
(`OnlySyncLambda` → `D`/`E`/`J` in §2's table) is still **caught** at every
depth tried, because C# emits closure display classes as SIBLINGS at
depth one of the declaring type, chained by field reference, never by
type nesting — lexical depth and IL depth are different quantities, and
the falsifier could never have refuted the hypothesis either way.

### Confirming state after all fix-round-2 changes

One build, one run, after A1/A2/A3 were all applied together (D1 is
record-only, A4/A5 are prose):

```
$ pgrep -fl "dotnet (build|test|format)"
(clear)
$ dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build
Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 9 s
$ dotnet format tests/Architecture.Tests/Architecture.Tests.csproj --verify-no-changes \
    --include tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs
(no output — clean)
```

**36/36, unchanged from round 1's count** — A1's two assertions and A2's
one assertion were all folded into methods the existing test already
calls; no new `[Fact]` was added. This reconciles against the leader's
stated baseline of **36** exactly: 36 before this round, 36 after.
`Architecture.Tests` alone was re-run, not the full 1908-test tree — per
the leader's explicit bound, `./quality.sh` was not re-run this round, and
no other project's source or tests were touched (`git status --short --
src/ tests/` shows only this one file, untracked, beyond the pre-existing
phase-14 dirty state already present at dispatch — confirmed by `git log
--oneline -- tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`
returning nothing and `git ls-files` returning nothing for the same path,
i.e. the file has never been committed).

`feature_list.json` not touched by this round (`git diff --stat --
feature_list.json` shows the same pre-existing dirty state that predates
this round's dispatch, matching round 1's own note that it was untouched
by the implementer and by the reviewer).

### Scope, restated for round 2

Touched: `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`
(A1's two assertions, A2's ordering + agreement assertion, A3's message
correction — all additive, no existing assertion's logic changed), this
record (§1 corrected in place, this section appended),
`progress/impl_application_layer_depends_on_infrastructure_unguarded.md:402-409`
(the propagated D1 sentence corrected). Nothing under `src/` touched.
`feature_list.json` untouched. No `git checkout`/`reset`/`restore`/
`clean`/`stash` run at any point.
