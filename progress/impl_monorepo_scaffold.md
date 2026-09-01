# impl_monorepo_scaffold — Feature 6 (phase 5)

## What I built

A compiling, testable OrderToCash.sln scaffold: central package management, per-project
Clean-Architecture folder layout for every service, an Architecture.Tests project with
NetArchTest.Rules + reflection-based rules enforcing the CLAUDE.md "Domain purity" and
"decimal" non-negotiables, and a quality.sh that runs format-check + build + test +
coverage collection (no gate yet — that is feature 34).

This feature is `sdd: false`; scope was taken from `feature_list.json`'s feature 6
`acceptance` array, as instructed.

## Files touched

Root:
- `OrderToCash.sln` — new. Classic `.sln` format (`dotnet new sln -f sln`, since .NET 10's
  default is now `.slnx` and the task named `OrderToCash.sln` explicitly).
- `Directory.Build.props` — new. `net10.0`, `Nullable`, `ImplicitUsings`,
  `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`, plus
  a comment stating severities live in `.editorconfig` and enforcement lives here.
- `Directory.Packages.props` — new. `ManagePackageVersionsCentrally=true`, one
  `PackageVersion` per package, using exactly the versions given in the brief.
- `quality.sh` — new, executable. `dotnet format --verify-no-changes` → `dotnet build` →
  `dotnet test --collect:"XPlat Code Coverage"` → parses and prints the Cobertura
  line-rate. Contains an explicit `TODO(feature 34 — sonarqube_quality_gates, phase 21)`
  marking where the enforced, fail-when-breached gate belongs. Does not fake a gate.

`src/` (all new):
- `SharedKernel/SharedKernel.csproj` + `README_PLACEHOLDER.cs` — zero `PackageReference`,
  no layer folders (per CLAUDE.md, this and Contracts are the only shared runtime code and
  have no layer structure).
- `Contracts/Contracts.csproj` + `README_PLACEHOLDER.cs` — no layer folders.
- `Gateway/`, `Orders/`, `Fulfillment/`, `Billing/`, `Notifications/`, `Projector/`,
  `Seed/` — each a `.csproj` referencing `SharedKernel` + `Contracts`, plus
  `Domain/README_PLACEHOLDER.cs`, `Application/README_PLACEHOLDER.cs`,
  `Infrastructure/README_PLACEHOLDER.cs`, `Presentation/README_PLACEHOLDER.cs`. Each
  placeholder is a single `sealed class <Project><Layer>Placeholder;` in namespace
  `OrderToCash.<Project>.<Layer>`, with a doc comment naming the future feature that
  replaces it. `RootNamespace`/`AssemblyName` are `OrderToCash.<Project>`.

`tests/Architecture.Tests/` (all new):
- `Architecture.Tests.csproj` — xUnit + NetArchTest.Rules + coverlet.collector,
  `ProjectReference` to all nine `src/` projects.
- `DomainAssemblies.cs` — the seven assemblies that own a `Domain/` folder (excludes
  SharedKernel/Contracts, which have none).
- `DomainPurityTests.cs` — six named `[Fact]`s, one per forbidden dependency.
- `SharedKernelHasNoPackagesTests.cs` + `RepositoryPaths.cs` — plain xUnit test that parses
  `src/SharedKernel/SharedKernel.csproj` text for `<PackageReference` (NetArchTest cannot
  see project files). `RepositoryPaths.Find` walks up from `AppContext.BaseDirectory`
  looking for `OrderToCash.sln`.
- `DomainDecimalTests.cs` — one `[Fact]` that reflects over every type in a `*.Domain`
  namespace segment across all seven service assemblies, checking fields, properties,
  method parameters/return types and constructor parameters for `typeof(decimal)`.

`feature_list.json` — feature 6's `status` set `in_progress` → `in_review` only (it was
already `in_progress` when I started; the leader had set that before launching me).

## Traceability to the acceptance list

| Acceptance criterion | How it is proven |
|---|---|
| `dotnet build` works from the solution root | `dotnet build OrderToCash.sln` — 0 errors, 0 warnings (verified below) |
| `global.json` honoured; `TreatWarningsAsErrors` on | `dotnet --version` under the solution resolves `10.0.111` per `global.json`; `Directory.Build.props` sets `TreatWarningsAsErrors=true`, and the whole solution built clean under it |
| NetArchTest fails the build on a deliberate Domain-layer violation of each forbidden reference | armed and reverted for all 6, table below |
| NetArchTest fails on a deliberate `decimal` in domain arithmetic | armed and reverted, table below |
| `./quality.sh` runs format check + build + test + coverage | ran end to end, output captured below |

## Per-rule arming evidence (the most important part of this report)

For every rule, I introduced the exact violation, ran the single named test with
`dotnet test ... --filter "FullyQualifiedName~<Name>"`, captured the failure verbatim,
then reverted the violation and confirmed the file diff was clean again
(`git diff` + `git status --porcelain` showed no residue). All arming edits were made to
`src/Orders/Domain/README_PLACEHOLDER.cs` and `src/Orders/Orders.csproj` (rules 1–6, 8) or
`src/SharedKernel/SharedKernel.csproj` (rule 7).

### 1. `DomainMustNotDependOnEntityFrameworkCore`
- Violation: added `<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />`
  to `Orders.csproj` and `public Microsoft.EntityFrameworkCore.DbContext? Context { get; set; }`
  to `OrdersDomainPlaceholder`.
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnEntityFrameworkCore`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnEntityFrameworkCore [209 ms]
  Error Message:
   Domain types must not depend on Microsoft.EntityFrameworkCore. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 2. `DomainMustNotDependOnConfluentKafka`
- Violation: `<PackageReference Include="Confluent.Kafka" />` +
  `public Confluent.Kafka.IProducer<string, string>? Producer { get; set; }`.
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnConfluentKafka`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnConfluentKafka [180 ms]
  Error Message:
   Domain types must not depend on Confluent.Kafka. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 3. `DomainMustNotDependOnNats`
- Violation: `<PackageReference Include="NATS.Net" />` +
  `public NATS.Client.Core.NatsOpts? Opts { get; set; }` (confirmed via a probe that
  `NATS.Net.dll` is an empty facade — the real types live in `NATS.Client.Core`, and
  `HaveDependencyOn("NATS")` matches it as a prefix, so the "any NATS.*" rule is real).
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnNats`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnNats [199 ms]
  Error Message:
   Domain types must not depend on any NATS.* type. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 4. `DomainMustNotDependOnMongoDb`
- Violation: `<PackageReference Include="MongoDB.Driver" />` +
  `public MongoDB.Bson.ObjectId Id { get; set; }`.
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnMongoDb`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnMongoDb [176 ms]
  Error Message:
   Domain types must not depend on any MongoDB.* type. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 5. `DomainMustNotDependOnAspNetCore`
- Violation: `<FrameworkReference Include="Microsoft.AspNetCore.App" />` +
  `public Microsoft.AspNetCore.Http.HttpContext? Context { get; set; }`.
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnAspNetCore`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnAspNetCore [165 ms]
  Error Message:
   Domain types must not depend on Microsoft.AspNetCore.*. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 6. `DomainMustNotDependOnSystemTextJson`
- Violation: `public string Serialize(object o) => System.Text.Json.JsonSerializer.Serialize(o);`
  (no extra package needed — `System.Text.Json` is part of the shared framework).
- Test: `OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnSystemTextJson`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.DomainPurityTests.DomainMustNotDependOnSystemTextJson [164 ms]
  Error Message:
   Domain types must not depend on System.Text.Json. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder
  ```
- Reverted; green again.

### 7. `SharedKernelCsprojDeclaresZeroPackageReferences`
- Violation: added `<PackageReference Include="NetArchTest.Rules" />` to
  `src/SharedKernel/SharedKernel.csproj`.
- Test: `OrderToCash.Architecture.Tests.SharedKernelHasNoPackagesTests.SharedKernelCsprojDeclaresZeroPackageReferences`
- Verbatim failure:
  ```
  Failed OrderToCash.Architecture.Tests.SharedKernelHasNoPackagesTests.SharedKernelCsprojDeclaresZeroPackageReferences [13 ms]
  Error Message:
   src/SharedKernel/SharedKernel.csproj must declare zero PackageReference entries, found: <PackageReference
  ```
- Reverted; green again.

### 8. `NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType`
- Violation: `public decimal TotalAmount { get; set; }` on `OrdersDomainPlaceholder`.
- Test: `OrderToCash.Architecture.Tests.DomainDecimalTests.NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType`
- Verbatim failure (note it independently catches both the property and its
  compiler-generated backing field):
  ```
  Failed OrderToCash.Architecture.Tests.DomainDecimalTests.NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType [20 ms]
  Error Message:
   decimal must not appear in domain arithmetic. Offences: OrderToCash.Orders.Domain.OrdersDomainPlaceholder.<TotalAmount>k__BackingField (field); OrderToCash.Orders.Domain.OrdersDomainPlaceholder.TotalAmount (property)
  ```
- Reverted; green again.

After all eight arm/revert cycles, `git status --porcelain` on `src/Orders/*` and
`src/SharedKernel/*` showed no diff, and the full suite is green (see below).

## Final verification

`dotnet build OrderToCash.sln`: `Build succeeded. 0 Warning(s). 0 Error(s).`

`dotnet test OrderToCash.sln`: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`

The 8 passing tests are exactly: the 6 `DomainPurityTests`, 1
`SharedKernelHasNoPackagesTests`, 1 `DomainDecimalTests`.

`dotnet format OrderToCash.sln --verify-no-changes`: clean, no output.

`./quality.sh`: all four sections (`Format check`, `Build`, `Test + coverage`,
`Coverage summary`) pass; coverage report parsed at 100.0% line coverage (expected —
every line in the eight test files and nine placeholder types executes).

`./init.sh`: exits 0. `git status --porcelain | grep -v '^??'` shows only
`feature_list.json` modified (status flip); everything else is new/untracked as expected
for a from-scratch scaffold.

## What I could not do / deferred

- Domain purity for `Gateway.Domain` is included in the architecture tests even though
  CLAUDE.md's layer diagram implies Gateway is presentation-only; the task explicitly
  listed Gateway among the seven services needing all four layer folders, so I kept it
  symmetric with the other six. No real Gateway domain logic exists yet (phase 13), so
  this is inert until then.
- No coverage *gate* — as instructed, feature 34 owns that, and I left a named TODO in
  `quality.sh` rather than fabricate one.
- `Contracts` and `SharedKernel` are deliberately trivial placeholders — Money, Quantity,
  GLN, etc. are feature 7's scope, and generated envelope/DTO types are feature 8's.

## Surprises

- `dotnet new sln` on this SDK (.NET 10 / `10.0.111`) defaults to the new `.slnx` XML
  format; had to pass `-f sln` explicitly to get the classic `OrderToCash.sln` the brief
  named.
- NetArchTest 1.3.2's `HaveDependencyOn(string)` does prefix/substring matching against a
  type's IL member references, not just its declaring assembly name — confirmed this
  empirically with a throwaway probe project before writing the "NATS" rule, since
  `NATS.Net.dll` itself contains zero types (it is a bare facade re-exporting
  `NATS.Client.Core`, `NATS.Client.Serializers.Json`, etc.), so a naive assembly-name-based
  approach would have silently never fired.
- `Types.InAssemblies(...)` takes an optional `searchDirectories` parameter I initially
  assumed was required — a quick reflection probe over the compiled `NetArchTest.Rules`
  assembly settled every API question in this feature (arg order, optionality, exact
  matching semantics) faster than searching docs would have, and left an auditable trail
  of what was actually verified rather than assumed.

---

## Re-review round — D1 and D2 fixed (feature back from REJECTED, `progress/review_monorepo_scaffold.md`)

The section above is the original (rejected) report, left intact. This section documents
what was wrong, the fix, and the independent re-arming evidence.

### D1 (REJECT-level): what it actually was

All six rules in `tests/Architecture.Tests/DomainPurityTests.cs` selected domain types with
`.That().ResideInNamespaceEndingWith(".Domain")`, a literal **suffix** match. It matches
`OrderToCash.Orders.Domain` but not `OrderToCash.Orders.Domain.ValueObjects`,
`…Domain.Events`, `…Domain.Errors` — i.e. it silently stops guarding the instant a real
`Domain/ValueObjects/`, `Domain/Events/` etc. subfolder appears, which CLAUDE.md's own layer
description guarantees will happen from feature 7 onward. The reviewer proved this by
placing a live `MongoDB.Bson.ObjectId` and a live `System.Text.Json.JsonSerializer` call
inside `OrderToCash.Orders.Domain.ValueObjects` and showing all six purity tests stayed
green. `DomainDecimalTests.cs` already used a namespace-segment regex
(`(^|\.)Domain(\.|$)`) that does cover sub-namespaces — the two rule families disagreed
about what "the domain layer" means, and the purity rules were the wrong one.

**Fix.** Added a single shared constant,
`DomainAssemblies.DomainNamespacePattern = @"(^|\.)Domain(\.|$)"`, in
`tests/Architecture.Tests/DomainAssemblies.cs`. All six rules in `DomainPurityTests.cs` now
use `.That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)` in place of
`ResideInNamespaceEndingWith(".Domain")` — `ResideInNamespaceMatching` is a real NetArchTest
1.3.2 API (confirmed by reflecting over the compiled `NetArchTest.Rules.dll`, the same
technique used in the original implementation). `DomainDecimalTests.cs`'s
`[GeneratedRegex(...)]` now also references `DomainAssemblies.DomainNamespacePattern`
instead of its own literal, so the two rule families provably share one definition of "the
domain layer" going forward rather than merely agreeing by coincidence today. `const string`
values from another class are valid arguments to `[GeneratedRegex]` (it requires a
compile-time constant, which a cross-class `const` satisfies) — confirmed by a clean build.

### D2 (medium): what it actually was

Nothing asserted that `DomainAssemblies.All` names the right seven service assemblies, or
that the domain-namespace selector actually yields a non-empty type set per assembly. A
future edit that dropped a service from the list, or renamed its `Domain` namespace, would
leave every purity/decimal rule silently passing over a shrinking (or eventually empty) type
set — a rule that "passes" on an empty selection is worthless.

**Fix.** New file `tests/Architecture.Tests/DomainAssembliesTests.cs` with two `[Fact]`s:
- `DomainAssembliesAllContainsExactlyTheSevenExpectedServiceAssemblies` — asserts
  `DomainAssemblies.All`'s assembly names equal exactly
  `{OrderToCash.Gateway, OrderToCash.Orders, OrderToCash.Fulfillment, OrderToCash.Billing,
  OrderToCash.Notifications, OrderToCash.Projector, OrderToCash.Seed}` (order-independent).
- `DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly` — for every assembly in
  `DomainAssemblies.All`, runs `Types.InAssembly(assembly).That().ResideInNamespaceMatching(
  DomainAssemblies.DomainNamespacePattern).GetTypes()` and asserts at least one type is
  selected; reports which assemblies came back empty by name if it fails.

Total suite is now 10 named tests (8 original + 2 new), all green.

### D3 (advisory) — recorded as a follow-up, not fixed here

Per the reviewer's explicit instruction, D3 (`SharedKernelHasNoPackagesTests` only greps the
one `.csproj` file text and would miss a package reaching `SharedKernel` via a
`GlobalPackageReference` in `Directory.Packages.props` or an `ItemGroup` in
`Directory.Build.props`) is **not** addressed in this round. It is not a rejection ground
for feature 6 and belongs with **feature 7 (`shared_kernel`)**, when `SharedKernel` gets
real content — ideally by also asserting the compiled assembly's `GetReferencedAssemblies()`
contains nothing outside the shared framework, as the reviewer suggested. Recording it here
so it is not lost.

### Independent re-arming evidence (this round)

All arming was done by editing `src/Orders/Orders.csproj` and adding temporary files under
`src/Orders/Domain/` (and once, editing `tests/Architecture.Tests/DomainAssemblies.cs`
itself for D2), then reverting and diffing against a pre-arming backup to confirm zero
residue. Baseline before this round: `dotnet build` → 0 warnings/0 errors;
`dotnet test OrderToCash.sln` → `Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`
(the 2 new D2 tests already included, added before arming so their own baseline is proven
too).

| # | Violation introduced | Where | Test run | Test name | Result | Verbatim message |
|---|---|---|---|---|---|---|
| 1 | `MongoDB.Bson.ObjectId Id` property + `System.Text.Json.JsonSerializer.Serialize` call, both on one type | **nested**: `OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` (new file `src/Orders/Domain/ValueObjects/ReviewerNested.cs`) + `<PackageReference Include="MongoDB.Driver" />` added to `Orders.csproj` | `dotnet test ... --filter "FullyQualifiedName~DomainPurityTests"` | `DomainMustNotDependOnMongoDb` | **FAILED (correct — was PASSED/DEFECT pre-fix)** | `Domain types must not depend on any MongoDB.* type. Offending types: OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` |
| 2 | same file as row 1 | same nested namespace | same filter run | `DomainMustNotDependOnSystemTextJson` | **FAILED (correct — was PASSED/DEFECT pre-fix)** | `Domain types must not depend on System.Text.Json. Offending types: OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` |
| 3 | `Microsoft.EntityFrameworkCore.DbContext?` property | **nested**: `OrderToCash.Orders.Domain.ValueObjects.ReviewerNestedEfCore` (new file) + `<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />` added to `Orders.csproj` | `dotnet test ... --filter "FullyQualifiedName~DomainMustNotDependOnEntityFrameworkCore"` | `DomainMustNotDependOnEntityFrameworkCore` | **FAILED (correct)** | `Domain types must not depend on Microsoft.EntityFrameworkCore. Offending types: OrderToCash.Orders.Domain.ValueObjects.ReviewerNestedEfCore` |
| 4 | `Seed` service commented out of the array (line replaced with a comment, not deleted, to make the diff obvious) | `tests/Architecture.Tests/DomainAssemblies.cs`, `DomainAssemblies.All` | `dotnet test ... --filter "FullyQualifiedName~DomainAssembliesTests"` | `DomainAssembliesAllContainsExactlyTheSevenExpectedServiceAssemblies` | **FAILED (correct — new D2 test)** | `DomainAssemblies.All must contain exactly {OrderToCash.Billing, OrderToCash.Fulfillment, OrderToCash.Gateway, OrderToCash.Notifications, OrderToCash.Orders, OrderToCash.Projector, OrderToCash.Seed}, found {OrderToCash.Billing, OrderToCash.Fulfillment, OrderToCash.Gateway, OrderToCash.Notifications, OrderToCash.Orders, OrderToCash.Projector}.` |

Rows 1–2 are a direct re-run of the reviewer's own arming case (their table rows 6–7) against
the fixed selector — same violation, same nested namespace, opposite (correct) result. Row 3
adds a third rule family (EF Core) at the nested namespace, deliberately chosen because
neither the implementer's nor the reviewer's original arming table exercised EF Core/Kafka/
NATS at all, to demonstrate the fix lives in the shared `DomainNamespacePattern` selector and
not in a per-rule patch — three independent rules (Mongo, System.Text.Json, EF Core) all now
fire on the identical nested-namespace shape. Row 4 arms the new D2 non-vacuity test.

**Restore and final green check**, after every row above was reverted (temporary files
deleted, `Orders.csproj` and `DomainAssemblies.cs` restored from pre-arming byte-for-byte
backups, confirmed with `diff`):

- `dotnet build OrderToCash.sln --nologo` → `Build succeeded. 0 Warning(s). 0 Error(s).`
- `dotnet test OrderToCash.sln --nologo` →
  `Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`
- `dotnet format OrderToCash.sln --verify-no-changes` → clean, no output (this also caught
  and required fixing an `IDE1006` naming-rule violation on the new
  `DomainAssembliesTests.ExpectedAssemblyNames` field — renamed to `_expectedAssemblyNames`
  per `.editorconfig`'s private-field convention, which is itself a live demonstration that
  `TreatWarningsAsErrors`/format enforcement works)
- `./quality.sh` → all four sections green, 10/10 tests, 100.0% line coverage (still
  info-only, no gate — unchanged from the original report, feature 34's scope)
- `./init.sh` → exits 0, only the expected "N uncommitted change(s) — expected mid-session"
  warning
- `git status --porcelain --untracked-files=all` → identical file set to the original
  (rejected) submission plus exactly one new file,
  `tests/Architecture.Tests/DomainAssembliesTests.cs`; no probe residue in `src/Orders/**`
  or `tests/Architecture.Tests/DomainAssemblies.cs`

### Files touched in this round

- `tests/Architecture.Tests/DomainAssemblies.cs` — added `DomainNamespacePattern` constant
  (D1's shared selector).
- `tests/Architecture.Tests/DomainPurityTests.cs` — all six rules switched from
  `ResideInNamespaceEndingWith(".Domain")` to
  `ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)`.
- `tests/Architecture.Tests/DomainDecimalTests.cs` — `[GeneratedRegex(...)]` now references
  the same shared constant instead of its own literal.
- `tests/Architecture.Tests/DomainAssembliesTests.cs` — new file, D2's two non-vacuity tests.
- `progress/impl_monorepo_scaffold.md` — this section.
- `feature_list.json` — status only, `in_progress` → `in_review`.

No file under `src/**` was left modified — the EF Core/MongoDB/SharedKernel probes used
during arming were all temporary and fully reverted, confirmed by `diff` against
pre-arming backups and by `git status --porcelain --untracked-files=all` showing the same
`src/**` file set as the original submission.
