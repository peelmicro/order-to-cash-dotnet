# review: `composition_root_env_reads_are_unguarded` (id 56, phase 13, `sdd: false`)

## Status header

**Verdict: REJECTED.** Three defects, one of them demonstrated by probe: a read reachable from the Seed composition root can be re-pointed at the **wrong database** with the whole Seed suite green. The rest of the feature is strong — the population is correctly enumerated, the mechanism generalises, and **18 of my own mutation probes bit, 16 of them on reads the implementer did not arm**. Id 56 is set back to `in_progress`. **Recommendation: one short round, not a rework** — the remedy for the blocking defect is three assertions in an existing test file, and the second item is a disclosure the report should already have made. **Phase 13 is not closed**: ids 60, 61, 63, 64, 65 (one guard-hardening loop) and 66 remain. No effort record has been appended to `progress/history.md`, because the feature is not closed.

## What I ran, and what I did not

**Ran myself** (all figures below are from my own runs, none quoted from the implementer):

| Command | Result |
|---|---|
| `dotnet build OrderToCash.sln --nologo` | succeeded, 0 warnings, 0 errors |
| `dotnet build OrderToCash.sln --no-incremental` (after all probes restored) | succeeded, 0 errors |
| the 11 container-free test projects (7 service `*.UnitTests` + `SharedKernel` + `Contracts` + `Cqrs` + `Architecture.Tests`) | **1223 passed, 0 failed, 0 skipped** — Billing 232, Fulfillment 124, Gateway 204, Notifications 70, Orders 351, Projector 91, Seed 41, SharedKernel 50, Contracts 21, Cqrs 23, Architecture 16 |
| `./init.sh` | exit 0, including §5d shared-spec parity (6 files byte-identical to #7, `test-matrix.md` exempt) |
| 18 mutation probes (table below) | 16 red as required, **2 GREEN — the defects** |

**Did not re-run, and why:** the six container-backed `*.IntegrationTests` projects and the full `./quality.sh`. No claim under review is about them — this feature adds no integration test, touches no infrastructure adapter, and my traceability walk showed the new code has **zero** integration-suite callers (see "Does anything but the new unit tests exercise these paths?"). The full-suite figure of 1572 is therefore the implementer's, not mine; my 1223 covers every project that could have been affected by the refactor plus every project containing a new test.

## CHECKPOINTS.md — walked

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 — run by me

### C2 — state is coherent
- [x] at most one feature `in_progress` (zero at the time of review; id 56 was `in_review`)
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests associated with it
- [x] `progress/current.md` describes the active session
- [x] every `blocked` feature records why — none blocked

### C3 — architecture is respected
- [x] no framework reference inside any `Domain/` folder — **verified by running** `Architecture.Tests`, 16/16, not by eye
- [x] no cross-service database access — the refactor moved no persistence code; the four `*DbContextFactory` classes each remain confined to their own service
- [x] no shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — the six new files each live inside their own service project
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` — enforced by the architecture suite that I ran
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — untouched by this feature
- [x] every inter-service interaction classifiable as Kafka-fact or NATS-RPC — this feature introduces no interaction; it moves configuration literals only
- [x] no stray debug logging, no context-free TODOs in the seven new source files

### C4 — verification is real
- [ ] `./quality.sh` passes — **not re-run by me** (see above); the implementer reports 1572/0/0. My 1223-test container-free subset is green.
- [x] domain tests are pure
- [x] integration tests use Testcontainers — unchanged by this feature
- [ ] coverage thresholds — no coverage gate is armed yet in this repository (`quality.sh`'s own header defers it to feature 34); not assessable, and not this feature's obligation
- [x] no Jest anywhere

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the 19 untracked paths are the seven new source files, ten new test files, the test-collection definition and the impl report
- [ ] `progress/history.md` has an entry with an effort record — **absent, correctly**: the feature is rejected, so no entry is owed yet
- [ ] `feature_list.json` reflects the true state — I have set id 56 back to `in_progress` as part of this verdict
- [ ] the human has been told what was done and how to test manually — for the leader, after the re-review
- [x] Claude did not commit

### C6 — spec-driven development
Not applicable: id 56 is `sdd: false`. `init.sh` confirms SDD coherence for the 7 `sdd: true` features past `pending`.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7 — `init.sh` §5d, 6 files, `test-matrix.md` exempt. **This feature touched nothing under `specs/shared/`**: `git diff -- specs/shared/asyncapi.yaml` shows exactly the three added `note:` lines of `SA-2`, which predate the feature and belong to the leader.
- [x] every deviation is a recorded amendment in both repositories — `SA-2` is applied to both, and `init.sh` §5d is green **because** both moved together
- [x] the `R<n>` ids are #7's — this feature claims none, correctly (see "R-mapping" below)
- [x] `n8n/workflows/*.json` unchanged — untouched
- [x] the black-box API script — untouched
- [ ] `progress/history.md` effort records complete — pending this feature's close
- [x] the README's benchmark section — untouched by this feature

## The population — re-derived independently, and with a sweep that could fail

Two agreeing counts obtained the same way are one count, so I did **not** simply re-run the implementer's `grep`. I first asked what a violation would do to the candidate list: a read that used **any other mechanism** would be invisible to a sweep for `Environment.GetEnvironmentVariable`. So the enumerating command is the *complement*:

```
$ grep -rn "GetEnvironmentVariable\|GetEnvironmentVariables\|AddEnvironmentVariables\|Configuration\[\|GetValue<\|GetConnectionString\|IConfiguration\|EnvironmentVariableTarget\|DOTNET_\|ASPNETCORE_" src/ --include="*.cs" | grep -v "Environment\.GetEnvironmentVariable("
src/Orders/Infrastructure/OrdersOutboxOptions.cs:5:/// ... not by binding <c>IConfiguration</c> here ...
src/Orders/OrdersHost.cs:39:        // ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT is unset — which is every
```

Two hits, **both doc comments, zero code** — classified individually. So `Environment.GetEnvironmentVariable` is the *only* env-read mechanism in `src/`, and the narrow sweep is safe to use as the population:

```
$ grep -rn "Environment\.GetEnvironmentVariable" src/ --include="*.cs" | wc -l
102
```

Per-file, my own run agrees with the implementer's table line for line: 18 files, 102 reads, 20 of them in the four `*DbContextFactory` classes, 82 in scope. **The corrected acceptance text's figure of 102 is confirmed.** One further enumeration matters for defect D1:

```
$ grep -rn "Environment\.GetEnvironmentVariable(" src/ --include="*.cs" | grep -v 'GetEnvironmentVariable("'
src/Seed/Infrastructure/Persistence/SeedDbConfig.cs:19:        var database = Environment.GetEnvironmentVariable(databaseEnvVar) ?? defaultDatabase;
```

Exactly one of the 82 reads takes its variable name from a **parameter** rather than a literal. That single hit is the defect.

## Probes — 18 mutations, run by me, in three families

Protocol: `cp` backup, mutate one line, run the owning project, restore from the backup, `cmp` against the backup, and after **all** probes a `dotnet build --no-incremental` plus a confirming green run (results in the table at the top). Sixteen of the eighteen targets are reads the implementer did **not** arm; nine of them live in Options classes rather than `Program.cs`, which is where the leader's scope correction bit.

| # | Mutation | Family | Result |
|---|---|---|---|
| P1 | `JwtOptions.cs:37` — `JWT_ISSUER` read deleted | deletion, Options class | RED — `JwtOptionsTests.FromEnvironment_ReadsAllThreeVariables_WhenSet`: `Expected: "otc-test-issuer"` / `Actual: "order-to-cash"` |
| P2 | `GatewaySseOptions.cs:23` — `GATEWAY_SSE_PING_INTERVAL_MS` read deleted | deletion, Options class | RED — 1 failed / 203 passed |
| P3 | `ProjectorOptions.cs:36` — `MONGO_INITDB_ROOT_USERNAME` read deleted | deletion, Options class | RED — `ProjectorProgramConfigurationTests.Configure_ReadsEveryVariable_...`: `Expected: "mongodb://custom_user:s3cr3t@mongo-box:27"···` / `Actual: "mongodb://otc_mongo_root:s3cr3t@mongo-box"···` |
| P4 | `SeedMongoConfig.cs:16` — `MONGO_HOST_PORT` read deleted | deletion, loader class | RED — `SeedMongoConfigTests.Load_ReadsEveryVariable_...`: `Expected: ···":s3cr3t@mongo-box:27099/?authSource=admin"` / `Actual: ···":s3cr3t@mongo-box:27017/?authSource=admin"` |
| P5 | `NotificationsProgramConfiguration.cs:32` — `MAILPIT_SMTP_HOST_PORT` read deleted (the second arm of a fallback chain) | deletion | RED — `Configure_FallsBackToMailpitSmtpHostPort_...`: `Expected: 1030` / `Actual: 1025` |
| P6 | `OrdersProgramConfiguration.cs:31` — `ConfigureSaga`'s `KAFKA_BOOTSTRAP_SERVERS` deleted (the *second* of two identical reads in the file) | deletion | RED — `OrdersProgramConfigurationTests.ConfigureSaga_ReadsKafkaBootstrapServers_WhenSet`: `Expected: "kafka-box:9999"` / `Actual: "localhost:9092"` |
| P7 | `LoginThrottleOptions.cs:27` — reads the **wrong variable** (`GATEWAY_LOGIN_RATE_WINDOW_SECONDS` → `GATEWAY_LOGIN_RATE_LIMIT`) | wrong-name | RED — `LoginThrottleOptionsTests.FromEnvironment_ReadsBothVariables_...`: `Expected: 30` / `Actual: 5` |
| P8 | `GatewayMongoOptions.cs:18` — default `"localhost"` corrupted to `"corrupted-host"` | corrupted default | RED — `GatewayMongoOptionsTests.FromEnvironment_BuildsTheDocumentedDefaultUri_...` |
| P9 | `GatewayProgramConfiguration.cs:35` — the **call site** `options.Sse = GatewaySseOptions.FromEnvironment();` deleted | call-site deletion | RED — `GatewayProgramConfigurationTests.Configure_SetsSseFromEnvironment_...`: `Expected: 77` / `Actual: 500` |
| P10 | `SeedDbConfig.cs:19` — the dynamic `GetEnvironmentVariable(databaseEnvVar)` read deleted | deletion | RED — `SeedDbConfigTests.BuildConnectionString_ReadsEveryVariable_...` |
| P11 | `OperatorIdentityLoader.cs:19` — `GATEWAY_OPERATOR_DISPLAY_NAME` read deleted | deletion, Options class | RED — `Expected: "Custom Operator Display Name"` / `Actual: "Order-To-Cash Operator"` |
| P12 | `BillingProgramConfiguration.cs:45` — `MSSQL_APP_USER` read deleted | deletion | RED — `Expected: ···"User Id=custom_user"···` / `Actual: ···"User Id=otc_app"···` |
| P13 | `FulfillmentProgramConfiguration.cs:32` — `MSSQL_HOST_PORT` read deleted | deletion | RED — `Expected: "Server=sql-box,14330;..."` / `Actual: "Server=sql-box,1433;..."` |
| P14 | `NotificationsProgramConfiguration.cs:33` — `NOTIFICATIONS_SMTP_FROM_ADDRESS` read deleted | deletion | RED — `Expected: "custom@example.com"` / `Actual: "no-reply@order-to-cash.example"` |
| P15 | `ProjectorOptions.cs:41` — reads the **wrong variable** (`MONGO_DB_READMODEL` → `MONGO_HOST`) | wrong-name | RED — `Expected: "custom_read_model"` / `Actual: "mongo-box"` |
| P16 | `GatewayProgramConfiguration.cs:48` — `NATS_HOST` read deleted | deletion | RED — `Expected: "nats://nats-box:4999"` / `Actual: "nats://localhost:4999"` |
| **P17** | `src/Billing/Program.cs:13` — `configure: BillingProgramConfiguration.Configure` replaced by `configure: static _ => { }` | call-site deletion | **GREEN — `Passed! Failed: 0, Passed: 232`** → defect D2 |
| **P18** | `OrdersSeedWriter.cs:23` — the Orders seed writer asks `SeedDbConfig` for **`MSSQL_DB_BILLING`** instead of `MSSQL_DB_ORDERS` | wrong-name | **GREEN — `Passed! Failed: 0, Passed: 41`** → defect D1 |

Every restore was confirmed with `cmp` against its backup **and** by re-reading the mutated line; the tracked file among them (`src/Billing/Program.cs`) was restored from a `cp` backup, never with `git checkout --`. `git diff --stat` after all eighteen probes is identical to the pre-review snapshot: **17 files changed, 289 insertions(+), 191 deletions(-)**.

## Defects

### D1 — BLOCKING. `src/Seed/Infrastructure/Persistence/OrdersSeedWriter.cs:23` (and its two siblings): a read reachable from the Seed composition root can be pointed at the wrong database with the suite green

```csharp
// src/Seed/Infrastructure/Persistence/OrdersSeedWriter.cs:23
public static string ConnectionString() => SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders");
// src/Seed/Infrastructure/Persistence/FulfillmentSeedWriter.cs:23  — "MSSQL_DB_FULFILLMENT", "otc_fulfillment"
// src/Seed/Infrastructure/Persistence/BillingSeedWriter.cs:24      — "MSSQL_DB_BILLING", "otc_billing"
```

`SeedDbConfig.cs:19` is the one read of the 82 whose **variable name is not a literal in the guarded code** — it is a parameter, and the three call sites above are what bind it. `SeedDbConfigTests` supplies that parameter itself (`SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders")`) and then asserts the result, so it can only ever prove that `BuildConnectionString` reads whatever name the **test** handed it. Nothing anywhere asserts that `OrdersSeedWriter` hands it `MSSQL_DB_ORDERS`.

P18 demonstrates the consequence: with the Orders seed writer asking for `MSSQL_DB_BILLING`, `dotnet run --project src/Seed` seeds the Orders fixtures into the **Billing** database, and `Seed.UnitTests` is 41/41 green.

This is exactly the shape `CLAUDE.md` names — *"a corruption probe only bites on a field whose expected value the test supplied … For fields the test does not control, inject the source … or the field is unguarded however many probes you run"* — and it is the same defect class the feature exists to close, surviving one hop up the call chain. It also contradicts the feature's own test documentation: `tests/Seed.UnitTests/SeedDbConfigTests.cs:9-16` states the reachability chain `Program.cs → SeedRunner.RunAsync → OrdersSeedWriter.ConnectionString() → SeedDbConfig.BuildConnectionString` as the justification for the file existing, and then tests only the last link of it.

**Why it matters beyond Seed:** the acceptance bullet the leader corrected says the guard covers every read *reachable from a composition root, wherever the read physically lives*. For this read, "which variable it reads" is not a property of where it lives — it is a property of the caller, and the caller is unguarded.

**Remedy (three assertions, one existing file).** In `tests/Seed.UnitTests/SeedDbConfigTests.cs`, add one test per writer that sets the writer's own `MSSQL_DB_*` variable to a distinctive value and asserts it appears in `OrdersSeedWriter.ConnectionString()` / `FulfillmentSeedWriter.ConnectionString()` / `BillingSeedWriter.ConnectionString()` — and, since the default matters too, that with the variable unset the writer's own default database name appears. Arm it with P18's mutation and record the verbatim failure.

### D2 — Required disclosure (and a recommended guard). The delegating line in each `Program.cs` is itself unguarded, and the report claims otherwise

`progress/impl_composition_root_env_reads.md` states that the chosen mechanism *"is 'an options-binding test that drives the real Program.cs path' (acceptance bullet 2's first option)"*. **No test drives `Program.cs`.** The tests drive the method `Program.cs` calls, which is a different and weaker claim, and P17 measures the difference: replacing `configure: BillingProgramConfiguration.Configure` with a no-op lambda leaves `Billing.UnitTests` 232/232 green. The same holds for the other five services and for the three Orders delegates — eight one-line references in six files, plus `src/Seed/Program.cs → SeedRunner.RunAsync()`, which no test executes at all.

This is not a rejection on its own — bullet 2 offers two mechanisms and the one chosen is legitimate, and the feature reduces the unguarded surface from 82 reads to 9 delegating references, which is the great majority of the value. But an **undisclosed** residual of the very class the feature closes is not acceptable in the record, particularly in a repository whose stated pathology is *"a property that is real, correct, and guarded by nothing"*.

**Required before re-review:** state the residual explicitly in the report — which lines remain unguarded, what mutation leaves the suite green, and why the remaining risk was accepted. **Recommended, not required:** one guard per service. The two candidates I would weigh are bullet 2's second option (a boot smoke test per service, expensive because it needs real brokers) and a cheap reflection assertion over the service assembly's compiler-generated `Program` type — note that the latter is a *partial* guard: it would catch a reversion to an inline lambda, but not a substitution of a different named delegate, so it should not be sold as complete.

## Ruling on the delegated scope judgement: the 20 `*DbContextFactory` reads

**The exclusion stands. The reachability claim is true. But it must be routed as a numbered backlog entry, not discharged in a paragraph.**

Verified as a search result, not a reading — the enumerating command over the whole repository, with every hit classified:

```
$ grep -rn "DbContextFactory" . --include="*.cs" --include="*.csproj" --include="*.sh" --include="*.md" --include="*.yml" --include="*.yaml" --include="*.json" | grep -v "/bin/\|/obj/"
```

Every hit in `src/` is either (a) one of the four class **declarations** — `BillingDbContextFactory.cs:18`, `FulfillmentDbContextFactory.cs:18`, `NotificationsDbContextFactory.cs:18`, `OrdersDbContextFactory.cs:18`, each `: IDesignTimeDbContextFactory<T>` — or (b) a **comment** in the corresponding `*ProgramConfiguration.cs` ("Mirrors …DbContextFactory's own reading"), or (c) a comment in `SeedDbConfig.cs:8`. **Zero constructions, zero DI registrations, zero call sites.** The remaining hits are `progress/*.md` and `feature_list.json`. So the classes are genuinely reachable only through `dotnet ef`'s reflection over the built assembly, and there is no composition-root path a test could drive through them. The implementer's ruling is correct and correctly evidenced.

**Where the ruling stops short.** *"Not reachable from a composition root"* is not the same as *"not used"*. `dotnet ef` **is** tooling this team runs — `progress/impl_db_orders.md`, `impl_db_billing.md` and `impl_money_column_width.md` record `migrations add`, `database update` and `has-pending-model-changes`, and `progress/review_db_orders.md:336` records a *reviewer* running `dotnet ef migrations list` as a probe. So those 20 reads are live, unguarded, and a corruption in one of them (say `MSSQL_DB_ORDERS` → `MSSQL_DB_BILLING`, exactly D1's shape) would silently point a migration at the wrong database with every test green. An exclusion of a live, unguarded population is **a decision, not an absence**, and this repository's own record is that a decision recorded only in prose gets routed around.

**Routing (for the leader to file — I do not write backlog entries):** a new `feature_list.json` entry, *"design-time `IDesignTimeDbContextFactory` env reads are unguarded — 20 reads across four `*DbContextFactory` classes"*, with acceptance along the lines of: each factory's `CreateDbContext([])` is driven directly by a named test that sets non-default `MSSQL_*` values and asserts the resulting connection string, armed in both families (read deleted, and variable name swapped). It is cheap — the classes are `public` and the four test projects already reference their services — and it closes the last 20 of the 102. Recording it as an entry rather than a paragraph is the whole point: `SA-2` needed two assessments and 39 commits to get filed because the first two disclosures named nobody.

## The ported-idiom ledger

`sdd: false`, so the ledger lives in `progress/impl_composition_root_env_reads.md` per the phase-13 gate decision. It has four rows, each citing #7 by file, and I checked the **claims**, not the existence.

| Row | Claim | My check | Verdict |
|---|---|---|---|
| 1 | #7 never solved this — *"no `main.spec.ts` anywhere"*, *"zero tests drive any `main.ts`"* | **Half true, and the false half matters.** `find /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs -iname "main.spec.ts" -not -path "*/node_modules/*"` → empty, so the first clause holds. But `grep -rn "from '.*main'" --include="*.spec.ts"` returns **three** hits — `apps/orders/src/{orders-create-idempotent-replay,catalog-reference-list-wire,orders-create-wire}.integration.spec.ts` all `import { createOrdersNatsMicroserviceOptions } from './main'`. #7's composition root was an ordinary module with **named exports**, and three of its tests drove one. See D3 | Overstated — must be corrected |
| 2 | *"six of roughly twenty"* `*.config.ts` loaders have a spec | **Both figures wrong.** `find $N -name "*.config.spec.ts" -not -path "*/node_modules/*"` → **7**; `find $N/apps -name "*.config.ts" -not -path "*/node_modules/*" \| wc -l` → **29**. The row's own list contains seven paths while its prose calls five `kafka.config.spec.ts` files "the four". The *direction* of the claim (a minority) survives; the numbers do not. See D3 | Overstated — must be corrected |
| 3 | The loader's result is actually wired in — and a **default-only** assertion could not distinguish "read and assigned" from "never assigned", because `GatewayOptions`' property-initialiser default equals `FromEnvironment()`'s no-env-set default | **This is the sharpest row and it is correct.** I re-derived it rather than trusting it: `GatewayProgramConfigurationTests` sets a non-default value per sub-option, and P9 (deleting the `options.Sse = …` call site) fails with `Expected: 77 / Actual: 500` — a default-only assertion would have passed. The named guard executes the code the row is about | Sound, and armed |
| 4 | The design-time factories are outside the running composition root, so **no guard is owed** | The row does say what makes a guard impossible (no code path to drive) rather than hand-waving "the framework handles it", and I verified the enumeration behind it. **But a row concluding no guard is owed must say what was done to make it fail** — this row says what *could not* be written, not what was tried. And the conclusion "not owed" over-reaches: not owed *by this feature's acceptance* is true; *not owed at all* is not, since `dotnet ef` runs here | Partially sound — must be amended per the routing above |

**The ledger row that is missing** is D2's: *"#7's composition root was `main.ts`, an ordinary module a test could `import`; in #8 it is top-level statements compiled into an inaccessible `Program.<Main>$`, so the composition root itself is unreachable and only the extracted delegate is guarded."* That is a genuine ported-property difference, it is the root cause the report identifies, and its consequence — the unguarded delegating line — is precisely the residual the ledger convention exists to force someone to name at translation time.

### D3 — Two counts in the ledger were written, not read

`CLAUDE.md`: *"a claim of completeness is a count, and a count is a reading"*, and *"a negative claim about the repository is a search result, not a reading"*. Two of the ledger's #7 claims fail that test, and both are one command from the truth:

```
$ N=/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs
$ find $N -name "*.config.spec.ts" -not -path "*/node_modules/*" | wc -l
7
$ find $N/apps -name "*.config.ts" -not -path "*/node_modules/*" | wc -l
29
$ grep -rn "from '.*main'" $N --include="*.spec.ts" | grep -v node_modules
apps/orders/src/orders-create-idempotent-replay.integration.spec.ts:40:import { createOrdersNatsMicroserviceOptions } from './main';
apps/orders/src/catalog-reference-list-wire.integration.spec.ts:42:import { createOrdersNatsMicroserviceOptions } from './main';
apps/orders/src/orders-create-wire.integration.spec.ts:66:import { createOrdersNatsMicroserviceOptions } from './main';
```

The report says *"six of roughly twenty"* (it is 7 of 29) and *"zero tests drive any `main.ts`"* (three do). Neither error changes the conclusion — a minority of loaders are tested, and no test drives a `bootstrap()` — but the second one **buries the most useful thing in the ledger**. #7's `main.ts` is a module with named exports, one of which three tests import; #8's `Program.cs` compiles to `Program.<Main>$` and exports nothing nameable. *That* is the ported-property difference, stated precisely, and it is a better justification for the refactor than "#7 never solved this" — it says what #7 got for free (an importable composition root) and what #8 had to hand-build to replace it. Also worth carrying: #7's `main.ts` files hold **one** env read each (`process.env.<SERVICE>_PORT`, six in total, inside the non-exported `bootstrap()`), because #7 put its env reads in `*.config.ts` loaders. #8's distribution is genuinely different, and the ledger should say so rather than score #7.

**Required before re-review:** replace both figures with enumerated ones, correct the `main.ts` claim, and re-state row 1 as the property difference rather than as a deficiency in #7.

## Verifying the `Program.<Main>$` claim myself

The claim justifies the entire refactor, so I did not accept it. I compiled a minimal reproduction of the old shape (top-level statements holding an inline `configure` lambda plus a local `static` helper, with `InternalsVisibleTo` for a probe assembly) and reflected over the emitted assembly:

```
TYPE Program public=False nested=False
   METHOD <Main>$ public=False static=True
   METHOD <<Main>$>g__Build|0_1 public=False static=True
TYPE Program+<>c public=False nested=True
   METHOD <<Main>$>b__0_0 public=False static=False
```

**Confirmed, with one nuance the report should carry.** The inline lambda becomes `Program+<>c.<<Main>$>b__0_0` and the local function becomes `Program.<<Main>$>g__Build|0_1`. Every one of those names contains `<`, `>` or `|`, so **no C# source can name them** — the report's "genuinely unreachable, not merely hard to invoke" is right for any reasonable test. Strictly, they are reachable by reflection over compiler-generated mangled names (and `InternalsVisibleTo("OrderToCash.Billing.UnitTests")` already exists at `src/Billing/InternalsVisibleTo.cs:8`, so the `Program` type itself is visible) — but those names are an implementation detail the compiler may change, and invoking `<Main>$` would run the whole host. The one *supported* alternative that would have reached the old shape is `WebApplicationFactory<Program>` for the Gateway; the repository does not use it, and `tests/Gateway.IntegrationTests/GatewayTestHost.cs:11` records a deliberate decision against `TestServer`/`WebApplicationFactory`. **So the refactor is justified as claimed**, and the "not merely hard, genuinely unreachable" phrasing is fair for the five non-HTTP hosts and very nearly fair for the Gateway.

## Did the extraction change runtime behaviour anywhere?

**No — verified mechanically, not by eye.** For each of the six services I took every removed line from `git diff -U0 -- src/<S>/Program.cs`, stripped comments, blanks and `using` directives, and checked each remaining statement appears **verbatim** in the new `<S>ProgramConfiguration.cs`. The only unmatched lines across all six are pure scaffolding — `var builder = XHost.CreateBuilder(`, `args,`, `configure: options =>`, `});` — i.e. the lambda syntax that the extraction replaced with a method declaration. Every assignment, every fallback literal, every `int.TryParse`, every `?? throw`, and the ordering between them is preserved statement for statement.

Cross-check by count, per file: Billing 12 reads before and after, Fulfillment 11, Orders 11 (2 outbox + 2 acceptance + 2 saga + 5 connection string), Notifications 11, Projector 4, Gateway 3. The delegates are still passed to the same `*Host.CreateBuilder`/`GatewayHost.Build` overloads in the same argument positions, so registration order, option-binding order and middleware order are untouched. `Architecture.Tests` 16/16 confirms no layering violation was introduced by the new root-namespace classes.

## Does anything but the new unit tests exercise these paths?

**No — this is the leader's third question, and the answer is the one that matters for D2.**

```
$ grep -rn "ProgramConfiguration\." --include="*.cs" src/ tests/
```

Every caller is either one of the six `Program.cs` files or one of the six new `*ProgramConfigurationTests` files. **Zero integration-suite callers.** The integration suites build their own hosts — `GatewayTestHost` explicitly so — and always did. That is not a regression, but it means the new classes are covered exclusively by the new unit tests, and the seam between `Program.cs` and them is covered by nothing.

## `R<n>` → test mapping

Id 56 is `sdd: false` and claims no `R<n>`; `specs/shared/test-matrix.md` is correctly **not** edited. The one adjacent requirement is **R43** (eager validation of `CREDIT_FAILURE_RATE`), already `DONE` and citing `BillingHostCreditFailureRateBootTests`. I verified the report's account of the relationship rather than taking it: that test supplies its own `configure` lambda calling `CreditSimulatorOptionsLoader.Load("not-a-number")`, so it proves the exception propagates through `CreateBuilder`/`Build()` and cannot prove that the composition root reads the variable — which is why deleting the read from `Program.cs:23` left it green in feature 20's review. `BillingProgramConfigurationTests.Configure_Throws_WhenCreditFailureRateIsOutOfRange` and `..._ReadsEveryVariable_...` now close that, and my own P-family probes on the Billing file confirm both fail when the read is removed or the value corrupted. R43's row remains accurate; no `test-matrix.md` change is owed.

## Other checks

- **The self-referential-list trap (backlog id 64's shape):** not present. The `_envVars` arrays in the new test files are used **only** to clear the environment between tests; no test compares a list of variable names against a list of variable names. Every assertion is a value assertion against a value the production code produced. P7 and P15 confirm it empirically: swapping which variable a line reads makes a test fail, which a name-list comparison could never do.
- **Test isolation:** `GatewayEnvironmentVariableTestCollection` is a real fix, not ceremony — seven Gateway test classes mutate process-wide environment variables and three of them share variable names with the new ones. The three pre-existing files' diffs are one added `[Collection(...)]` attribute each and nothing else. I ran `Gateway.UnitTests` eight times across this review (baseline plus seven probe cycles) with no flake.
- **Package discipline:** no `PackageReference` added anywhere — confirmed, `git status` shows no `.csproj` modified.
- **`feature_list.json`:** the diff before my edit was the single `"status"` line for id 56 plus the leader's own pre-existing uncommitted hunks (id 56's corrected acceptance/notes, the new id 66 entry). The implementer added nothing else. I edited one line and read the diff to confirm it.
- **`specs/shared/`:** untouched by this feature. `git diff -- specs/shared/asyncapi.yaml` is the three `SA-2` lines only.

## What must change before re-review

1. **D1 — fix and arm.** Guard the three seed-writer bindings (`OrdersSeedWriter`, `FulfillmentSeedWriter`, `BillingSeedWriter`) so that P18's mutation — asking `SeedDbConfig` for another service's `MSSQL_DB_*` variable — fails a named test. Also guard each writer's own default database name. Record the verbatim failures.
2. **D2 — disclose, and say what was accepted.** Correct the report's claim that the tests "drive the real Program.cs path", name the nine delegating references that remain unguarded (six `Program.cs` files, eight delegate arguments, plus `Seed/Program.cs → SeedRunner.RunAsync()`), state P17's green result as the measurement, and either add a guard or record explicitly why the residual is accepted.
3. **Ledger row 4 — amend.** Say what was attempted to make the design-time factories fail and why nothing could, and replace *"no guard is owed"* with *"no guard is owed **by this feature**; the 20 reads are live under `dotnet ef` and are routed to backlog entry `<id>`"*. Add the missing row for the `main.ts` → `Program.<Main>$` property difference and its residual.
4. **D3 — re-enumerate the two #7 counts** (7 config specs, 29 loaders) and correct the *"zero tests drive any `main.ts`"* claim; re-frame ledger row 1 as the property difference (importable module with named exports → inaccessible `Program.<Main>$`).
5. **Nothing else.** Do not re-touch the six `*ProgramConfiguration.cs` files, the six `Program.cs` files, or any of the ten new test files beyond `tests/Seed.UnitTests/SeedDbConfigTests.cs` (or a new sibling file for the writer guards). The extraction is behaviour-identical and the guards are sound — 16 of my 18 probes bit, including nine in Options classes. This is a one-round fix.

**For the leader, not the implementer:** file the `*DbContextFactory` backlog entry described above before this feature closes. I have not written it — backlog entries are the leader's, and `feature_list.json` is a single-writer file.

## Phase 13 is not closed

Remaining after id 56: **60, 61, 63, 64, 65** (one guard-hardening loop) and **66** (`operator_note_reaches_the_timeline`, the entry `SA-2` makes satisfiable). `SA-2` itself is applied to both repositories and still uncommitted.

## Effort record

None appended to `progress/history.md` — the feature is rejected and not closeable. When it closes, the record must note that **#7 has no counterpart**: it never filed this entry, has no `main.spec.ts`, and unit-tests fewer than a third of its config loaders. This is **#8-only process cost**, and what it bought should be stated as the measurement rather than an adjective: 82 of 102 environment reads moved from *provably unguarded* (feature 20's review deleted one and the suite stayed green) to *provably guarded* — 16 independent mutations, in three families, every one red.

---

# Round 2 — re-review

## Status header

**Verdict: APPROVED.** D1 is closed and I killed it with my own hands: the exact mutation that left round 1 at 41/41 green now takes down a named test, and so do three more substitutions I invented. D2's disclosure is honest and complete. D3's two counts are correct — I re-derived both against #7's checkout rather than reading them. The round touched exactly one file under `src/` or `tests/`, proven three independent ways. **One new finding, R2-5, non-blocking and routed**: the guard pins *which variable each seed writer reads*, but nothing pins *which connection string each writer's DbContext is opened with*, and swapping them inside `SeedRunner` produces D1's exact consequence — Orders fixtures into the Billing database — with `Seed.UnitTests` 44/44 green. It sits inside the residual D2 already discloses, it is outside id 56's acceptance population (it is not an environment-variable read), and **it must leave a backlog entry rather than a sentence** — see "Routing" below. Id 56 set `done`, effort record appended. **Phase 13 is not closed**: ids 60, 61, 63, 64, 65 (one guard-hardening loop) and 66 remain, and `SA-2` is still uncommitted.

## What I ran this round, and what I did not

| Command | Result |
|---|---|
| `dotnet build OrderToCash.sln --no-incremental --nologo` (after all restores) | succeeded, **0 warnings, 0 errors** |
| `dotnet test tests/Seed.UnitTests` (baseline, before any probe of mine) | **44 passed, 0 failed, 0 skipped** |
| the 11 container-free projects, `--no-build` off the forced rebuild | **1226 passed, 0 failed, 0 skipped** — Billing 232, Fulfillment 124, Gateway 204, Notifications 70, Orders 351, Projector 91, Seed 44, SharedKernel 50, Contracts 21, Cqrs 23, Architecture 16 |
| `./init.sh` | exit 0 — including §5d (`shared spec byte-identical to #7 across 6 file(s)`), `no feature in_progress`, `43/66 features done`, backlog tripwire clean, commit-msg hook matches its tracked copy |
| 5 mutation probes of my own (table below) | **4 RED as required, 1 GREEN — finding R2-5** |

**Did not re-run, and why:** the six container-backed `*.IntegrationTests` projects and `./quality.sh`. No claim in this round is about them — the round added three unit tests to one project and changed no source. I corroborated the implementer's `1575` **without** re-running it, two ways: (a) my own container-free subset is **1226**, exactly round 1's 1223 plus this round's three, and 1226 + the six integration projects' reported 349 (6+52+12+56+48+83+92) = **1575**, and (b) `TestResults/` carries **18** `coverage.cobertura.xml` files written between **15:47:31 and 15:55:21**, one per test project, which is a real full pass and not a quoted figure. Round 1's own 1223 subset is likewise mine, not the implementer's.

## Probe 1 — D1: the substitution guards, run by me

Protocol: `cp` backup, mutate one literal, `dotnet build --no-incremental`, run `Seed.UnitTests`, restore from the backup, `cmp` against it, forced rebuild, confirming green. Final state proven against HEAD as well — these four files are **tracked**, so `git diff -- src/Seed/` is a check that can fail, and it is empty.

| # | Mutation | Family | Result |
|---|---|---|---|
| **R2-1** | `OrdersSeedWriter.cs:23` — `BuildConnectionString("MSSQL_DB_ORDERS", …)` → `("MSSQL_DB_BILLING", …)` — **round 1's P18, the one that was green** | substitution | **RED** |
| **R2-2** | `OrdersSeedWriter.cs:23` — the *default* only: `"otc_orders"` → `"otc_billing"`, variable name left correct | substitution, default half | **RED** |
| **R2-3** | `FulfillmentSeedWriter.cs:23` — `"MSSQL_DB_FULFILLMENT"` → `"MSSQL_DB_ORDERS"` | substitution | **RED** |
| **R2-4** | `BillingSeedWriter.cs:24` — `"MSSQL_DB_BILLING"` → `"MSSQL_DB_FULFILLMENT"` | substitution | **RED** |
| **R2-5** | `SeedRunner.cs:26` — `OrdersSeedWriter.OpenDb(ordersConnectionString)` → `OpenDb(billingConnectionString)` | substitution, one hop up | **GREEN — 44/44** → finding R2-5 |

Verbatim, R2-1:

```
Failed OrderToCash.Seed.UnitTests.SeedDbConfigTests.OrdersSeedWriter_ConnectionString_ReadsMsSqlDbOrders_AndFallsBackToOtcOrders [5 ms]
  Error Message:
   Assert.Contains() Failure: Sub-string not found
String:    "Server=localhost,1433;Database=otc_orders"···
Not found: "Database=custom_orders_db;"
Failed!  - Failed: 1, Passed: 43, Skipped: 0, Total: 44
```

Verbatim, R2-2 — the second assertion in the same test, which is what makes the default a guarded property and not an incidental one:

```
Failed OrderToCash.Seed.UnitTests.SeedDbConfigTests.OrdersSeedWriter_ConnectionString_ReadsMsSqlDbOrders_AndFallsBackToOtcOrders [4 ms]
String:    "Server=localhost,1433;Database=otc_billin"···
Not found: "Database=otc_orders;"
```

Verbatim, R2-3 and R2-4 together (one build, both writers mutated):

```
Failed OrderToCash.Seed.UnitTests.SeedDbConfigTests.FulfillmentSeedWriter_ConnectionString_ReadsMsSqlDbFulfillment_AndFallsBackToOtcFulfillment [14 ms]
Not found: "Database=custom_fulfillment_db;"
Failed OrderToCash.Seed.UnitTests.SeedDbConfigTests.BillingSeedWriter_ConnectionString_ReadsMsSqlDbBilling_AndFallsBackToOtcBilling [< 1 ms]
Not found: "Database=custom_billing_db;"
Failed!  - Failed: 2, Passed: 42, Skipped: 0, Total: 44
```

Note what makes these bite rather than accidentally pass: `ClearWriterDbVars()` clears **all three** `MSSQL_DB_*` names before each test, so a substituted read finds its sibling unset and falls through to the default — which the first assertion is written not to accept. Without that helper the substitution probe would have a false-negative mode of its own. It is deliberate and it is the right shape.

### The harder question the brief asked: is `ConnectionString()` a seam that exists for the test?

**No. It is the production call site, and I enumerated rather than reasoned.**

```
$ grep -rn "SeedWriter\.ConnectionString\|ConnectionString()" src/ tests/ --include="*.cs" | grep -v "/bin/\|/obj/"
```

Every hit classified. The only **production** callers of the three `ConnectionString()` methods are `src/Seed/Presentation/SeedRunner.cs:21`, `:22` and `:23`, and `SeedRunner.RunAsync` has exactly one caller anywhere — `src/Seed/Program.cs:10`:

```
$ grep -rn "SeedRunner" src/ tests/ --include="*.cs" | grep -v "/bin/\|/obj/"
src/Seed/Program.cs:10:    var summary = await SeedRunner.RunAsync().ConfigureAwait(false);
src/Seed/Presentation/SeedRunner.cs:17:public static class SeedRunner
tests/Seed.UnitTests/SeedDbConfigTests.cs:9:   (doc comment)
tests/Seed.UnitTests/SeedMongoConfigTests.cs:9: (doc comment)
```

Four hits, two of them doc comments, **zero test callers**. So `ConnectionString()` was not invented for the guard — it predates this feature (it is committed code from feature 12, tracked and clean against HEAD) and it is the method the seed job runs. The guard pins a real call site. That was this round's central risk and it does not obtain.

## Finding R2-5 — non-blocking, and it must be routed rather than narrated

The three new tests pin **which environment variable each writer reads**. Nothing pins **which connection string each writer's `DbContext` is opened with**. `SeedRunner.cs:21-28` does both jobs in two adjacent blocks:

```csharp
var ordersConnectionString = OrdersSeedWriter.ConnectionString();      // :21 — now guarded
var billingConnectionString = BillingSeedWriter.ConnectionString();    // :23 — now guarded
...
await using var ordersDb = OrdersSeedWriter.OpenDb(ordersConnectionString);   // :26 — unguarded
```

Substituting `billingConnectionString` at `:26` leaves `Seed.UnitTests` at **44/44** and produces D1's exact consequence: `dotnet run --project src/Seed` writes the Orders fixtures into the Billing database. `Seed.IntegrationTests` cannot see it either — it builds its own three connection strings at `tests/Seed.IntegrationTests/SeedIntegrationTests.cs:35-41` and calls the writer methods directly, never `SeedRunner.RunAsync`.

**Why this is not a rejection.** It is not an environment-variable read, so it is outside id 56's acceptance population; and it lies squarely inside the residual the round-2 report already discloses in D2 (*"plus `src/Seed/Program.cs → SeedRunner.RunAsync()`, which no test executes at all"*). The disclosure is honest and was made before I found this instance of it.

**Why it still gets an entry.** This repository's own record is that a residual recorded only in prose gets routed around — `SA-2` needed two assessments and 39 commits because two correct disclosures named nobody. R2-5 is the first *demonstrated* consequence of the composition-root residual, it is cheap to close, and *"a later feature will close it"* is the sentence I am not permitted to write.

### Routing (for the leader to file — I do not write backlog entries)

One entry, phase 14, covering the boundary this feature could not cross:

> **`composition_root_delegation_and_wiring_are_unguarded`** — the one line in each `Program.cs` that hands control to the extracted configuration, and the wiring inside `SeedRunner`, are guarded by nothing. Ten sites: `BillingProgramConfiguration.Configure`, `FulfillmentProgramConfiguration.Configure`, `GatewayProgramConfiguration.Configure`, `NotificationsProgramConfiguration.Configure`, `ProjectorProgramConfiguration.Configure`, Orders' three (`.ConfigureOutbox`, `.ConfigureAcceptance`, `.ConfigureSaga`), `src/Seed/Program.cs:10 → SeedRunner.RunAsync()`, and `SeedRunner.cs:26-28`'s connection-string-to-`DbContext` pairing. Measured: replacing `configure: BillingProgramConfiguration.Configure` with `configure: static _ => { }` leaves `Billing.UnitTests` 232/232 green (round-1 probe P17); substituting `billingConnectionString` at `SeedRunner.cs:26` leaves `Seed.UnitTests` 44/44 green (round-2 probe R2-5), seeding Orders fixtures into the Billing database. Acceptance should require **both** mutations to fail a named test, and should record which mechanism was chosen — `tests/Gateway.IntegrationTests/GatewayTestHost.cs:11` records a standing decision against `WebApplicationFactory`/`TestServer`, so reversing it is itself a decision to take at the gate, not inside the feature.

Two candidates already exist alongside it and should be weighed in the same entry rather than separately: id 67 (`design_time_dbcontext_factory_env_reads_are_unguarded`, filed by the leader for the exclusion I upheld in round 1) closes the last 20 of the 102 reads, and the guard-hardening loop's instrument (ids 60/61/63/64/65) is where the substitution family below belongs.

## Probe 2 — D2: judging the disclosure

**Honest and complete.** The report retracts the false claim in terms (*"That claim is wrong, and this corrects it"*), names the nine delegating references plus `Seed/Program.cs`, cites P17's green 232/232 as the measurement rather than asserting the gap in prose, and states what closing it would take — three candidate mechanisms, each costed, each with the reason it was not taken, including the correct note that a reflection assertion over the compiled `Program` type would be a **partial** guard and must not be sold as complete. It does not claim the residual away and it does not overstate what was built.

The only thing the disclosure did not anticipate is R2-5, which is a *consequence* of the residual it correctly names rather than a gap in the naming. Routed above.

## Probe 3 — D3: both counts re-derived, not accepted

Run by me against `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`, full output, every hit classified.

```
$ find "$N" -name "*.config.spec.ts" -not -path "*/node_modules/*" | wc -l
7
$ find "$N/apps" -name "*.config.ts" -not -path "*/node_modules/*" | wc -l
29
$ grep -rn "from '\..*main'" "$N" --include="*.spec.ts" | grep -v node_modules
apps/orders/src/orders-create-wire.integration.spec.ts:66:import { createOrdersNatsMicroserviceOptions } from './main';
apps/orders/src/orders-create-idempotent-replay.integration.spec.ts:40:import { createOrdersNatsMicroserviceOptions } from './main';
apps/orders/src/catalog-reference-list-wire.integration.spec.ts:42:import { createOrdersNatsMicroserviceOptions } from './main';
$ find "$N" -iname "main.spec.ts" -not -path "*/node_modules/*"
(empty)
```

**`7` and `29` are correct. `three` specs importing from `./main` is correct.** All three figures now match a command rather than a memory.

Three things my own enumeration adds, none of them a defect, all of them things #9 should inherit as numbers rather than impressions:

1. **There is a fourth importer of `./main`, and it is not a spec.** Widening the predicate from `--include="*.spec.ts"` to `--include="*.ts"` returns `apps/orders/src/test-support/saga-integration-harness.ts:32`. The report's claim is correct **as scoped**; the fuller count is four.
2. **Only one of #7's six `main.ts` files exports anything at all.** `grep -rn "^export" $N/apps/*/src/main.ts` returns exactly two hits, both in Orders — `apps/orders/src/main.ts:30` (`export interface OrdersNatsConnectionOptions`) and `:36` (`export function createOrdersNatsMicroserviceOptions`). So ledger row 1's *"#7's composition root is an ordinary ES module with named exports"* is true as a claim about the **language**, and true of #7's **practice** in 1 of 6 services. The property difference the row records is right; the impression that #7 broadly enjoyed a tested composition root is not, and the row would be stronger for saying **1 of 6**.
3. **The row's supporting claims check out, with the line numbers it omits.** `bootstrap()` is `async function bootstrap()` and never exported in all six — `billing:13`, `gateway:14`, `orders:49`, `fulfillment:13`, `projector:14`, `notifications:10`. Each `main.ts` holds exactly one `process.env` read, all of the form `Number(process.env.<SERVICE>_PORT ?? 300x)` inside that non-exported function — `orders:118`, `fulfillment:42`, `notifications:60`, `projector:78`, `gateway:30`, `billing:43`. The brief asked whether row 1 carries **file and line**; rows 1 and 5 cite `apps/orders/src/main.ts` without a line for the export. Minor, and I have put the missing line numbers on the record here so the ledger's claims are verifiable without re-deriving them.

**Ledger row 1 as re-framed is the right row.** It states the real property difference — an importable ES module with named exports on one side, `Program.<Main>$` with no source-nameable identifier on the other — cites the mechanism rather than scoring #7, and closes with the residual that is true of both stacks. That last sentence is the most useful thing in the ledger and it was not there in round 1.

## Ledger — the Guard column re-checked against `CLAUDE.md`'s "a row's Guard is a countable claim"

| Row | Guard claim | Can it fail? |
|---|---|---|
| 1 (re-framed) | the six `*ProgramConfigurationTests` | **Yes** — 16 of my round-1 probes, across three families, every one red |
| 2 | `GatewayMongoOptionsTests`, `OperatorIdentityLoaderTests`, `ProjectorProgramConfigurationTests` | **Yes** — round-1 P3, P8, P11, P15 |
| 3 | the non-default-value technique at every `options.X = …` call site | **Yes** — round-1 P9 (`Expected: 77 / Actual: 500`), the case a default-only assertion could not distinguish |
| 4 | **no guard**, routed to id 67 | Correctly declines, now says *what was tried and why nothing could fire*, and no longer over-reaches to "not owed at all". **Routing verified**: id 67 `design_time_dbcontext_factory_env_reads_are_unguarded` exists in `feature_list.json`, `pending` |
| 5 (new) | **no guard** for the boundary-crossing line; the 82 reads on the guarded side are armed | Honest. R2-5 is a further instance of the same unguarded boundary and is routed above |

No row makes a claim I could not check, and no row's named guard survived the mutation it exists to catch.

## Probe 4 — did the round disturb anything it said it did not?

**No. Verified three independent ways, none of them a re-reading of the report.**

1. **Timestamps.** Exactly one file under `src/` or `tests/` carries a round-2 mtime: `tests/Seed.UnitTests/SeedDbConfigTests.cs` at **15:40:59**. Every other file in the enumeration is either ≤ 14:51 (round-1 authoring) or 15:24–15:29 (**my own** round-1 probe restores). `src/Projector/ProjectorProgramConfiguration.cs` is 14:45:49 and was never reopened.
2. **Content.** `cmp` against snapshots taken **before** round 2 began: all six `*ProgramConfiguration.cs`, both Seed loader classes, the five Gateway/Projector `Options` classes and `src/Billing/Program.cs` are **byte-identical**. The three `*SeedWriter.cs` and `SeedRunner.cs` are byte-identical to HEAD (`git diff -- src/Seed/` empty), which for these — tracked files — is the stronger check.
3. **Counts.** My container-free subset is **1226** against round 1's **1223**. Exactly +3, all in `Seed.UnitTests` (41 → 44). No other project moved by a single test, which is what "the round touched nothing else" means operationally.

## Advisory A1 — the report is wrong about which files are tracked

`progress/impl_composition_root_env_reads.md`, D1's arming section: *"restore from the `cp` backup (never `git checkout --`, since these files are untracked)"*, and later *"these files are untracked, so `git status --short` rather than `git diff` is the check that applies"*.

**The three `*SeedWriter.cs` files and `SeedRunner.cs` are tracked** — `git ls-files --error-unmatch` returns all four. The *behaviour* was correct (a `cp` backup is what the protocol wants either way) and the conclusion was correct (the restores are clean), but the reasoning is inverted: for tracked files `git diff` is available and is the **strongest** available check, and I used it. `CLAUDE.md`'s arming clause turns on trackedness, and a wrong belief about which files are tracked is the precondition of both `git checkout --` incidents this repository has recorded. Worth correcting in the record; not worth a round.

## CHECKPOINTS.md — walked, round 2

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 — run by me this round

### C2 — state is coherent
- [x] at most one feature `in_progress` — `init.sh` reports **none**; id 56 was `in_review` and is set `done` by this verdict
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests associated with it
- [x] `progress/current.md` describes the active session — its `**Feature:**` line names id 56; **its body is stale** (it still says `Status: in_progress` and predates both review rounds), which `init.sh` explicitly cannot see. For the leader to rewrite at this transition, per its own note
- [x] every `blocked` feature records why — none blocked

### C3 — architecture is respected
- [x] no framework reference inside any `Domain/` folder — `Architecture.Tests` **16/16**, run by me
- [x] no cross-service database access — this round changed no persistence code; the seed writers each remain bound to their own service's `MSSQL_DB_*`, which is now the guarded property rather than the assumed one
- [x] no shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` — in the architecture suite I ran
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — untouched
- [x] every inter-service interaction classifiable as Kafka-fact or NATS-RPC — this feature introduces none
- [x] no stray debug logging, no context-free TODOs in the one changed file

### C4 — verification is real
- [x] `./quality.sh` passes — **not re-run by me**; corroborated independently by my 1226-test container-free subset and by the 18 `coverage.cobertura.xml` files from the implementer's run (15:47:31–15:55:21), which sum with the six integration projects to the reported 1575
- [x] domain tests are pure
- [x] integration tests use Testcontainers — unchanged
- [ ] coverage thresholds — no coverage gate is armed yet (feature 34 owns it); not assessable, not this feature's obligation
- [x] no Jest anywhere

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the 20 untracked paths are this feature's six source files, eleven test files, the collection definition, the impl report and this review
- [x] `progress/history.md` has an entry with an effort record — appended by this verdict
- [x] `feature_list.json` reflects the true state — id 56 set `done` by me, single-line edit, diff read
- [ ] the human has been told what was done and how to test manually — for the leader
- [x] Claude did not commit

### C6 — spec-driven development
Not applicable: id 56 is `sdd: false`. `init.sh` confirms SDD coherence for the 7 `sdd: true` features past `pending`.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7 — `init.sh` §5d, 6 files, `test-matrix.md` exempt
- [x] every deviation is a recorded amendment in both repositories — `SA-2`, applied to both, **still uncommitted**
- [x] the `R<n>` ids are #7's — this feature claims none, correctly
- [x] `n8n/workflows/*.json` unchanged
- [x] the black-box API script unchanged
- [x] `progress/history.md` effort records complete — this one appended
- [x] the README's benchmark section — untouched by this feature

## `R<n>` → test mapping

Unchanged from round 1 and re-verified: id 56 is `sdd: false`, claims no `R<n>`, and correctly does not edit `specs/shared/test-matrix.md`. The one adjacent requirement is **R43** (eager validation of `CREDIT_FAILURE_RATE`), already `DONE`; `BillingProgramConfigurationTests.Configure_Throws_WhenCreditFailureRateIsOutOfRange` and `..._ReadsEveryVariable_...` now close the gap that `BillingHostCreditFailureRateBootTests` structurally could not, and both fail under my round-1 probe P12. R43's row remains accurate; no `test-matrix.md` change is owed.

## The three mutation families — an answer for the guard-hardening loop's brief

The leader asked whether the instrument should run all three families over its population, or whether substitution is only meaningful where an identifier is a parameter rather than a constant. **All three, and the parameter/constant distinction is the wrong discriminator.**

What made deletion and corruption insufficient at D1 was not that `databaseEnvVar` was a parameter. Deletion could not apply — removing `ConnectionString()`'s body is a compile error, not a green suite. Corruption could not apply — the value is a constant in the source and never appeared among the values the test supplied, and `CLAUDE.md` already records that *"a corruption probe only bites on a field whose expected value the test supplied."* Substitution is the family that covers the remaining case: **a correct-looking constant that selects the wrong one of several equally valid things.**

The right discriminator is therefore a property of the **literal**, not of the parameter list: *does this literal name a member of a set whose other members are also present in this repository?* `MSSQL_DB_ORDERS` has three siblings; `"TrustServerCertificate=True;"` has none, and substituting it degenerates into corruption. That predicate is mechanically enumerable — build the candidate sets by grepping the literal families (`MSSQL_DB_*`, `MONGO_DB_*`, the `otc-*` client and consumer-group ids, the `*.v1` event types, the NATS subject strings, Mongo collection names) and let the instrument generate sibling swaps automatically.

**Why it earns its place rather than being a third chore.** Substitution is the only one of the three whose green suite hides *correct behaviour aimed at the wrong target*. Deletion produces missing behaviour; corruption produces wrong data; substitution produces a working system pointed at another service's database, topic or subject. In a six-service repository with a database, a topic set and a subject set per service, that is the failure that crosses a service boundary, and it is exactly what D1 and R2-5 are.

**One caution for the brief.** Substitution has a false-negative mode of its own: if the test does not clear the sibling identifier, the swapped read may fall back to a default and fail for the *default* reason rather than the *name* reason — or, worse, pick up a value the test happened to set for another purpose and pass. `SeedDbConfigTests.ClearWriterDbVars()` handles this deliberately and is the pattern to copy. An instrument that swaps blindly must check that the failure message names what it intended, or it will report a red that proves something else.

## Effort record

Appended to `progress/history.md`.

## Phase 13 is not closed

Remaining after id 56: **60, 61, 63, 64, 65** (one guard-hardening loop, and its brief should carry the three-family answer above) and **66** (`operator_note_reaches_the_timeline`, the entry `SA-2` makes satisfiable). Newly routed and awaiting the leader: the `composition_root_delegation_and_wiring_are_unguarded` entry above. **`SA-2` is applied byte-identically to both repositories and is still uncommitted** — it must be committed on its own, per the `SA-1` convention.
