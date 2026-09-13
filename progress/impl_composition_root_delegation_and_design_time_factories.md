# impl: Slice A of phase 14's guard-hardening loop — id 67 (`design_time_dbcontext_factory_env_reads_are_unguarded`) and id 68 (`composition_root_delegation_and_wiring_are_unguarded`)

## Status header

Implementation complete, self-verified. Full solution build: `dotnet build
OrderToCash.sln --no-incremental` — **0 warnings, 0 errors**. `dotnet format
OrderToCash.sln --verify-no-changes` — clean (exit 0). Full test tree —
container-free unit projects run directly, container-backed integration
projects run via Testcontainers against the live Docker daemon on this
machine — **1905 passed, 0 failed, 0 skipped, across 18 projects** (counted
below, reconciles exactly against the brief's stated baseline of 1880 + this
session's 25 new tests). `./init.sh` exits 0; `feature_list.json` was **not**
edited (id 67 remains `in_progress`, exactly as the leader owns the
transition). Recommendation: ready for the leader to set id 67 → `in_review`,
transition id 68 `pending` → `in_progress` → `in_review`.

Every mutation in the arming tables below was restored from a `cp` backup,
confirmed identical with `cmp`, and confirmed identical a second way for six
of the production files via `git diff` (they are **tracked**, so that check
can fail): `git status --short` on the full set of production files this
slice touched during arming shows **zero** modifications — the four
`*DbContextFactory.cs` files, the seven `Program.cs` files and
`SeedRunner.cs` are all byte-identical to HEAD.

## The one thing done first: id 68's population re-derived, not inherited

The filed entry claimed "ten delegating call sites — the six `Program.cs` →
`ProgramConfiguration.Configure` calls." That figure does not reconcile and
was **not** used. Re-derived as a search result, unit stated explicitly:

```
$ for f in src/Billing/Program.cs src/Fulfillment/Program.cs src/Gateway/Program.cs \
           src/Notifications/Program.cs src/Projector/Program.cs src/Orders/Program.cs \
           src/Seed/Program.cs; do
    echo "=== $f ==="; grep -oE "configure[A-Za-z]*:" "$f" | sort
  done
```

| File | `configure*:` arguments found |
|---|---|
| `src/Billing/Program.cs` | `configure:`, `configureHealth:`, `configureTelemetry:` (3) |
| `src/Fulfillment/Program.cs` | `configure:`, `configureHealth:`, `configureTelemetry:` (3) |
| `src/Gateway/Program.cs` | `configure:`, `configureTelemetry:` (2) |
| `src/Notifications/Program.cs` | `configure:`, `configureHealth:`, `configureTelemetry:` (3) |
| `src/Projector/Program.cs` | `configure:`, `configureHealth:`, `configureTelemetry:` (3) |
| `src/Orders/Program.cs` | `configureOutbox:`, `configureAcceptance:`, `configureSaga:`, `configureTelemetry:`, `configureHealth:` (5) |
| `src/Seed/Program.cs` | none (0) |

**Sum: 19.** The unit is a **delegating ARGUMENT** — the identifier bound to
a `configureX:` named parameter of a `*Host.CreateBuilder`/
`GatewayHost.Build` call inside a `Program.cs` — never a line, never a file.
Verified Gateway's real signature (not the filed "1", not a guessed "3"):
`grep -n "ConfigureHealth\|GatewayHost.Build" src/Gateway/*.cs` shows
`GatewayHost.Build` is called with exactly `configure:` and
`configureTelemetry:` — no `configureHealth:` exists for Gateway at all, so
the filed entry's implied packing of three arguments onto Gateway's one line
was itself wrong on top of the line-counting artefact it named.

Two more shapes exist that are **not** an argument at all and would
otherwise have silently escaped the argument-only definition:

- `src/Seed/Program.cs:10` → `SeedRunner.RunAsync()` — a plain static call,
  no delegate parameter.
- `src/Seed/Presentation/SeedRunner.cs:26-28` — three connection-string-to-
  `OpenDb` pairings, the exact shape R2-5 found unguarded.

Both are named explicitly below and covered by their own guard rather than
folded into the 19, so the true population this slice closes is **19
delegating arguments + 1 direct call + 3 connection-string pairings = 23
distinct guarded sites**, all in one mechanism.

## The mechanism (chosen once, inherited)

Both entries port id 56's own precedent — top-level statements compile to
the unreachable, compiler-generated `Program.<Main>$` (id 56's review
verified this by reflecting over a minimal reproduction; re-confirmed here
by re-reading that review section rather than re-deriving it), so **no test
can execute `Program.cs`'s own line**. The strongest guard available without
reversing the standing decision against `WebApplicationFactory`/`TestServer`
(`tests/Gateway.IntegrationTests/GatewayTestHost.cs:11`, a decision the
review explicitly said is a gate call, not one to take inside this entry) is
to read the **actual, current source text** of `Program.cs`/`SeedRunner.cs`
at test time and assert, by name, exactly which identifier each delegating
site binds.

This is not invented for this feature — it is this repository's own
established idiom for guarding a property no compiled type can express:
`tests/Gateway.UnitTests/WriteDatabaseAbsenceTests.cs` (`File.ReadAllText`
+ line-scan over `.cs`/`.csproj` text) and
`tests/Architecture.Tests/HealthProbeCopyParityTests.cs` (byte-comparison of
source text against a canonical) both do exactly this. The new file,
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`, applies
the SAME technique uniformly to every one of the 23 sites — one test per
service/shape, not one bespoke mechanism per call site (id 68 bullet 3).

For id 67, the mechanism id 56's review recommended and this slice
implements literally: drive each `*DbContextFactory` through the exact
interface `dotnet ef` uses.

```csharp
private static IDesignTimeDbContextFactory<OrdersDbContext> Factory => new OrdersDbContextFactory();
...
using var db = Factory.CreateDbContext(args);
return db.Database.GetConnectionString()!;
```

`Factory` is typed as `IDesignTimeDbContextFactory<TContext>`, not as the
concrete `OrdersDbContextFactory`, so the call resolves through the same
interface method `dotnet ef`'s reflection discovers — never through
`SeedDbConfig.BuildConnectionString` or any other helper the factory happens
to share (id 67 bullet 2). One nuance discovered while writing this:
`DatabaseFacade.GetConnectionString()` does **not** return the raw string
passed to `UseSqlServer` — it returns `SqlConnectionStringBuilder`'s
normalised form (`Data Source=...;Initial Catalog=...;User ID=...`, plus an
`Application Name` EF Core appends). Assertions are against that normalised
form's component substrings (`Assert.Contains("Initial Catalog=custom_orders_db", ...)`),
not a full-string `Assert.Equal` against the pre-normalisation literal —
confirmed by capturing the actual normalised string once via a deliberate
`Assert.Fail(connectionString)` probe before writing the real assertions.

## id 67 — the four `*DbContextFactory` classes, 20 reads

Structurally identical across all four: `MSSQL_HOST` (default `localhost`),
`MSSQL_HOST_PORT` (default `1433`), `MSSQL_DB_<SERVICE>` (default
`otc_<service>`), `MSSQL_APP_USER` (default `otc_app`), `MSSQL_APP_PASSWORD`
(**no default** — throws `InvalidOperationException` naming the variable).
20 reads = 5 × 4 services, re-verified against the four files read in full
this session (`OrdersDbContextFactory.cs`, `BillingDbContextFactory.cs`,
`FulfillmentDbContextFactory.cs`, `NotificationsDbContextFactory.cs`).

New test files, one per service, each 4 `[Fact]`s (16 new tests total):

- `tests/Orders.UnitTests/OrdersDbContextFactoryTests.cs`
- `tests/Billing.UnitTests/BillingDbContextFactoryTests.cs`
- `tests/Fulfillment.UnitTests/FulfillmentDbContextFactoryTests.cs`
- `tests/Notifications.UnitTests/NotificationsDbContextFactoryTests.cs`

Each file's four tests:

1. `CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues` —
   guards `MSSQL_HOST`, `MSSQL_HOST_PORT`, the service's own `MSSQL_DB_*`,
   `MSSQL_APP_USER` in one shot (four independent `Assert.Contains`).
2. `CreateDbContext_BuildsEveryDocumentedDefault_WhenOnlyTheRequiredPasswordIsSet`
   — guards the four fallback literals.
3. `CreateDbContext_Throws_WhenMsSqlAppPasswordIsNotSet` — guards the
   required-password throw and its message naming the variable.
4. `CreateDbContext_ReadsMsSqlDb<Service>_FromItsOwnDistinctVariableName_NeverASiblingsKey`
   — the substitution guard (bullet 3): sets the service's own `MSSQL_DB_*`
   AND all three siblings to distinct non-default values, then asserts the
   own value is present and (for extra strength beyond the minimum) that
   none of the three sibling values leaked in.

**Test isolation.** All four `*ProgramConfiguration.Configure` test classes
(id 56) already mutate the same `MSSQL_*` process-wide variables the new
`*DbContextFactory` test classes mutate. xUnit parallelises across
collections by default, so a second class touching the same names in the
same assembly is a real race, not a theoretical one — this is exactly the
hazard `GatewayEnvironmentVariableTestCollection` (id 56) was written to
close, and none of Orders/Billing/Fulfillment/Notifications had it yet
because each previously had only ONE class touching these names. Added one
`[CollectionDefinition]` per service (`OrdersEnvironmentVariableTestCollection`,
`BillingEnvironmentVariableTestCollection`,
`FulfillmentEnvironmentVariableTestCollection`,
`NotificationsEnvironmentVariableTestCollection`) and applied `[Collection(...)]`
to both the pre-existing `*ProgramConfigurationTests` class (one-line diff
each, confirmed by `git diff` above) and the new `*DbContextFactoryTests`
class.

## id 68 — composition-root delegation and Seed wiring

New file: `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`
(9 new tests — chosen as Architecture.Tests because it already references
all seven service projects including Seed, and already carries this
repository's source-text-assertion idiom).

- One test per service (`BillingProgramCs_Delegates...`,
  `FulfillmentProgramCs_Delegates...`, `GatewayProgramCs_Delegates...`,
  `NotificationsProgramCs_Delegates...`, `ProjectorProgramCs_Delegates...`,
  `OrdersProgramCs_DelegatesAllFiveConfigureArguments_...`) asserting every
  one of that file's `configure*:` arguments names the exact expected
  `<Service>ProgramConfiguration.<Method>` target.
- `SeedProgramCs_CallsSeedRunnerRunAsync_TheExtractedOrchestrationMethod` —
  the direct-call shape.
- `SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings`
  — R2-5's exact fix: asserts `OrdersSeedWriter.OpenDb(...)` is called with
  `ordersConnectionString` (never `fulfillmentConnectionString`/
  `billingConnectionString`), and the same for the other two writers.
- `ThePopulationTableHoldsExactlyNineteenDelegatingArguments` — a meta-guard
  on the literal table itself, so a future service addition that is not
  reflected in the table fails loudly rather than silently under-counting.

## Arming table — 16 mutations, every one restored and confirmed clean

Protocol followed exactly for all 16: `cp` a backup → mutate → `dotnet build
--no-incremental` (scoped to the affected test project, which rebuilds its
full dependency chain) → run the ONE named test → record the verbatim
failure → restore from the `cp` backup → `cmp` against the backup (identical
in all 16 cases) → for the six **tracked** production files this slice
mutated (all of them — every file below is committed, so `git diff` is
available and is the strongest check), `git status --short`/`git diff`
additionally confirmed clean, reported in the Status header above → `touch`
+ forced rebuild → confirm green. One build/test run at a time throughout;
no concurrent builds.

### id 67 — 8 mutations (2 per factory: one deletion, one substitution)

| # | File | Mutation | Named test | Verbatim failure |
|---|---|---|---|---|
| 1 | `OrdersDbContextFactory.cs:22` | deleted `MSSQL_HOST` read (`Environment.GetEnvironmentVariable("MSSQL_HOST") ?? "localhost"` → `"localhost"`) | `OrdersDbContextFactoryTests.CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues` | `Assert.Contains() Failure: Sub-string not found` / `String: "Data Source=localhost,14330;Initial Catal"···` / `Not found: "Data Source=sql-box,14330"` |
| 2 | `OrdersDbContextFactory.cs:24` | substituted `MSSQL_DB_ORDERS` → `MSSQL_DB_BILLING` | `OrdersDbContextFactoryTests.CreateDbContext_ReadsMsSqlDbOrders_FromItsOwnDistinctVariableName_NeverASiblingsKey` | `Not found: "Initial Catalog=custom_orders_db"` (string shows `"Data Source=localhost,1433;Initial Catalo"···` — the sibling's own default-orders literal never appears; the assertion fails on the correct reason, not a default fallback) |
| 3 | `BillingDbContextFactory.cs:23` | deleted `MSSQL_HOST_PORT` read | `BillingDbContextFactoryTests.CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues` | `Not found: "Data Source=sql-box,14330"` (actual `"Data Source=sql-box,1433;Initial Catalog="···`) |
| 4 | `BillingDbContextFactory.cs:24` | substituted `MSSQL_DB_BILLING` → `MSSQL_DB_FULFILLMENT` | `BillingDbContextFactoryTests.CreateDbContext_ReadsMsSqlDbBilling_FromItsOwnDistinctVariableName_NeverASiblingsKey` | `Not found: "Initial Catalog=custom_billing_db"` |
| 5 | `FulfillmentDbContextFactory.cs:25` | deleted `MSSQL_APP_USER` read | `FulfillmentDbContextFactoryTests.CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues` | `Not found: "User ID=custom_user"` (actual `"Data Source=sql-box,14330;Initial Catalog"···`) |
| 6 | `FulfillmentDbContextFactory.cs:24` | substituted `MSSQL_DB_FULFILLMENT` → `MSSQL_DB_NOTIFICATIONS` | `FulfillmentDbContextFactoryTests.CreateDbContext_ReadsMsSqlDbFulfillment_FromItsOwnDistinctVariableName_NeverASiblingsKey` | `Not found: "Initial Catalog=custom_fulfillment_db"` |
| 7 | `NotificationsDbContextFactory.cs:34-38` | deleted the required-password throw (`?? throw new InvalidOperationException(...)` → `?? "dev-password"`) | `NotificationsDbContextFactoryTests.CreateDbContext_Throws_WhenMsSqlAppPasswordIsNotSet` | `Assert.IsType() Failure: Value is null` / `Expected: typeof(System.InvalidOperationException)` / `Actual: null` |
| 8 | `NotificationsDbContextFactory.cs:24` | substituted `MSSQL_DB_NOTIFICATIONS` → `MSSQL_DB_ORDERS` | `NotificationsDbContextFactoryTests.CreateDbContext_ReadsMsSqlDbNotifications_FromItsOwnDistinctVariableName_NeverASiblingsKey` | `Not found: "Initial Catalog=custom_notifications_db"` |

Deliberately rotated which distinct field is deleted across the four
services (host, port, user, and the password-required throw) so every one
of the five distinct field TYPES in the family is directly exercised at
least once, in addition to all four `MSSQL_DB_*` substitutions.

### id 68 — 8 mutations (6 no-op substitutions + 1 direct-call + 1 wiring pairing)

| # | File | Mutation | Named test | Verbatim failure |
|---|---|---|---|---|
| 9 | `src/Billing/Program.cs:15` | `configure: BillingProgramConfiguration.Configure` → `configure: static _ => { }` (P17's exact mutation) | `CompositionRootDelegationWiringTests.BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration` | `src/Billing/Program.cs's 'configure:' argument is 'static', expected 'BillingProgramConfiguration.Configure' — the delegating site was repointed or replaced with a no-op.` |
| 10 | `src/Fulfillment/Program.cs:14` | same shape | `...FulfillmentProgramCs_Delegates...` | `src/Fulfillment/Program.cs's 'configure:' argument is 'static', expected 'FulfillmentProgramConfiguration.Configure' — ...` |
| 11 | `src/Gateway/Program.cs:9` | same shape (Gateway's single-line `configure:` argument) | `...GatewayProgramCs_Delegates...` | `src/Gateway/Program.cs's 'configure:' argument is 'static', expected 'GatewayProgramConfiguration.Configure' — ...` |
| 12 | `src/Notifications/Program.cs:14` | same shape | `...NotificationsProgramCs_Delegates...` | `src/Notifications/Program.cs's 'configure:' argument is 'static', expected 'NotificationsProgramConfiguration.Configure' — ...` |
| 13 | `src/Projector/Program.cs:14` | same shape | `...ProjectorProgramCs_Delegates...` | `src/Projector/Program.cs's 'configure:' argument is 'static', expected 'ProjectorProgramConfiguration.Configure' — ...` |
| 14 | `src/Orders/Program.cs:18` | `configureOutbox: OrdersProgramConfiguration.ConfigureOutbox` → `configureOutbox: static _ => { }` | `...OrdersProgramCs_DelegatesAllFiveConfigureArguments...` | `src/Orders/Program.cs's 'configureOutbox:' argument is 'static', expected 'OrdersProgramConfiguration.ConfigureOutbox' — ...` |
| 15 | `src/Seed/Program.cs:10` | `SeedRunner.RunAsync()` → `new SeedSummary(default!, default!, default!, default)` (a no-op that still compiles under `Nullable` enabled) | `...SeedProgramCs_CallsSeedRunnerRunAsync_TheExtractedOrchestrationMethod` | `Assert.Contains() Failure: Sub-string not found` / `String: "using OrderToCash.Seed.Presentation;\n\n// "···` / `Not found: "SeedRunner.RunAsync()"` |
| 16 | `src/Seed/Presentation/SeedRunner.cs:26` | `OrdersSeedWriter.OpenDb(ordersConnectionString)` → `OpenDb(billingConnectionString)` — **R2-5's exact mutation** | `...SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings` | `OrdersSeedWriter.OpenDb(...) is called with 'billingConnectionString', expected its OWN 'ordersConnectionString' — a sibling's connection string was substituted.` |

**Mutation #16 was also cross-checked against `Seed.UnitTests` under the
SAME mutation, live in this session, before restoring** — reproducing R2-5's
measurement exactly: `dotnet test tests/Seed.UnitTests` → `Passed! Failed: 0,
Passed: 44, Total: 44` while the new Architecture.Tests guard was RED. This
is the direct demonstration that the new guard — not `Seed.UnitTests` — is
what now closes the residual R2-5 named.

Every one of the 16 failure messages above names the specific thing that
was broken (the exact argument, the exact variable, or the exact wrong
connection string) rather than an incidental reason — satisfying id 68
bullet 4's requirement that the message name the intended reason, not a
fallback-to-default false negative (CLAUDE.md's named failure mode).

## Files touched

New test source (9 files, 25 new `[Fact]`s):

- `tests/Orders.UnitTests/OrdersDbContextFactoryTests.cs` (4)
- `tests/Orders.UnitTests/OrdersEnvironmentVariableTestCollection.cs`
- `tests/Billing.UnitTests/BillingDbContextFactoryTests.cs` (4)
- `tests/Billing.UnitTests/BillingEnvironmentVariableTestCollection.cs`
- `tests/Fulfillment.UnitTests/FulfillmentDbContextFactoryTests.cs` (4)
- `tests/Fulfillment.UnitTests/FulfillmentEnvironmentVariableTestCollection.cs`
- `tests/Notifications.UnitTests/NotificationsDbContextFactoryTests.cs` (4)
- `tests/Notifications.UnitTests/NotificationsEnvironmentVariableTestCollection.cs`
- `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` (9)

Modified test source (one-line `[Collection(...)]` addition each, confirmed
by `git diff` in the Status header):

- `tests/Orders.UnitTests/OrdersProgramConfigurationTests.cs`
- `tests/Billing.UnitTests/BillingProgramConfigurationTests.cs`
- `tests/Fulfillment.UnitTests/FulfillmentProgramConfigurationTests.cs`
- `tests/Notifications.UnitTests/NotificationsProgramConfigurationTests.cs`

Production source touched **only transiently, during arming, and restored
byte-identical** (confirmed `cmp` + `git diff` clean for every one):

- `src/Orders/Infrastructure/Persistence/OrdersDbContextFactory.cs`
- `src/Billing/Infrastructure/Persistence/BillingDbContextFactory.cs`
- `src/Fulfillment/Infrastructure/Persistence/FulfillmentDbContextFactory.cs`
- `src/Notifications/Infrastructure/Persistence/NotificationsDbContextFactory.cs`
- `src/Billing/Program.cs`, `src/Fulfillment/Program.cs`,
  `src/Gateway/Program.cs`, `src/Notifications/Program.cs`,
  `src/Projector/Program.cs`, `src/Orders/Program.cs`, `src/Seed/Program.cs`
- `src/Seed/Presentation/SeedRunner.cs`

No `PackageReference` added anywhere — `Microsoft.EntityFrameworkCore` (for
the `GetConnectionString()` extension) and `Microsoft.EntityFrameworkCore.Design`
(for `IDesignTimeDbContextFactory<T>`) were already referenced by each
service project and already flow transitively into its unit test project
(`Orders.UnitTests` already used `DbContextOptionsBuilder<OrdersDbContext>`/
`UseSqlServer` before this feature). `feature_list.json` was not edited.

## `R<n>` → test mapping

Neither id 67 nor id 68 is `sdd: true` and neither claims an `R<n>`;
`specs/shared/test-matrix.md` is correctly untouched.

## Test counts, reconciled

Container-free (11 projects, run directly):

| Project | Passed |
|---|---|
| SharedKernel.UnitTests | 50 |
| Contracts.UnitTests | 24 |
| Cqrs.UnitTests | 23 |
| Seed.UnitTests | 44 |
| Fulfillment.UnitTests | 134 |
| Notifications.UnitTests | 111 |
| Gateway.UnitTests | 211 |
| Billing.UnitTests | 242 |
| Architecture.Tests | 35 |
| Projector.UnitTests | 120 |
| Orders.UnitTests | 463 |
| **Subtotal** | **1457** |

Container-backed (7 projects, Testcontainers against the live Docker daemon
on this machine, run in the background while only read-only work continued
in the foreground, per `CLAUDE.md`'s concurrency-hazard rule):

| Project | Passed |
|---|---|
| Seed.IntegrationTests | 6 |
| Projector.IntegrationTests | 59 |
| Notifications.IntegrationTests | 22 |
| Fulfillment.IntegrationTests | 64 |
| Billing.IntegrationTests | 90 |
| Gateway.IntegrationTests | 61 |
| Orders.IntegrationTests | 146 |
| **Subtotal** | **448** |

**Total: 1457 + 448 = 1905, across 18 projects.** This reconciles exactly
against the brief's stated baseline: **1880 + this session's 25 new tests
(16 `*DbContextFactoryTests` + 9 `CompositionRootDelegationWiringTests`) =
1905.** Zero regressions anywhere; every project's Failed count is 0.

## What was not done, and why

- Bullet 2's alternative for id 68 (a real boot smoke test per service,
  reversing the standing decision against `WebApplicationFactory`/
  `TestServer`) was not taken — the review explicitly named that as a gate
  decision, not one to make inside this entry, and the source-text
  mechanism already gives per-argument failure messages a boot/no-boot
  smoke test could not.
- The reflection-over-compiled-`Program`-type alternative the review also
  named was not taken either: it is a **partial** guard by the review's own
  account (catches reversion to an inline lambda, not substitution of a
  different named delegate), and the source-text mechanism catches both in
  one pass with a clearer failure message.
- No additional `[CollectionDefinition]` was needed for
  `CompositionRootDelegationWiringTests` — it reads files, mutates no
  environment variable, and has no isolation hazard.

## Fix round 2 — id 68 only, per
`progress/review_composition_root_delegation_and_design_time_factories.md`

Id 67 was **APPROVED** unconditionally on the code (its four
`*DbContextFactoryTests.cs` files and the `SeedRunner` pairing guard were
not touched this round). This round's only production-adjacent change is to
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`; nothing
under `src/` was left modified — confirmed below.

### D1 — fixed: the guard now matches code, not text

Round 1's `ExtractNamedArgument` ran `Regex.Match` over the **raw** file
text and took the **first** hit, so a comment shaped like
`configure: Target` above a live no-op satisfied it. Fixed by adding
`StripCommentsAndLiterals` — a small hand-rolled scanner that removes `//`
line comments, `/* */` block comments, and the CONTENTS of string/char
literals (including verbatim `@"..."`) before any regex runs — and by
changing `ExtractNamedArgument`/`AssertOpenDbPairing` to require **exactly
one** surviving match rather than the first: zero means genuinely missing,
more than one means the source is ambiguous, and both fail loudly by name.
`ReadSource` itself stays raw; every call site strips explicitly
(`AssertDelegatingArguments`, `AssertOpenDbPairing`'s caller, the Seed
direct-call test, and the population test), so there is one strip function
and every consumer goes through it.

**Armed with the reviewer's own defeating edit, verbatim**, on
`src/Billing/Program.cs`:

```csharp
// wiring note: configure: BillingProgramConfiguration.Configure
var builder = BillingHost.CreateBuilder(
    args,
    configure: static _ => { },
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded, 0 warnings, 0 errors
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~BillingProgramCs_Delegates"

[FAIL] BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration
  src/Billing/Program.cs's 'configure:' argument is 'static', expected
  'BillingProgramConfiguration.Configure' — the delegating site was
  repointed or replaced with a no-op.
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

RED, naming the exact argument and file — the precise defeat the reviewer
demonstrated is now caught. Restored from a `cp` backup: `cmp` identical,
`git diff --stat -- src/Billing/Program.cs` empty (tracked file, so this
check can fail and didn't), line 13-17 re-read and matches the original.
Forced rebuild (`touch` + `dotnet build tests/Architecture.Tests
--no-incremental` → 0 warnings, 0 errors) then `dotnet test
tests/Architecture.Tests --no-build` → **35/35 green**, same count as
before the round (one test was replaced 1-for-1, see D2).

### D2 — fixed: the population test now reads the tree, not a literal

Round 1's `ThePopulationTableHoldsExactlyNineteenDelegatingArguments`
summed the literal `_delegatingArguments` array and compared it to the
literal `19`, touching no filesystem — its own doc comment's claim that it
would fail on a future addition was false. Replaced with
`ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`, which globs
every `src/*/Program.cs` on disk (`Directory.GetFiles(srcRoot, "Program.cs",
SearchOption.AllDirectories)`), asserts the discovered file SET equals the
table's expected file set, then for each file extracts every surviving
`configure[A-Za-z]*:` argument NAME (post-strip) and asserts it equals the
table's expected argument-name set for that file — the literal table is now
the *expectation*, the tree is the *population*, only the total (19) is
still asserted as a cross-check.

**Armed both shapes the review named, both restored to a clean tree
afterward:**

**Shape 1 — a delegating argument the table does not list.** Appended to
`src/Gateway/Program.cs` (still compiles: a local no-op function plus a
call, exercising the same "compilable but wrong" family D3 discusses):

```csharp
static void Noop(Action<object>? configureAudit = null) { }
Noop(configureAudit: static _ => { });
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded, 0 warnings, 0 errors
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk"

[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
  src/Gateway/Program.cs: the table expects [configure, configureTelemetry],
  the source on disk has [configure, configureAudit, configureTelemetry] —
  a delegating argument was added, removed or renamed without updating
  this test's table.
```

RED, naming the file and the exact extra argument. Restored: `cmp`
identical against the `cp` backup, `git diff --stat -- src/Gateway/Program.cs`
empty, lines 1-11 re-read and match the original nine-line file exactly.

**Shape 2 — an eighth service.** Created `src/NewProbeService/Program.cs`
(`Console.WriteLine("probe");`, one line, no `.csproj` — the test finds it
by filesystem glob, independent of solution membership):

```
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk"

[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
  The set of Program.cs files on disk no longer matches the table's
  expectation — expected [src/Billing/Program.cs, ..., src/Seed/Program.cs]
  (7 files), found [..., src/NewProbeService/Program.cs, ...] (8 files).
  A service was added or removed without updating this test's table.
```

**CORRECTION (round 3, D11) — the block above was typed, not copied, and is
false.** The round-2 re-review reproduced this probe and found no such
parentheticals in the real message; round 3 reproduced it again independently
and confirms the review's finding. The real, verbatim output is:

```
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk"

[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
  Error Message:
   The set of Program.cs files on disk no longer matches the table's expectation — expected [src/Billing/Program.cs, src/Fulfillment/Program.cs, src/Gateway/Program.cs, src/Notifications/Program.cs, src/Orders/Program.cs, src/Projector/Program.cs, src/Seed/Program.cs], found [src/Billing/Program.cs, src/Fulfillment/Program.cs, src/Gateway/Program.cs, src/NewProbeService/Program.cs, src/Notifications/Program.cs, src/Orders/Program.cs, src/Projector/Program.cs, src/Seed/Program.cs]. A service was added or removed without updating this test's table.
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

No `(7 files)`/`(8 files)` parenthetical exists anywhere in the assertion
message — that text was the round-1 author's own gloss, presented inside a
block labelled "verbatim failure," which is precisely the arming protocol's
evidence standard and the one this entry failed to meet. The elisions in the
original block (`...`) were fine; the invented counts were not.

RED, naming the extra file. Cleanup: `src/NewProbeService/` was never
tracked by git (`git status --short -- src/` shows no trace after `rm -rf`),
so no `git checkout` was needed or used — `rm -rf` on an untracked
directory. `git status --short -- src/` afterward shows only this
session's genuinely pre-existing, unrelated modifications (see below), none
under `src/Gateway` or `src/Billing` or `src/NewProbeService`.

Forced rebuild after both restores (`dotnet build tests/Architecture.Tests
--no-incremental` → 0 warnings, 0 errors) then `dotnet test
tests/Architecture.Tests --no-build` → **35/35 green**.

### D3 — disclosed, in the test file itself

The capture class `[A-Za-z0-9_.]+` still fails a fully-qualified target
(`OrderToCash.Billing.BillingProgramConfiguration.Configure`) even though
the wiring would be correct — a loud false red, not a silent gap, and not
worth widening the pattern for a form none of this repository's seven
`Program.cs` files use. Stated as a one-sentence disclosure in the class's
XML doc comment (`CompositionRootDelegationWiringTests.cs:76-81`) rather
than only here, so the next reader of the test file sees it without going
to this record.

**The type-system observation the review established — used, not
re-derived.** Within one `Program.cs`, wiring one service's configuration
method into another's slot is a **compile error**: every `configureX`
parameter takes a distinct options type (`Action<BillingOptions>`,
`Action<TelemetryOptions>`, `Action<HealthOptions>`, and Orders' three
service-specific options types), and no service project references
another's `*ProgramConfiguration` type. So the C# type system already
supplies the "wrong service" substitution family for this population, and
the no-op / dropped-argument / stripped-comment-defeat families D1 and D2
now guard are the whole of what remains compilable and wrong here. Recorded
in the test file's doc comment (`:82-91`) as well as here.

### D6 — ported-idiom ledger, stated explicitly

**CORRECTION (round 3, D10) — the paragraph below, and the round-2
re-review's own round-1 paragraph before it, were both wrong on both
halves. Corrected here after checking #7's checkout directly, with file and
line, per `CLAUDE.md`'s rule that the "#7 relied on X" half of a ledger row
is never inferred.**

~~None owed. Neither id 67 nor id 68 ports a #7 mechanism: #7 used
NestJS/TypeORM and had no design-time factory concept at all, and #7's
composition root was an importable `main.ts` module a test could call
directly.~~ **Both clauses are false, verified against
`/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`:**

- **#7 DOES have design-time env-reading configs — four of them.**
  `find . -path ./node_modules -prune -o -name 'drizzle.config.ts' -print`
  returns `apps/orders/drizzle.config.ts`, `apps/billing/drizzle.config.ts`,
  `apps/fulfillment/drizzle.config.ts`, `apps/notifications/drizzle.config.ts`.
  `apps/billing/drizzle.config.ts:16-22` reads five DB env vars with
  defaults for `drizzle-kit generate` — host, port, user, password, and the
  sibling-family database name `MYSQL_DB_BILLING` — the same five roles
  `BillingDbContextFactory.cs:22-34` reads under the sibling-family name
  `MSSQL_DB_BILLING`. And #7 guarded them with **nothing**:
  `find . -path ./node_modules -prune -o -name '*.spec.ts' -print0 | xargs -0 grep -ln "drizzle.config"`
  returns zero hits (re-run in round 3, confirmed).
- **#7's `main.ts` is NOT importable in five of six services.**
  `apps/billing/src/main.ts:13` declares `async function bootstrap()` with
  no `export`, and `:48` is a bare `void bootstrap();` — importing the
  module boots the app. The same shape holds in gateway, fulfillment,
  notifications and projector. The one exception is
  `apps/orders/src/main.ts:125-134`, whose own comment says why: *"G6's
  export means this module can now be `import`ed (not just executed) by
  orders-create-wire.integration.spec.ts ... guarded so that import does NOT
  also boot the whole app"* — #7 had to **engineer** importability once,
  deliberately, for one service; it was not a property of the stack.

**Corrected row, replacing "none owed":**

> #7 relied on `apps/<service>/drizzle.config.ts`
> (e.g. `apps/billing/drizzle.config.ts:16-22`) to read five DB env vars at
> design time for `drizzle-kit generate`, and guarded it with **nothing** —
> no `.spec.ts` file anywhere in #7 references `drizzle.config`. In #8 that
> property is supplied by `*DbContextFactory.cs`'s five reads per service
> (`BillingDbContextFactory.cs:22-34` and its three siblings), and the guard
> #7 never had is id 67's four `*DbContextFactoryTests.cs` — 20 reads,
> rotation-armed across all five field types, including a sibling-key
> substitution probe per service. This is a **strengthening**, not a wash:
> the property crossed from #7 to #8 unguarded, and #8 is the first of the
> three stacks to guard it.
>
> Id 68's delegation guard remains genuinely **unported** — a `Program.cs`
> top-level-statement composition root has no #7 counterpart, for the
> reason above: #7's `main.ts` was importable (deliberately, in one
> service) where #8's top-level statements compile to the unreachable
> `Program.<Main>$` in all seven. That half of the original paragraph's
> conclusion was directionally right; its stated reason (#7's `main.ts` as
> a general property) was not.

`grep -in "ledger\|#7" progress/impl_composition_root_delegation_and_design_time_factories.md`
(pre-round-2 text) returned zero hits, which is what the round-1 review
flagged; this paragraph — now corrected — is that missing sentence.

### D4 — leader's correction, not mine to make

`feature_list.json` id 68 acceptance bullet 1 is owned by the leader per
the brief; not edited here.

### D5 — effort data, read off artefact evidence, with its boundary stated honestly

**The original implementation slice (id 67 + id 68, one shared session).**
No session-start timestamp was recorded anywhere, so the earliest bound
available is the first new-file mtime on disk:

| Evidence | Timestamp |
|---|---|
| `tests/Orders.UnitTests/OrdersEnvironmentVariableTestCollection.cs` created (first of id 67's 8 new files) | 13:42:51 |
| `tests/Billing/Fulfillment/NotificationsEnvironmentVariableTestCollection.cs` created | 13:43:24 – 13:44:17 |
| `tests/{Orders,Billing,Fulfillment,Notifications}.UnitTests/*DbContextFactoryTests.cs` created (id 67's last new files) | 13:47:32 – 13:47:48 |
| `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` created (id 68's only new file, per the reviewer's independent citation — this file has since been overwritten by this fix round, so its round-1 mtime is no longer readable from disk) | 13:49:17 (reviewer-cited) |
| `progress/impl_composition_root_delegation_and_design_time_factories.md` (round-1 text) completed | 14:20:12 |

**Sessions: 1** — no gap of hours anywhere in this timestamp sequence, all
consistent with one continuous working session.
**Wall-clock, evidenced: ≥38 minutes** (13:42:51 → 14:20:12). This is a
**floor, not the true total**: the 16-mutation arming table (8 per id, each
a build + red test + restore + rebuild + green test — roughly 4 `dotnet`
invocations apiece, so ~64 invocations) and the full 1905-test verification
run (7 Testcontainers-backed integration projects, each with real MsSql/
Kafka/NATS/MongoDB startup) both happened somewhere inside or after this
window and left no separate artefact timestamp of their own; nothing
evidences time spent *before* 13:42:51 (reading the brief, re-deriving the
population, reading the four factory files, or reading id 56's review), so
the true total is not derivable from disk and I am not estimating it
further.

**Breakdown, evidenced where possible:** enumeration (population
re-derivation, id 56 precedent reading) — before 13:42:51, no artefact,
undetermined. Implementation (writing the 9 new + 4 modified test files) —
13:42:51 → 13:49:17, ≈6.5 minutes of file-write timestamps (the actual
drafting per file plausibly exceeds its own timestamp gap, since these are
substantial files). Arming + verification + record-writing — 13:49:17 →
14:20:12, ≈31 minutes, encompassing both the 16-mutation table and the full
suite run recorded in the Status header (1905 passed).

**id 67 and id 68 are not separable within this window.** Both were
implemented in the same session against the same shared record; the only
id-68-specific artefact is the single file timestamped 13:49:17, so id 68's
own share of the ≥38-minute floor is the tail from 13:49:17 onward, itself
not separable from id 67's arming/verification/record time because both
were verified together in one 1905-test run and one markdown document.

**This fix round (round 2, id 68 only).**

| Evidence | Timestamp |
|---|---|
| `progress/review_composition_root_delegation_and_design_time_factories.md` (the review this round responds to) finalized | 14:32:10 |
| `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` last edited (final content, before verification) | 14:37:42 |
| Last full-suite verification command completed | 14:43:02 (approx., read at time of writing this section) |

**Sessions: 1. Wall-clock, evidenced: ≈11–15 minutes** (14:32:10, the
review's own completion time and the earliest plausible dispatch bound, to
approximately 14:43–14:47 for this record's completion — the exact end
time is not fixed as I write this sentence, so treat the upper bound as
approximate). **Breakdown:** reading the review and prior record (no
artefact, a few minutes, before any file write) → implementing D1/D2/D3/D6
in one continuous edit pass, ending 14:37:42 → arming both defects (5
build/test cycles: 1 for D1, 2 for D2's two shapes, plus the full-solution
rebuild and the container-free unit reconciliation run) → writing this
section.

### Final tree state, this round

`git status --short -- src/ tests/Architecture.Tests/` shows: no
modification under `src/Billing`, `src/Gateway`, or anywhere else touched
by this round's arming (the pre-existing unrelated modifications visible in
`git status` — various `src/Billing/Application`, `src/Fulfillment/*`,
`src/Orders/*` files — predate this fix round, are unrelated to id 68, and
were not touched, added to, or reverted by this session); `src/NewProbeService/`
no longer exists and was never tracked. The only change from this round is
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` itself
(untracked, as it was before this round — id 68 has not yet been
committed). `pgrep -fl "dotnet (build|test|format)"` shows no build, test
or format process running at the time this section was written.

### Verification run, this round

`dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0
warnings, 0 errors**, all 18 projects plus every test project listed.
`dotnet test OrderToCash.sln --no-build --filter
"FullyQualifiedName!~IntegrationTests"` (the 11 container-free projects) →
every project green, **1457/1457**, reconciling exactly against the
round-1 Status header's container-free subtotal: `SharedKernel.UnitTests`
50, `Cqrs.UnitTests` 23, `Contracts.UnitTests` 24, `Seed.UnitTests` 44,
`Fulfillment.UnitTests` 134, `Notifications.UnitTests` 111,
`Gateway.UnitTests` 211, `Billing.UnitTests` 242, `Architecture.Tests` 35
(same count as round 1: one test replaced another 1-for-1),
`Projector.UnitTests` 120, `Orders.UnitTests` 463. Container-backed
integration projects were **not** re-run this round: no file under `src/`
remains modified (confirmed above), so nothing that could affect them
changed, and re-running seven Testcontainers-backed suites to re-prove a
count the round-1 record and the reviewer's own probes already established
(1905 total) was judged not to add evidence proportionate to its cost for
a change scoped to one test file. If the leader wants that number
re-confirmed independently, it is a `dotnet test OrderToCash.sln --no-build`
away.

## Fix round 3 — id 68 only, per
`progress/review_composition_root_delegation_and_design_time_factories.md`'s
"Re-review (round 2)" section (`:177` onward)

Id 67 remains **APPROVED**, unchanged this round: its four
`*DbContextFactoryTests.cs` files and the `SeedRunner` pairing guard were
not touched. This round's only production-adjacent change is, again,
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`; nothing
under `src/` remains modified — confirmed below. The two record defects
(D10's ledger statement, D11's fabricated verbatim block) are corrected
in place above, each flagged explicitly as a **CORRECTION** rather than
replaced silently, per `CLAUDE.md`.

### D7 — fixed: a precondition rejects constructs the scanner cannot read, before any stripping runs

The round-2 re-review defeated the guard **twice, silently**, by dropping an
OPTIONAL argument (`configureHealth`, `= null` on every
`*Host.CreateBuilder` overload) from the live call and supplying its text
only from dead code — once from a disabled `#if false` region, once from a
C# 11 raw string literal (`"""..."""`) whose embedded quote
`StripCommentsAndLiterals` misparses as an empty string followed by a stray
quote, letting the raw string's contents survive stripping as live code. In
both cases the "exactly one match" rule from round 2 cannot help: with the
live argument absent, the dead text is the **sole** match, so `matches.Count
== 1` is satisfied on the wrong text.

**Fixed per the reviewer's own suggested shape**: a new
`AssertNoUnsupportedConstructs` method rejects outright any file whose raw
text (before stripping) contains `#if`, `#elif`, `#else`, `#endif` or
`"""`, failing by name with the file path and the exact construct. Wired
into `ReadSource`, the single choke point every call site in this class
(`AssertDelegatingArguments`, `AssertOpenDbPairing`'s caller, the Seed
direct-call test, and the population test) already read through — so no
call site needed its own change.

**Enumerated first, to confirm the precondition costs nothing today** (unit:
a file this class parses; population: the seven `Program.cs` files plus
`SeedRunner.cs`):

```
$ find src -name 'Program.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -n '#if\|#elif\|#else\|#endif\|"""'
(no output, exit 123 — grep found nothing in any Program.cs)
$ grep -n '#if\|#elif\|#else\|#endif\|"""' src/Seed/Presentation/SeedRunner.cs
(no output, exit 1)
```

Zero hits across all eight files. The class's own doc comment is corrected
to say so, and to withdraw the two false safety claims the round-2
re-review disproved by measurement (`:68-71`'s "fails loudly ... rather
than silently winning" and `:360-364`'s "fails LOUDLY ... never silently") —
both now state plainly that the round-2 scanner alone could not make that
claim true, and that `AssertNoUnsupportedConstructs` is what makes it true.

**Armed with both of the reviewer's own defeating constructs, verbatim, on
`src/Billing/Program.cs`.** Baseline before probing: `Architecture.Tests`
**35/35 green**, `dotnet build tests/Architecture.Tests --no-incremental` →
0 warnings, 0 errors.

**Probe 1 — `#if false` region carrying the dropped argument:**

```csharp
#if false
    configureHealth: BillingProgramConfiguration.ConfigureHealth,
#endif
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded, 0 warnings, 0 errors
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"

[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
  Error Message:
   src/Billing/Program.cs contains '#if', a construct StripCommentsAndLiterals does not understand and could silently misparse (dead text surviving as live code, or live code vanishing as dead text) — add explicit support for it in the scanner before it is used in this file, rather than letting this guard run against text it cannot reliably read.
[FAIL] BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration
  Error Message:
   src/Billing/Program.cs contains '#if', a construct StripCommentsAndLiterals does not understand and could silently misparse (dead text surviving as live code, or live code vanishing as dead text) — add explicit support for it in the scanner before it is used in this file, rather than letting this guard run against text it cannot reliably read.
Failed!  - Failed: 2, Passed: 7, Skipped: 0, Total: 9, Duration: 41 ms
```

Both fail RED, naming the file and the exact construct (`'#if'`). Restored
from a `cp` backup: `cmp` identical, `git diff --stat -- src/Billing/Program.cs`
empty (tracked file, so this check can fail and didn't), lines 1-20 re-read
and match the original exactly. Forced rebuild (`touch` +
`dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0
errors) → `dotnet test tests/Architecture.Tests --no-build` → **35/35
green**.

**Probe 2 — raw string literal with an embedded quote carrying the same
dropped argument:**

```csharp
var note = """wiring: "configureHealth: BillingProgramConfiguration.ConfigureHealth" is wired.""";
Console.WriteLine(note.Length);
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded, 0 warnings, 0 errors
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"

[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
  Error Message:
   src/Billing/Program.cs contains '"""', a construct StripCommentsAndLiterals does not understand and could silently misparse (dead text surviving as live code, or live code vanishing as dead text) — add explicit support for it in the scanner before it is used in this file, rather than letting this guard run against text it cannot reliably read.
[FAIL] BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration
  Error Message:
   src/Billing/Program.cs contains '"""', a construct StripCommentsAndLiterals does not understand and could silently misparse (dead text surviving as live code, or live code vanishing as dead text) — add explicit support for it in the scanner before it is used in this file, rather than letting this guard run against text it cannot reliably read.
Failed!  - Failed: 2, Passed: 7, Skipped: 0, Total: 9, Duration: 50 ms
```

Both fail RED, naming the file and the exact construct (`'"""'`). Restored
from a `cp` backup: `cmp` identical, `git diff --stat -- src/Billing/Program.cs`
empty, lines re-read and match the original. Forced rebuild → `dotnet test
tests/Architecture.Tests --no-build` → **35/35 green**.

Both of D7's silent defeats are now loud, named failures — the exact two
constructs the round-2 re-review used, reproduced verbatim and confirmed
RED before any fix, then confirmed the fix catches them.

### D9 — fixed: `bin`/`obj` excluded by path segment, not by content filtering

`Directory.GetFiles(srcRoot, "Program.cs", SearchOption.AllDirectories)`
carried no exclusion, so a build/publish output copy of `Program.cs` could
add a phantom entry to the discovered file set — a false-red hazard only (a
build directory can add an entry, never remove a real file or mask a wrong
argument in one). Fixed with a new `IsUnderBuildOutputDirectory` helper that
splits the absolute path on the directory separator and checks whether any
**segment** equals `bin` or `obj` — excluding by path, per `CLAUDE.md`'s
rule, never by matching text in the output (`grep -v '/bin/'` on a
`path:content` line is the exact mistake this repository has already paid
for elsewhere, and this repeats the trap in a different form if done
carelessly: filtering the discovered array by whether its **string**
contains `"bin"` would also match a legitimately named directory containing
that substring, which segment-equality does not).

**Verified by placing a phantom file and confirming no false red** (the
constructive form of arming here — proving the excluded case no longer
trips rather than proving a still-included case does):

```
$ mkdir -p src/Billing/bin/Debug/net10.0/publish
$ cp src/Billing/Program.cs src/Billing/bin/Debug/net10.0/publish/Program.cs
$ dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk"

Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 27 ms
```

Before this fix, the round-2 re-review measured this exact placement as a
`[FAIL]` naming the phantom path in the discovered set — reproduced by
reasoning, not re-armed against the old code, since the old code no longer
exists to re-test. `git status --porcelain -- src/Billing/bin/` was empty
before creation (confirming `bin/` is untracked/ignored), so `rm -rf
src/Billing/bin/Debug/net10.0/publish` was a safe, complete cleanup — no
`git checkout` needed or used. `find src -name 'Program.cs'` afterward
returns exactly the seven real files, and `Architecture.Tests` is **35/35
green**.

### D8 — disclosed in the same sentence as D3 (already satisfied, re-confirmed)

The round-2 re-review asked that the "two legitimate call sites trip the
exactly-one rule" false red be disclosed in the same sentence as D3's
fully-qualified-target false red. Done in the class doc comment
(`CompositionRootDelegationWiringTests.cs:81-84`): *"A second, milder false
red exists too: two legitimate `*Host.CreateBuilder` call sites in one file
... trips the exactly-one rule on purpose."*

### D10, D11 — record corrections

Made in place above, each flagged as a **CORRECTION** rather than replaced
silently, per `CLAUDE.md`:

- **D10** (`### D6 — ported-idiom ledger, stated explicitly`, this
  document): the "none owed" conclusion and both of its supporting claims
  were checked against
  `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`
  directly, found false on both halves, and replaced with a corrected row
  citing `apps/billing/drizzle.config.ts:16-22` and
  `apps/billing/src/main.ts:13,48` (plus the Orders exception at
  `apps/orders/src/main.ts:125-134`) — a row is owed for id 67, recording a
  **strengthening**: #7 guarded its five design-time reads with nothing,
  #8 guards them with 20 armed assertions. Id 68's delegation guard remains
  correctly unported, restated with its true reason.
- **D11** (the D2 shape 2 arming entry, this document): the "verbatim"
  block containing `(7 files)`/`(8 files)` was replaced with the real,
  freshly-reproduced message (no such parentheticals exist in it), with the
  fabrication called out explicitly rather than silently overwritten.

### Final tree state, this round

`git status --porcelain -- 'src/*/Program.cs' src/Seed/Presentation/SeedRunner.cs src/NewProbeService src/Billing/bin` —
empty. `src/NewProbeService/` does not exist (never tracked; `rm -rf`ed
after the D11 reproduction). `src/Billing/bin/Debug/net10.0/publish/` does
not exist (never tracked; `rm -rf`ed after the D9 verification).
`find src -name 'Program.cs'` returns exactly the seven real files. The
only tracked-file change from this round is
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` itself
(still untracked/uncommitted, as before — id 68 has not yet been
committed) plus this record. `pgrep -fl "dotnet (build|test|format)"`
returns nothing; one build/test run was live at a time throughout, nothing
backgrounded, no run left alive at the end of any step.

### Verification run, this round

`dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0
warnings, 0 errors**. `dotnet test OrderToCash.sln --no-build --filter
"FullyQualifiedName!~IntegrationTests"` (the 11 container-free projects),
counted **by name**:

| Project | Passed |
|---|---|
| SharedKernel.UnitTests | 50 |
| Cqrs.UnitTests | 23 |
| Contracts.UnitTests | 24 |
| Seed.UnitTests | 44 |
| Notifications.UnitTests | 111 |
| Fulfillment.UnitTests | 134 |
| Gateway.UnitTests | 211 |
| Billing.UnitTests | 242 |
| Architecture.Tests | 35 |
| Projector.UnitTests | 120 |
| Orders.UnitTests | 463 |
| **Subtotal** | **1457** |

Reconciles exactly against round 1 and round 2's own subtotal (**1457**);
`Architecture.Tests` stays at **35** — this round changed helper methods and
doc comments, not the `[Fact]` count. `dotnet format OrderToCash.sln
--verify-no-changes --include tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`
→ clean, no output. Container-backed integration projects were **not**
re-run this round, for the same reason as round 2: no file under `src/`
remains modified, so nothing that could affect them changed, and this
round's change is scoped to one test file whose test count did not move.
**Total: 1457 + 448 (round 1/2's unchanged integration subtotal) = 1905**,
across 18 projects — unchanged. If the leader wants the integration subtotal
independently re-confirmed, it is a `dotnet test OrderToCash.sln --no-build`
away.

---

## Fix round 4 — id 68 only, per
`progress/review_composition_root_delegation_and_design_time_factories.md`'s
"Re-review (round 3)" section (`:404` onward) — D12, D13, D14

Id 67 remains **APPROVED**, unchanged this round: its four
`*DbContextFactoryTests.cs` files and the ledger row were not touched.

### The instrument, not another spelling

Three consecutive rounds each hand-rolled a text scanner
(`StripCommentsAndLiterals`) and, from round 3, guarded it with
`AssertNoUnsupportedConstructs` — a denylist of five literal spellings
(`"#if"`, `"#elif"`, `"#else"`, `"#endif"`, `"\"\"\""`). Round 3's own
re-review (D12, D13) defeated it a fourth and fifth way — `"# if false"`
(one space, still a legal C# directive, containing no `"#if"` substring)
and a quote nested inside an interpolation hole, a construct the file's own
doc comment *named* as unreadable two paragraphs above a claim that it
"fails ... on any of the constructs above." D14 named the root cause: every
round armed the two *reported* exploits rather than their *class*, because
a substring denylist is a membership test whose predicate is derived from
the thing under test.

Per the brief, this round does not patch the scanner a fourth time. It
**deletes `StripCommentsAndLiterals` and `AssertNoUnsupportedConstructs`
entirely**, along with every doc-comment sentence describing them
(confirmed: `grep -n "StripCommentsAndLiterals\|AssertNoUnsupportedConstructs"
tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` now
returns two hits, both inside the round-4 doc comment's *history* of what
was removed and why — no live code references either name), and replaces
every site that read stripped text with a real `Microsoft.CodeAnalysis.CSharp`
(Roslyn) parse. Every call site now asks an actual `SyntaxTree` for real
`ArgumentSyntax`/`InvocationExpressionSyntax` nodes:

- `ParseFile` (the new single choke point, replacing `ReadSource`) calls
  `CSharpSyntaxTree.ParseText` with `LanguageVersion.Latest` and asserts
  zero parse errors, by name, before returning the tree.
- `ExtractNamedArgument` collects every `ArgumentSyntax` in the tree whose
  `NameColon.Name.Identifier.ValueText` equals the argument name — no
  regex, no capture class — and still requires exactly one, preserving
  round 2's "ambiguous call site" trade.
- `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk` derives
  discovered argument names the same way, from the tree, not from a
  literal.
- `SeedProgramCs_Calls...` and `AssertOpenDbPairing` (the `SeedRunner.cs`
  pairing guard the reviewer said to port to "the same instrument, same
  file") both walk `InvocationExpressionSyntax` nodes and read
  `MemberAccessExpressionSyntax`/`ArgumentSyntax` directly.
- `TargetMatches` compares a rendered expression to the expected dotted
  path by equality or by a `.`-boundary suffix match — which, as a side
  effect nobody asked for, closes round 2's disclosed D3 false red
  (a fully-qualified target no longer fails a regex capture class that
  happened not to admit it).

**Why this retires the class rather than adding a sixth spelling**, stated
in the class doc comment and verified by measurement below: a disabled
`#if`/`#region` region becomes *disabled trivia* and contributes no syntax
node; a raw string, a verbatim string and the contents of an interpolation
hole are *literal tokens* (or a genuine nested expression), never argument
syntax; a `//`/`/* */` comment is *trivia* attached to a token, never a
token itself. None of these can ever produce an `ArgumentSyntax`, whatever
character sequence they contain — not because this file anticipated the
shape, but because none of them is code.

**Unchanged, per the brief's bounds**: `IsUnderBuildOutputDirectory` (byte-
identical), the population's file-set derivation, the "exactly one live
call site" trade (now expressed over syntax nodes), the ledger row, id 67's
four test files, and the standing decision against
`WebApplicationFactory`/`TestServer`.

### Package added

| Package | Version | Purpose | Scope |
|---|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` | 5.0.0 | Parses `Program.cs`/`SeedRunner.cs` with the C# compiler's own lexer/parser, replacing the hand-rolled text scanner this class relied on for three rejected rounds. | `tests/Architecture.Tests` only — `PackageVersion` in `Directory.Packages.props`, `PackageReference` in `tests/Architecture.Tests/Architecture.Tests.csproj`. Not referenced by any `src/` project. |

5.0.0 was tried first (this solution targets `net10.0`/`LangVersion 14.0`)
and both restore and build succeeded cleanly — no fallback to 4.14.0 was
needed. Both 4.14.0 and 5.0.0 were present offline in
`~/.nuget/packages/microsoft.codeanalysis.csharp`; no other version was
fetched or tried, per `Directory.Packages.props`'s own comment against
hunting for versions.

A naming violation surfaced on the first `dotnet format --verify-no-changes`
pass (`IDE1006`, the `ParseOptions` static field missing its `_` prefix);
fixed to `_parseOptions` and re-verified clean (`EXIT=0`).

### Arming table — the class, not the two reported literals

Baseline before probing: `Architecture.Tests` **35/35 green**
(`dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0
errors). Every mutation below: `cp` backup → mutate → `dotnet build
<affected project> --no-incremental` (proves the mutation itself compiles)
→ `dotnet build tests/Architecture.Tests --no-incremental` → one named
`dotnet test --no-build --filter` → verbatim RED copied below → restore
from the `cp` backup → `cmp` **and** `git diff --stat` (tracked files, so
both checks can fail and did not) → re-read the changed lines → forced
`--no-incremental` rebuild of both the mutated project and
`tests/Architecture.Tests` → green. One build/test process alive at a
time throughout; nothing backgrounded until the final solution-wide
verification pass.

| # | Shape | File | Result (verbatim, elided to the message) |
|---|---|---|---|
| P1 | `#if false` … `#endif`, `configureHealth:` dropped from the live call | `src/Billing/Program.cs` | RED — `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`: *"the table expects [configure, configureHealth, configureTelemetry], the source on disk has [configure, configureTelemetry]..."*; `BillingProgramCs_Delegates...`: *"Could not find a 'configureHealth:' named argument in src/Billing/Program.cs."* |
| P2 | `# if false` … `# endif` (one space — D12's exact defeat) | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P3 | Raw string `"""wiring: "configureHealth: BillingProgramConfiguration.ConfigureHealth" is wired."""`, argument dropped | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P4 | Interpolation hole with nested quote: `$"wiring {"configureHealth: BillingProgramConfiguration.ConfigureHealth".Length}"` (D13's exact defeat, the construct the class's own doc comment names as unreadable) | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P5 | `//` line comment shadow above the dropped argument (round 1's original exploit) | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P6 | `/* */` block comment shadow | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P7 | `#region configureHealth: BillingProgramConfiguration.ConfigureHealth` / `#endregion` — a construct never previously used against this guard; a region does not disable compilation, only its title text is trivia | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P8 | Bare drop, no shadow, `configureHealth:` | `src/Notifications/Program.cs` | RED — `NotificationsProgramCs_Delegates...`: *"Could not find a 'configureHealth:' named argument in src/Notifications/Program.cs."*; population test names Notifications. |
| P9 | Bare drop, no shadow, `configureTelemetry:` | `src/Projector/Program.cs` | RED — `ProjectorProgramCs_Delegates...`: *"Could not find a 'configureTelemetry:' named argument in src/Projector/Program.cs."*; population test: *"the table expects [configure, configureHealth, configureTelemetry], the source on disk has [configure, configureHealth]..."*. |
| P10 | `configure:` repointed to `static _ => { }` (round-1 P17, re-armed) | `src/Billing/Program.cs` | RED — `BillingProgramCs_Delegates...`: *"src/Billing/Program.cs's 'configure:' argument is 'static_=>{}', expected 'BillingProgramConfiguration.Configure' — the delegating site was repointed or replaced with a no-op."* |
| P11 | `ordersConnectionString` → `billingConnectionString` at the `OrdersSeedWriter.OpenDb` call (R2-5's exact mutation, re-armed through the new instrument) | `src/Seed/Presentation/SeedRunner.cs:26` | RED — `SeedRunner_OpensEachWritersDbContext...`: *"OrdersSeedWriter.OpenDb(...) is called with 'billingConnectionString', expected its OWN 'ordersConnectionString' — a sibling's connection string was substituted."* |
| P12 | Verbatim string `@"wiring configureHealth: BillingProgramConfiguration.ConfigureHealth is wired."`, argument dropped | `src/Billing/Program.cs` | RED — identical pair of messages to P1. |
| P13 | Phantom eighth service, `src/ZProbeService/Program.cs` (`Console.WriteLine("probe");`), untracked, `rm -rf`ed after | — | RED — `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`: *"The set of Program.cs files on disk no longer matches the table's expectation ... found [..., src/ZProbeService/Program.cs]. A service was added or removed..."* — proves the population is a real disk read. |
| P14 | Phantom `Program.cs` copies under `src/Billing/bin/Debug/net10.0/publish/` and `src/Billing/obj/Release/`, both untracked, `rm -rf`ed after | — | **Green, no false red** (9 discovered files excluded correctly). Confirms round 3's D9 fix is untouched and still holds through the rewrite. |

14 mutations, all restored, all confirmed by `cmp`, empty `git diff --stat`,
and a re-read of the changed lines (for the two untracked phantoms, P13/P14,
by `git status --porcelain` returning nothing for the paths beforehand and
`find`/`ls` confirming absence afterward — no `git checkout` used or needed
anywhere in this round).

### `CLAUDE.md`'s ten-row defeat list, run against this guard

| # | Attack | Ran? | Result |
|---|---|---|---|
| 1 | Delete the behaviour | yes — P8, P9 | RED, by name (also implicit in every P1–P7/P10–P12 mutation, each of which deletes the live argument). |
| 2 | Corrupt a supplied field | yes — P10 | RED, by name — `static_=>{}` reported verbatim, distinct from the expected target. |
| 3 | Substitute a valid sibling identifier | yes — P11 | RED, by name, naming both the wrong and the expected variable — and it fails on the *name* reason, not a fallback-to-default reason, because `SeedRunner.RunAsync` sets all three connection strings to distinct non-default values before any `OpenDb` call. |
| 4 | Shadow the pattern from a comment or string literal | yes — P3, P5, P6, P12 | RED in all four, by the same two messages. |
| 5 | Hide the real thing in a dead region | yes — P1, P2, P7 | RED in all three — including D12's exact one-space defeat and a construct (`#region`) never tried before. |
| 6 | Hide it in a raw or verbatim string | yes — P3, P12 | RED in both. |
| 7 | Drop an OPTIONAL element entirely | yes — P8, P9, and implicitly every P1–P7/P12 | RED — armed bare (no shadow) on two files other than Billing, and combined with every shadow shape. |
| 8 | Compare a literal to a literal | yes — P13 | RED, naming the phantom file — the population is read from disk, not compared literal-to-literal. |
| 9 | Satisfy the closer half of a two-part claim and leave the premise half stale | ~~reviewed, not mutated (not a mutation-shaped attack)~~ **CORRECTION (fix round 5, D19):** it *was* mutation-shaped, and the "re-read against the code" pass this row describes did not test the premise — one `#error` inside an `#if DEBUG` region falsified the row's own claim ("no claim describes a scanner that no longer exists") in a single build. The specific claim this row cleared — the doc comment's "a defeat that relies on any of these shapes cannot exist here" — was false for the `#if false`/`#if DEBUG` shape, because the parser's view of which region is live disagreed with the compiler's (D15). See "Fix round 5" below for the correction and the arming that proves it. |
| 10 | Let a build-output copy join the population | yes — P14 | Green, no false red — confirms round 3's D9 fix still holds through the rewrite. |

All ten apply and all ten were run (item 9 as a reading rather than a
mutation, since it names a class of *disclosure* defect, not a class of
runtime defeat — stated rather than skipped, per the brief).

### D3 — closed as a side effect, not requested

Round 2 disclosed, and did not fix, a false red on a fully-qualified target
(`OrderToCash.Billing.BillingProgramConfiguration.Configure` failing a
regex capture class). `TargetMatches`'s suffix comparison accepts this
case by construction (the class doc comment states this explicitly). Not
separately armed with a new mutation — it falls directly out of P10's own
comparison logic, which every `AssertDelegatingArguments` call already
exercises on every passing run.

### Final tree state, this round

`git status --porcelain -- 'src/*/Program.cs' src/Seed/Presentation/SeedRunner.cs`
— empty. `src/ZProbeService/` does not exist (never tracked,
`git status --porcelain` confirmed empty for that path before creation,
`rm -rf`ed after P13). `src/Billing/bin/Debug/net10.0/publish/` and
`src/Billing/obj/Release/` do not exist (never tracked, `rm -rf`ed after
P14). `find src -name 'Program.cs'` returns exactly the seven real files.
The tracked-file changes from this round are
`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` (full
rewrite), `tests/Architecture.Tests/Architecture.Tests.csproj` (one
`PackageReference`), `Directory.Packages.props` (one `PackageVersion` plus
its comment), and this record — nothing under `src/` remains modified.
`pgrep -fl "dotnet (build|test|format)"` returns nothing. One build/test
process was live at a time throughout this round; the one background run
(the final solution-wide build/test pass below) was waited on by PID with
`while kill -0 <pid>; do sleep 15; done`, never `pgrep -f`, and no further
command ran until it completed.

### Verification run, this round

`dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0
warnings, 0 errors** (28 projects, including the two new
`Microsoft.CodeAnalysis.CSharp`/`Microsoft.CodeAnalysis.Common` restores).
`dotnet test OrderToCash.sln --no-build --filter
"FullyQualifiedName!~IntegrationTests"`, counted **by name**:

| Project | Passed |
|---|---|
| SharedKernel.UnitTests | 50 |
| Cqrs.UnitTests | 23 |
| Contracts.UnitTests | 24 |
| Seed.UnitTests | 44 |
| Notifications.UnitTests | 111 |
| Fulfillment.UnitTests | 134 |
| Gateway.UnitTests | 211 |
| Billing.UnitTests | 242 |
| Architecture.Tests | 35 |
| Projector.UnitTests | 120 |
| Orders.UnitTests | 463 |
| **Subtotal** | **1457** |

Reconciles exactly against round 3's own subtotal (**1457**);
`Architecture.Tests` stays at **35** — this round replaced the
implementation, not the `[Fact]` count. Container-backed integration
projects were **not** re-run this round: no file under `src/` remains
modified, so nothing that could affect them changed. **Total: 1457 + 448
(unchanged integration subtotal) = 1905**, across 18 projects — matching
the brief's stated baseline exactly. `dotnet format OrderToCash.sln
--verify-no-changes --include tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`
→ clean, exit 0 (after the `_parseOptions` naming fix noted above).

### What was not done, and why (unchanged from round 3)

No change to id 67's four test files, the ledger row, or the
`WebApplicationFactory` decision — none were touched this round, and none
needed to be: the reviewer's bounds named exactly `CompositionRootDelegationWiringTests.cs`,
`Architecture.Tests.csproj`, `Directory.Packages.props` and this record.

---

## Fix round 5 (id 68) — the last round for this entry, per the brief

Round 4's re-review (`progress/review_composition_root_delegation_and_design_time_factories.md`,
"Re-review (round 4)", `:568-778`) found the Roslyn rewrite itself carried
two new, untested premises — D15 (parser/compiler disagreement about which
`#if` region is live) and D16 (the argument finder not anchored to the
host invocation) — plus a fourth consecutive false absolute claim (D19,
now in two files) and one closeable advisory (D18, the connection-string
provenance gap). D17 (a syntax tree has no symbols, so a shadowing type
can defeat spelling-only proof) was judged advisory and explicitly NOT to
be fixed.

### What changed, one file at a time

**`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`**

- **D15, fixed.** `ParseFile` (the single choke point every call site in
  this class reads through) now asserts, after the existing parse-error
  check, that the parsed tree contains **zero** directive trivia
  (`root.DescendantTrivia().Where(t => t.IsDirective)`), naming every
  directive found and the file. This is the reviewer's "positive
  precondition" option: a composition root under this guard may not carry
  conditional compilation of any kind, checked structurally against the
  syntax tree rather than by trying to match the build's `DEBUG`/`TRACE`
  symbols (which would only relocate the disagreement to `#if RELEASE`,
  `#if !DEBUG`, or a future `DefineConstants`). Confirmed, before relying
  on it, that none of the seven `Program.cs` files or `SeedRunner.cs`
  contains a directive today (`grep -n "^\s*#" src/*/Program.cs
  src/Seed/Presentation/SeedRunner.cs` → no hits, exit 1).
- **D16, fixed.** Added `_hostInvocationsByProgramCsPath`, mapping each
  `Program.cs` to the exact `(HostType, HostMethod)` it must call — read
  directly from the six host classes (`BillingHost.CreateBuilder`,
  `FulfillmentHost.CreateBuilder`, `GatewayHost.Build` — **not**
  `GatewayHost.CreateBuilder`, which also exists on that type but is not
  what `Program.cs` calls — `NotificationsHost.CreateBuilder`,
  `ProjectorHost.CreateBuilder`, `OrdersHost.CreateBuilder`). Added
  `IsArgumentOfInvocation`, which requires an `ArgumentSyntax`'s immediate
  enclosing `InvocationExpressionSyntax` (via
  `argument.Parent is BaseArgumentListSyntax { Parent: InvocationExpressionSyntax }`)
  to be that exact host call. `ExtractNamedArgument` now takes `hostType`/
  `hostMethod` and filters on this anchor before counting matches; an
  argument bound to any other invocation (a local function, an unrelated
  helper) no longer counts.
- **D18, closed here, in the existing test** (not routed, per the brief —
  it is one assertion). Added `AssertConnectionStringProvenance`, which
  reads the `<Service>ConnectionString` local's `VariableDeclaratorSyntax`
  and asserts its initializer is a zero-argument
  `<Service>SeedWriter.ConnectionString()` invocation — the missing half
  of `AssertOpenDbPairing`, which only ever checked the identifier's name.
  Wired into `SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings`
  for all three services, ahead of the existing `AssertOpenDbPairing`
  calls — same test name, no new `[Fact]`.
- **D19, corrected.** Deleted the false "a defeat that relies on any of
  these shapes cannot exist here" claim's third list item (the disabled
  `#if false` bullet) rather than weakening it in place, and added a
  "Fix round 5" doc-comment block narrating D15/D16/D17/D19 — including
  why the deleted claim was true about disabled trivia and irrelevant to
  the actual exposure (which region the *parser* treats as disabled, not
  whether a disabled region is invisible). D17 is now stated explicitly as
  a bound ("this guard proves SPELLING, never MEANING") rather than
  implied to be closed.
- **D17 — deliberately not fixed.** Re-confirmed defeatable (see arming
  table below); the doc comment states the bound instead of claiming
  closure.

**`Directory.Packages.props`** — comment only, `:21-34`. Removed the
"this whole defeat class cannot exist" sentence D19 flagged; the
replacement states plainly that conditional-compilation regions were
**not** automatically safe under the Roslyn rewrite and names the guard
(`ParseFile`) that now rejects them structurally. No `PackageVersion`
change — still `Microsoft.CodeAnalysis.CSharp` `5.0.0`, `tests/Architecture.Tests`
only.

**`progress/impl_composition_root_delegation_and_design_time_factories.md`**
— the round-4 defeat-list row 9 (`:1063`) is struck through with a
`CORRECTION` note rather than silently edited, per this repository's D11
precedent: the row's own claim ("no claim describes a scanner that no
longer exists") was the exact claim D19 falsified, and the row is now
explicit that it was mutation-shaped after all.

Nothing under `src/` remains modified — every mutation below was restored
from a `cp` backup, confirmed by `cmp` and an empty `git diff --stat`, and
force-rebuilt before the next step.

### The premises this fixed guard now depends on, enumerated and armed

Per the brief: D15 and D16 were both premises nobody had written down.
Naming the new ones and arming each:

| # | Premise the guard now depends on | Arming mutation | Result |
|---|---|---|---|
| Pr1 | `SyntaxTrivia.IsDirective` is `true` for every directive kind Roslyn's lexer produces, regardless of build symbols | **A1** — D15's exact exploit: `#if DEBUG` around a live 3-argument call, `#else` around a no-op, in `src/Billing/Program.cs` | RED — `ParseFile` names `[#if DEBUG, #else, #endif]` |
| Pr2 | A directive anywhere in a parsed file (not only inside a defeat attempt) is rejected, including a bare dead region with no live/decoy split | Plain `#if false` / `#endif` wrapping a dropped `configureHealth:` (round-4's original D12/D13 shape, re-armed through the new check) | RED — `ParseFile` names `[#if false, #endif]` |
| Pr3 | `IsArgumentOfInvocation`'s anchor (`argument.Parent is BaseArgumentListSyntax { Parent: InvocationExpressionSyntax }`) correctly excludes an argument passed to any call other than the named host call | **A2** — D16's exact exploit: `configureHealth:` dropped from `BillingHost.CreateBuilder(...)` and passed to a local function `WiringNote` instead | RED — `Could not find a 'configureHealth:' named argument passed to BillingHost.CreateBuilder(...)` |
| Pr4 | The anchor does not produce a false red on legitimate call sites in files other than Billing (i.e. the `(HostType, HostMethod)` table is correct for all six hosts, including Gateway's `Build` vs `CreateBuilder` distinction) | Full baseline run, all six `*ProgramCs_Delegates...`/`OrdersProgramCs_...` facts, unmutated source | GREEN — 35/35 (see final verification below) |
| Pr5 | `AssertConnectionStringProvenance`'s initializer match correctly identifies a repointed sibling call while leaving the guarded identifier NAME untouched | `ordersConnectionString`'s initializer repointed to `BillingSeedWriter.ConnectionString()` at `SeedRunner.cs:21` (round-4's exact D18 mutation) | RED — `'ordersConnectionString' is initialized from 'BillingSeedWriter.ConnectionString()', expected 'OrdersSeedWriter.ConnectionString()'` — and `Seed.UnitTests` stays **44/44 green** under the same mutation, confirming this guard is the one that now catches what that suite cannot |
| Pr6 | D17 remains an accurate, disclosed bound rather than a silent gap — the fix round does not accidentally close it and then under-claim, or leave it open and over-claim | A same-named `static class BillingProgramConfiguration` appended after the top-level statements in `src/Billing/Program.cs`, shadowing the real (`using`-imported) type, all three delegates no-ops | Compiles; `BillingProgramCs_Delegates...` stays **GREEN** (1/1 passed) — confirms the doc comment's "never MEANING" claim is still true, not a regression |

### Regression re-confirmation — rows already closed in round 4, re-run against the fixed instrument

| Row | Shape | Mutation | Result |
|---|---|---|---|
| 5 (dead region, D12's one-space defeat superseded) | `#if false`/`#endif` around a dropped argument | Pr2 above | **RED**, now via the directive check rather than the missing-argument message — a change in which assertion catches it, not a regression |
| 7′ (drop from the real call, satisfy elsewhere — D16) | `configureHealth:` moved to `WiringNote` | A2 above | **RED** |
| 7 (drop an optional element entirely, on a file other than Billing) | `configureHealth:` bare-dropped from `src/Projector/Program.cs` | — | **RED** on both `ProjectorProgramCs_Delegates...` (`Could not find a 'configureHealth:' named argument passed to ProjectorHost.CreateBuilder(...)`) and `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk` |
| 3 (substitute a valid sibling identifier — id56/D1/D18's family) | `ordersConnectionString` initializer repointed to `BillingSeedWriter.ConnectionString()` | Pr5 above | **RED**, and closes D18 rather than merely re-confirming it |
| — (D17, advisory, deliberately open) | Shadowing type | Pr6 above | **GREEN**, as documented — not a regression, a disclosed bound |

10 mutations/checks total this round (A1, A2, Pr2, A8-equivalent/Pr5, A3-equivalent/Pr6, plus the Projector drop, plus the four confirming green runs: baseline before, baseline after each restore ×3, and the final full-suite run below). Every mutation used its own `cp` backup, a `dotnet build <project> --no-incremental` proving the mutation itself compiles, `dotnet build tests/Architecture.Tests --no-incremental`, one named `dotnet test --no-build --filter`, verbatim RED/GREEN copied above, restore from the backup, `cmp` **and** `git diff --stat` (both clean every time), a re-read of the restored lines, and a forced `--no-incremental` rebuild of both the mutated project and `tests/Architecture.Tests` before moving on. One build/test process alive at a time throughout; nothing backgrounded; `pgrep -xl dotnet` at close shows only idle `MSBuild.dll /nodemode:1 /nodeReuse:true` reuse nodes (141 of them, all `Sl`/`S`/`Ss`/`Ssl`, none of them a live `build`/`test`/`format` invocation — this long session's accumulated node-reuse pool, not concurrency).

### `CLAUDE.md`'s ten-row defeat list, run again this round

| # | Attack | Ran this round? | Result |
|---|---|---|---|
| 1 | Delete the behaviour | yes, implicitly in A1, A2, Pr2, Projector drop | RED throughout |
| 2 | Corrupt a supplied field | not re-armed this round (unaffected by D15/D16/D18's fix; round 4's P10 stands) | Accepted from round 4 |
| 3 | Substitute a valid sibling identifier | yes — Pr5 (D18) | **RED**, newly closed |
| 4 | Shadow the pattern from a comment or string literal | not re-armed (unaffected by this round's changes; round 4's P3/P5/P6/P12 stand) | Accepted from round 4 |
| 5 | Hide it in a dead region | yes — Pr2 | **RED** |
| 5′ | Hide it in a region the parser thinks is dead and the compiler compiles | yes — A1 (D15) | **RED**, newly closed |
| 6 | Hide it in a raw or verbatim string | not re-armed (unaffected; round 4's P3/P12 stand) | Accepted from round 4 |
| 7 | Drop an optional element entirely | yes — Projector | **RED** |
| 7′ | Drop it from the real call and satisfy the count elsewhere | yes — A2 (D16) | **RED**, newly closed |
| 8 | Compare a literal to a literal | not re-armed (unaffected; round 4's A8-population equivalent/P13 stands) | Accepted from round 4 |
| 9 | Premise half goes stale | yes — this round's whole premise table above, plus the impl-record correction of round 4's own row 9 | Was the round-4 defect (D19); corrected this round |
| 10 | Build-output copy joins the population | not re-armed (unaffected; round 3/4's P14 stands) | Accepted from round 4 |
| — | Identical text, different meaning | yes — Pr6 (D17) | **GREEN, unfixed, disclosed** — not closed by design |
| — | Right name, wrong value provenance | yes — Pr5 (D18) | **RED**, newly closed |

### Final verification, this round

`touch`ed the three mutated files after the last restore, then:

- `dotnet build src/Billing --no-incremental` → 0 warnings, 0 errors.
- `dotnet build src/Projector --no-incremental` → 0 warnings, 0 errors.
- `dotnet build src/Seed --no-incremental` → 0 warnings, 0 errors.
- `dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0 errors.
- `dotnet test tests/Architecture.Tests --no-build` → **35/35 green** (unchanged from round 4 — no `[Fact]` added or removed this round, only method bodies and helpers).
- `dotnet build OrderToCash.sln --no-incremental` → 0 warnings, 0 errors, all 18 projects.
- `dotnet test OrderToCash.sln --no-build --filter "FullyQualifiedName!~IntegrationTests"`, counted **by name**:

| Project | Passed |
|---|---|
| SharedKernel.UnitTests | 50 |
| Cqrs.UnitTests | 23 |
| Contracts.UnitTests | 24 |
| Seed.UnitTests | 44 |
| Notifications.UnitTests | 111 |
| Fulfillment.UnitTests | 134 |
| Gateway.UnitTests | 211 |
| Billing.UnitTests | 242 |
| Architecture.Tests | 35 |
| Projector.UnitTests | 120 |
| Orders.UnitTests | 463 |
| **Subtotal** | **1457** |

Reconciles exactly against round 4's own subtotal (**1457**); no project's
count moved, because this round added no test, only fixed and re-anchored
existing ones. Integration suites were **not** re-run: `git status
--porcelain -- src/Billing/Program.cs src/Projector/Program.cs
src/Seed/Presentation/SeedRunner.cs` is empty (confirmed above, alongside
`find src -name 'Program.cs'` → 7), so nothing that could affect them
changed. **Total: 1457 + 448 (unchanged integration subtotal) = 1905**,
matching the brief's stated baseline exactly.

`dotnet format OrderToCash.sln --verify-no-changes --include
tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs
Directory.Packages.props` → clean, exit 0.

### What was not done, and why

Per the brief's bounds: nothing under `src/` remains modified (three
files were mutated for arming, all three restored and verified above);
`feature_list.json` was not touched; no git command that writes the index
or working tree was used anywhere this round (every restore was `cp` from
a backup taken this session). D17 was deliberately left open, disclosed
as a bound rather than fixed, per the brief's explicit instruction. This
is the fifth and last implementation round for id 68 per the brief; any
defect found in a subsequent review is disclosed and filed as a numbered
backlog entry rather than looped again on this id.
