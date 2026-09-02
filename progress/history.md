# History — append-only log

> One entry per completed feature. **The effort record is mandatory**: this file is
> assessment #8's measurement against the #7 baseline, and the trilogy's first
> empirical answer to how much a mature spec plus a mature harness accelerates a
> full re-implementation. Without honest effort numbers here, the benchmark the
> SDD adoption plan asked for does not exist.
>
> **Record what was NOT faster with the same care as what was.** An all-green
> benchmark is not a result, it is a lack of measurement. The #7 baseline lives in
> `peelmicro/order-to-cash-nestjs`, `progress/history.md`, feature by feature.

Entry format:

```markdown
## <feature_name> (id <n>, phase <n>) — <date>

**Effort:** <n> session(s), ~<n>h wall-clock
**#7 baseline:** <n> session(s), ~<n>h | no counterpart (new in #8)
**Spec:** specs/<name>/ | n/a (sdd: false) | reused from specs/shared/ (R<n>…)
**Tests:** <what was added, and the R<n> requirements they prove>

**What was built:**

**Deviations from the spec/plan:**

**What the reuse saved — and what it did not:**

**Notes for #9:**
```

---

## repo_bootstrap (id 1, phase 1) — 2026-08-31

**Effort:** 1 session, ~0.5h wall-clock
**#7 baseline:** 1 session, ~0.5h
**Spec:** n/a (sdd: false)
**Tests:** n/a — toolchain phase, no application code yet

**What was built:**

.NET 10 toolchain verified by scaffolding, building and running the three templates the build actually needs (`webapi`, `worker`, `xunit`), not by reading a version string; all eight key NuGet packages resolved and build-verified together. Repo-local `peelmicro` identity and account-explicit remote set. `global.json` (SDK 10.0.111, `rollForward: latestPatch`), `.nvmrc`, `.gitignore`, `.editorconfig` and a minimal honest README created.

**Deviations from the spec/plan:**

- `dotnet-ef` alignment deferred from Phase 1 to Phase 6, the phase that first uses it. Left unticked in the plan rather than quietly dropped.
- The cloned repository was completely empty (not even a first commit), so this phase's commit is the root commit and `git branch -M main` was needed.

**What the reuse saved — and what it did not:**

**Saved:** the GitHub two-account 403 that cost #7 a debugging session did not recur — it was written down in #7's plan, so it cost one `git remote set-url` before the first push, which then succeeded first time. Likewise the `/data/` gitignore trap that silently untracked 11 source files in #7: anchored from the start here, and verified in both directions with `git check-ignore`.

**Did not save:** everything stack-specific was new work — the `.editorconfig` C# section, `global.json`, the .NET template and package probes. Effort came out identical to #7's despite the reuse, because what #7 saved on was offset by .NET having more toolchain surface to pin than Node did.

**Notes for #9:**

Verify a toolchain by *using* it, not by asking its version. The probe that scaffolds, builds and tests a throwaway project is cheap and has caught real problems in both assessments. Also: pin the SDK/interpreter in a file whose failure mode is loud — `global.json` makes `dotnet` fail outright when unsatisfiable, which is exactly what a session check wants.

## harness_layer (id 2, phase 2) — 2026-08-31

**Effort:** 1 session, ~1h wall-clock
**#7 baseline:** 1 session, ~1.5h
**Spec:** n/a (sdd: false)
**Tests:** `init.sh` verified green and verified to exit 1 on all eight break cases

**What was built:**

`AGENTS.md`, `CHECKPOINTS.md`, `init.sh` and the six agent definitions copied from #7 and re-pointed to .NET, with model pinning left untouched. `CLAUDE.md` adapted: three rules translated (explicit DI registration plus a startup validation pass; one `BackgroundService` per transport; xUnit backend / Vitest web) and one added with no #7 ancestor — the JSON wire shape must match #7 byte for byte. `CHECKPOINTS.md` C7 inverted from "is this reusable by #8?" to "did it actually reuse it, and is the benchmark honest?". `feature_list.json` reset to 42 pending features keeping #7's ids, names and phases for row-to-row comparison, plus `cqrs_dispatcher` (id 43), flagged as having no #7 counterpart. Fresh `progress/`. `docs/PROCESS.md` re-pointed, §11 reset so #7's findings stay #7's.

**Deviations from the spec/plan:**

- The plan's option of porting `init.sh`'s Node backlog validator to Python was declined at the human gate. Node is present for `apps/web` anyway, the script is proven, and rewriting it would add effort to the benchmark while changing nothing. The oddity is documented in `CLAUDE.md` instead of engineered away.
- I had earlier proposed dropping several #7 "firefighting" features from the backlog. On inspection they were never backlog features at all — they were `impl_*.md` reports for work done inside existing features. Nothing was dropped; 41 + 1 = 42.

**What the reuse saved — and what it did not:**

Measured rather than asserted, by diffing each file against #7's pristine original:

| Artifact | Lines | Changed | % |
|---|---|---|---|
| `.claude/agents/leader.md` | 75 | 2 | 3% |
| `AGENTS.md` | 79 | 10 | 13% |
| `.claude/agents/suite_runner.md` | 45 | 9 | 20% |
| `init.sh` | 178 | 41 | 23% |
| `.claude/agents/reviewer.md` | 63 | 16 | 25% |
| `.claude/agents/spec_author.md` | 94 | 29 | 31% |
| `.claude/agents/implementer.md` | 67 | 26 | 39% |
| `.claude/agents/test_maintainer.md` | 39 | 16 | 41% |
| `CHECKPOINTS.md` | 69 | 29 | 42% |
| `docs/PROCESS.md` | 288 | 189 | 66% |
| `CLAUDE.md` | 144 | 98 | 68% |

**Pure orchestration ports almost free.** `leader.md` needed two lines, because "decompose, gate, never implement" contains nothing stack-specific. **Anything encoding conventions or accumulated evidence barely ports at all** — `CLAUDE.md` and `PROCESS.md` are effectively rewrites wearing a copy's clothes.

The honest summary is that the *harness* transferred and the *conventions* did not, and only the first is what #7 claimed was reusable. A one-third saving on wall-clock (1h vs 1.5h) is real but far smaller than "we copied it" suggests.

**Notes for #9:**

Expect the same split, and budget for it: the agent definitions and `AGENTS.md` are nearly free, `CLAUDE.md` and `PROCESS.md` are near-total rewrites every time. Also worth carrying: re-point by targeted patch against a pristine copy rather than by rewriting, so the diff is auditable afterwards — that is what made this measurement possible at all.

## shared_spec (id 3, phase 3) — 2026-08-31

**Effort:** 1 session, ~0.75h wall-clock
**#7 baseline:** 1 session, ~2.5h (2 `spec_author` passes + 1 amendment pass)
**Spec:** n/a (sdd: false) — this feature *is* the spec arriving
**Tests:** n/a — no code yet. Verification was `cmp` on every file, a stack-term sweep, and a YAML parse of both contracts

**What was built:**

Nothing was built. Seven specification files and four n8n workflow JSONs were copied from `peelmicro/order-to-cash-nestjs` at `aaabd59` (`specs/shared/` last touched by `8a3a3d3`). Six of the seven and all four workflows are **byte-identical**, proven by `cmp` and SHA-256 rather than asserted.

`test-matrix.md` is the single exception, reset by following **#7's own normative four-step recipe** rather than improvising: 63 Status cells to `TODO`; coverage counts to 0 green / 0 scoped / 63 not-yet-green, checked to sum to the Total row; the paragraphs labelled per-assessment asides deleted; the four passages narrating #7's realisation record removed. Columns 1–4 verified identical on all 63 rows. The file went 122 KB → 38 KB, which is the recipe working, not content lost.

**Deviations from the spec/plan:**

One, and it became `SA-1` — see below.

**What the reuse saved — and what it did not:**

**The largest saving so far by a wide margin: ~0.75h against #7's ~2.5h, and the gap understates it.** #7's 2.5h bought two `spec_author` passes and an amendment pass over 63 EARS requirements, an AsyncAPI document with 36 channels and 33 operations, and a 17-path OpenAPI contract. #8 paid for a copy, a proof, an audit and a read-through. This is the phase where "the spec is written once and reused" is most obviously true, and it should be quoted as such in the benchmark — with the caveat that reading the specification properly is *not* optional and did consume most of the 0.75h.

**Did not save:** the audit and the read-through do not shrink. Knowing the saga well enough to write Phase 8's triple-doc costs what it costs, whoever wrote the words.

**`SA-1` — the trilogy's first cross-repository amendment:**

`test-matrix.md`'s reset recipe told a new assessment which prose to delete by **listing the specific paragraphs in that copy**. Correct at the moment of reading; false the moment it had been followed. #9 inheriting the executed file from #8 would have read an inventory of content already gone, with no way to tell a completed step from a missed one. Steps 3 and 4 now describe the **class** rather than the instance, so the instruction stays true in every copy.

Applied to #7 and #8 in the same session, byte-identical, one line changed in #7, recorded in both repositories' `progress/history.md` and in #8's README register.

**Phase 25's audit could not have caught it.** That audit hunted stack terms; this is a self-reference defect, findable only by *executing* the recipe — which nobody had done until now. The first genuine act of reuse is also the first genuine test of the reuse instructions, and that is an argument for the trilogy's structure that #7 alone could not have made.

**Notes for #9:**

- **#7's C7 claim holds.** A sweep of the reusable part for `nestjs`, `drizzle`, `nuxt`, `mysql`, `kafkajs`, `typescript`, `vitest`, `supertest`, `vue`, `apps/`, `packages/` and `.ts` returned twelve hits, **all twelve the substring `nest` inside the word `honest`**. Zero real leaks across 313 KB. Start from the spec with confidence; audit anyway, and audit by *doing*, not by grepping.
- **Prove the copy, do not assert it.** `cmp` per file plus the source commit SHA in the commit message costs a minute and makes "verbatim" checkable by a stranger.
- **Server-sent events are fixed by the shared contract** (`§10`), not merely preferred. A WebSocket or SignalR substitution is a deviation to record, not an option the contract leaves open.

## infra_compose (id 4, phase 4) — 2026-08-31

**Effort:** 1 session, ~1.25h wall-clock
**#7 baseline:** 1 session, ~4h (implementation ~1.5h, then two review rounds)
**Spec:** n/a (sdd: false)
**Tests:** no code yet. Verification was the running stack: 12 services healthy, the four databases created, the app login exercised in each, bootstrap idempotency re-run, and the healthcheck's failure mode probed directly

**What was built:**

`docker-compose.infra.yml` — 15 services (12 long-running, 2 one-shots, SonarQube behind a profile): MS-SQL Server, MongoDB, Kafka (KRaft), Redpanda Console, kafka-exporter, NATS core, Mailpit, OTel Collector, Jaeger, Prometheus, Grafana, n8n, plus `kafka-init` and `n8n-init`. `.env.example` scoped to what this phase actually creates, and a `.env` from it.

**Reused byte-identically from #7** (`cmp`-verified, nine files): the Kafka topic script and its Dockerfile, the OTel Collector config and Dockerfile, `prometheus.yml`, the Grafana dashboard JSON and both provisioning files, the n8n import script.

**Written here**: `infra/mssql/entrypoint.sh` and `infra/mssql/init/01-create-databases.sql`. The MySQL image runs anything in `/docker-entrypoint-initdb.d`; the MS-SQL image has **no init hook of any kind** — it execs `sqlservr` and nothing else. The entrypoint starts the engine in the background, waits until it answers a query, bootstraps through `sqlcmd`, then `wait`s so the container's lifetime and exit status remain the engine's. The init script is idempotent by construction, because unlike MySQL's once-only hook it re-runs on every container start.

**Deviations from the spec/plan:**

- **`COMPOSE_PROJECT_NAME=otcnet`**, decided at the human gate. #7 uses `otc`, so every container and volume except the database engine's would have collided name-for-name and #8 would have opened #7's Grafana database, MongoDB read model and Kafka log directory. Logical database names stay `otc_*` — that is shared-spec parity. Host ports unchanged, so the two stacks run one at a time.
- **MS-SQL pinned to `2022-CU26-ubuntu-22.04`**, matching how #7 pins every image. `2022-latest` shares a manifest digest with no specific CU tag, so it is genuinely unidentifiable.
- **Healthcheck `retries` cut 30 → 10** after measuring the real startup. See below.

**What the reuse saved — and what it did not:**

**Saved, and more than the ~1.25h vs ~4h suggests.** #7 spent two review rounds on this phase; almost every defect those rounds found arrived here as a comment in a file I copied — the Kafka `KAFKA_LOG_DIRS` mount mismatch (its D1), the init-script-must-be-`.sh`-to-read-env reasoning (D5), the healthcheck-passes-during-init trap, the kafka-exporter rationale, the topic-set-must-match-exactly assertion. The nine reused files needed no thought at all.

**Did not save: the MS-SQL bootstrap, which is the single largest piece of genuinely new infrastructure in #8.** No amount of #7 reuse helps when the engine has no initialisation mechanism to reuse *into*. That piece was written, debugged and probed from scratch and accounts for most of the 1.25h.

**Two plan predictions were wrong, both in #8's favour, both recorded rather than dropped:**

| | Predicted | Measured | #7 |
|---|---|---|---|
| Cold start to all-healthy | materially worse than #7 | **36 s** from empty volumes | 35–42 s |
| MS-SQL container RAM | 1.5–2 GB | **1.04 GiB** | MySQL ~400 MB |

Total infra footprint 2 492 MiB across 12 containers. The engine answers 4–9 s after container start; bootstrap adds ~3 s.

**Notes for #9:**

- PostgreSQL *does* have `/docker-entrypoint-initdb.d`, so #9 gets #7's mechanism back and should be quicker here than #8 was. Do not read #8's number as the cost of "porting infra" in general — it is the cost of one engine lacking one hook.
- Pin the exact image tag and check the digest. `2022-latest` matching no specific CU digest is the concrete argument.
- Make the database healthcheck assert the *databases exist*, not that the engine answers. Every engine accepts connections before your bootstrap has run.
- Set `retries` from a measured startup, not by copying another repo's shape.

## messaging_topology (id 5, phase 4) — 2026-08-31

**Effort:** 1 session, ~0.25h wall-clock
**#7 baseline:** 1 session, ~3h (implementation ~1h, then two review rounds)
**Spec:** n/a (sdd: false) — the topology is `specs/shared/asyncapi.yaml`
**Tests:** `kafka-init` asserts its own result and exits non-zero on any mismatch; NATS verified functionally, not by reading a status page

**What was built:**

Nothing new. `infra/kafka/create-topics.sh` was reused byte-identically and parses the topic list out of the copied `asyncapi.yaml` at run time — it never hardcodes a topic name, and it fails loudly if the broker's non-internal topic set is not *exactly* the spec's, or if any topic exists with the wrong partition count or replication factor. Six topics created: three fact topics and three `.dlq` companions.

NATS subjects were confirmed against the same spec: **15 request subjects** plus their reply channels.

**Deviations from the spec/plan:**

- **The plan's NATS subject table was missing `billing.credit.release`** — it listed 14, the spec declares 15. Found by verifying against the spec rather than against the plan. The plan was corrected; `specs/shared/` was not touched, so this is a plan defect, not a spec amendment.

**What the reuse saved — and what it did not:**

This is the purest reuse win in the build so far: ~0.25h against ~3h, because the artifact is a script that reads a spec, and both the script and the spec were inherited. Nothing about Kafka topic creation is stack-specific, and #7 had already paid for two review rounds' worth of hardening.

**A finding about verification, not about Kafka:**

The report offered `curl -s localhost:8222/varz | grep -c jetstream` as proof NATS ran core-only, annotated with its expected output. It returns **1 whether JetStream is enabled or not** — `/varz` carries the key either way. The check could not fail, so it proved nothing while looking like verification, and it was **caught by the human at the gate, not by the agent that wrote it**.

The claim was true — confirmed afterwards by asking JetStream to create a stream and being told `no responders available`, i.e. the API is not listening at all. The generalisable form is recorded in `docs/PROCESS.md` §11.2: a check that cannot fail is worse than no check. The arming discipline `CLAUDE.md` demands of an implementer's tests applies equally to the commands offered to a reviewer as proof.

**Notes for #9:**

- The topic script ports untouched. Budget nothing for it.
- Verify a broker's *mode* functionally — ask it to do the thing it should refuse. Status pages name features they are not running.

## monorepo_scaffold (id 6, phase 5) — 2026-09-01

**Effort:** 1 session, ~3h wall-clock — implementation ~1.25h, review round 1 ~0.75h (**REJECTED**), fix pass ~0.5h, re-review round 2 ~0.5h
**#7 baseline:** 1 session, ~3.5h — TS7 validation spike ~1h, scaffold ~1.5h, review ~1h; **approved on the first pass**
**Spec:** n/a (sdd: false) — contract was feature 6's 5-item `acceptance` array in `feature_list.json` plus CLAUDE.md's non-negotiables
**Tests:** 10 xUnit tests in `tests/Architecture.Tests`, all armed and proven to fail on a real violation — 6 `DomainPurityTests` (EF Core, Confluent.Kafka, NATS.*, MongoDB.*, ASP.NET Core, System.Text.Json), 1 `DomainDecimalTests`, 1 `SharedKernelHasNoPackagesTests`, 2 `DomainAssembliesTests` (non-vacuity). No `R<n>` mapping — pre-spec phase.

**What was built:**

`OrderToCash.sln` (classic `.sln`; .NET 10's `dotnet new sln` now defaults to `.slnx`) with nine projects: `SharedKernel` and `Contracts` (no layer folders, placeholders for features 7–8) plus seven services — Gateway, Orders, Fulfillment, Billing, Notifications, Projector, Seed — each with `Domain/`, `Application/`, `Infrastructure/`, `Presentation/` folders inside **one** `.csproj`, `RootNamespace`/`AssemblyName` = `OrderToCash.<Project>`. `Directory.Build.props` carries enforcement (`net10.0`, `Nullable`, `ImplicitUsings`, `LangVersion 14.0`, `TreatWarningsAsErrors`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild`) and `.editorconfig` keeps severities — no duplication, with a comment in each file stating the split. `Directory.Packages.props` pins every package centrally with transitive pinning on. `quality.sh` runs format-check → build → test → coverage collection, propagating the first failing exit code.

**Deviations from the spec/plan:**

- `Gateway` got a `Domain/` folder and is covered by the purity rules even though it is presentation-only in CLAUDE.md's layer diagram. Kept symmetric with the other six deliberately; inert until phase 13.
- **`quality.sh` collects and prints coverage but does not gate it**, with an explicit `TODO(feature 34 — sonarqube_quality_gates, phase 21)` at the point where the gate belongs. This is the deviation that matters: #7 discovered its own coverage gate had been inert for twenty phases, so a script that prints a percentage next to a green tick is exactly the failure mode this repository exists to avoid. Naming the owning feature in the file is the difference between a deferral and a silent omission.

**What the reuse saved — and what it did not:**

**Saved:** the ~1h TypeScript-7 validation spike that dominated #7's equivalent phase had no counterpart here — the .NET toolchain was already pinned and probed in phase 1, so there was no language-version gamble to resolve. The four-layer folder shape, the service list, the ports and the naming conventions were all inherited decisions that cost nothing to re-derive.

**Did not save — and this is the honest headline: #8 was NOT meaningfully faster than #7 on this feature (~3h vs ~3.5h), and it needed a rejection round that #7 did not.** Two reasons, both worth recording. First, the artefact itself is almost entirely stack-specific: #7's domain-purity guard was an ESLint `no-restricted-imports` rule scoped to the **path glob** `apps/*/src/domain/**`; #8's is NetArchTest over **namespaces**. Nothing ported. Second — and this is the interesting part — **the translation silently lost a property the original had for free.** A path glob covers subdirectories automatically; the first implementation's `ResideInNamespaceEndingWith(".Domain")` is a literal suffix match that covers `OrderToCash.Orders.Domain` but *not* `…Domain.ValueObjects`, `…Domain.Events` or `…Domain.Errors` — precisely where CLAUDE.md says aggregates, value objects and domain events live from feature 7 onward. The reviewer caught it by placing a live `MongoDB.Bson.ObjectId` and a `System.Text.Json.JsonSerializer` call in a nested domain namespace and watching **all six purity tests stay green**. The implementer's arming evidence had been honest and complete — it simply never tested the nested case, so a truthful report gave false confidence. Fixed with a single shared `DomainNamespacePattern = @"(^|\.)Domain(\.|$)"` constant consumed by both rule families, plus two non-vacuity tests guarding the assembly set; re-review armed all six rules at nested namespaces and both new tests.

So the reuse dividend on this feature was roughly zero, and it was consumed by a defect that only existed *because* of the port. That is a result, not a failure to record.

**Process evidence (kept deliberately, not smoothed over):**

This feature took an implementer pass, a rejection, a fix pass and a re-review. The implementer appended its correction rather than rewriting its report, so both the defect and its repair are in the record. This is the second consecutive phase to surface the same class of problem — `messaging_topology` (id 5) offered a `grep -c jetstream` check that returned 1 whether JetStream ran or not — but with one difference worth the ink: **that one was caught by the human at the gate; this one was caught by the reviewer agent.** The guard-that-does-not-guard is the dominant defect class in this build, and the harness is now catching it one layer earlier than it did last phase.

**Notes for #9:**

- **When porting a rule between stacks, port its *scope*, not just its *intent*.** A path glob, a namespace predicate and a package boundary are not interchangeable; ask explicitly which one the original relied on and whether the translation still holds. FastAPI will face this again — Python has no namespace/assembly split at all, so the domain-purity guard will likely be an import-graph or module-path check, closer to #7's glob than to #8's namespace match.
- **Arm every guard at the shape the code will actually take, not the shape it has today.** Placeholder types in flat namespaces are not representative of a real domain layer; the arming case must use the sub-namespace/sub-package structure the next feature will create.
- **Add a non-vacuity test for any rule that selects a type set.** A rule over an empty selection passes and looks green. Breaking the selector here left 7 of 10 tests passing — only the explicit non-vacuity test caught it. Budget one such test per selector-based rule from the start; it is far cheaper than discovering the gap twenty phases later.
- Do not fake a coverage gate to make the quality script look complete. Print the number, name the feature that will enforce it, and leave the TODO in the file.

## shared_kernel (id 7, phase 5) — 2026-09-01

**Effort:** 1 session, ~1.25h wall-clock — implementation ~0.4h, leader-reopened architecture-coverage fix ~0.15h, review round 1 ~0.35h (**REJECTED**, six defects), fix pass ~0.25h, re-review round 2 ~0.1h. Wall-clock estimated from file timestamps across the session (06:48 → 07:55), not from a stopwatch — recorded as an estimate rather than dressed up as a measurement.
**#7 baseline:** 1 session, ~1.5h — implementation ~1h, review ~0.5h; **APPROVED on the first pass, zero defects**, reviewer mutation-probed 4/4 killed.
**Spec:** n/a (`sdd: false`). Contract = feature 7's 4-item `acceptance` array + `specs/shared/requirements.md` R1–R4 + `domain-model.md` §2 (value objects) + `CLAUDE.md`.
**Tests:** 32 xUnit tests in `tests/SharedKernel.UnitTests` (pure — test SDK, xunit, coverlet and one `ProjectReference`, nothing else) + 12 in `tests/Architecture.Tests` (was 10 before this feature). 91.3% line coverage on the SharedKernel-covering run. R1 (domain half), R2, R3, R4 proven; **every one of them armed by the reviewer independently**, not accepted from the implementer's table.

**What was built:**

`src/SharedKernel`, zero `PackageReference`, replacing feature 6's placeholder: `Money` (`readonly record struct`, `long MinorUnits` + format-validated ISO 4217 alpha-3, M1–M4, no `decimal`/`float`/`double` surface of any kind, no conversion operator, closed arithmetic, division deliberately not offered), `Quantity` (strictly positive `int`, plus a `From(double)` boundary that rejects fractional, `NaN`, infinite and out-of-`int`-range inputs as domain errors), `GLN` (13 digits, real GS1 mod-10), `OrderNumber` (`ORD-` + zero-padded sequence that grows rather than truncates, with `Parse`), `UniqueId`, `Entity` (identity equality by `Id` + runtime type), `AggregateRoot` (collects `IDomainEvent`s, `ClearDomainEvents`), `DomainError` (stable `Code`), and six named error types.

**Test-matrix flips:** R2, R3, R4 → green; R1 → **ratified scoped** (domain half green, API half explicitly deferred to the gateway feature, ratification named in the cell per matrix rule 3(b)). Coverage summary moved 0/0/63 → **3 green / 1 scoped / 59 not yet green**.

**Deviations from the spec/plan:**

- **The §7.1 fact envelope is not here.** #7's shared kernel carried it and flipped **R11**; #8 places it in `Contracts` (feature 8), so R11 correctly stays `TODO`. A placement difference, not a coverage gap.
- **`Quantity.From(double)` has no #7 counterpart.** An `int` constructor parameter makes "fractional" structurally unrepresentable, so without this overload the matrix's fractional-rejection case for R3 could not be written at all. It is the one deliberate floating-point boundary in the shared kernel, and it needed a named exemption in the new architecture rule — disclosed, narrow, and armed.
- **D5 — #7's sibling business references were not built.** `domain-model.md` §2.3 places `DES-######`, `INV-######` and `CR-######` in the shared-kernel section and `CLAUDE.md` lists all four together; **#7 built all four**, #8 built `OrderNumber` alone, because feature 7's title and acceptance array name exactly the seven types delivered. Recorded rather than silently built: either Fulfillment and Billing each grow their own copy of the same zero-padded-prefix formatter and `Parse`, or a later feature generalises `OrderNumber`'s existing `Prefix` + `MinimumSequenceDigits` shape once a second consumer exists. **A deliberate divergence from #7 with a named consequence is the kind of thing this file exists to carry.**

**What the reuse saved — and what it did not:**

**Saved: the domain code itself, completely.** Every value object came out correct on the first pass and nothing in `src/SharedKernel` was rejected, rewritten or re-armed at any point across four rounds — `Money.cs`, `GLN.cs`, `OrderNumber.cs`, `UniqueId.cs`, `Entity.cs`, `AggregateRoot.cs` and `DomainError.cs` are byte-identical between the reviewer's round-1 verification and the approved tree, and `Quantity.cs` moved by one guard clause. `domain-model.md` §2 is precise enough (M1–M4, the mod-10 wording, the `ORD-######` shape) that the implementation is close to transcription. That is the reuse dividend, and it is real.

**Did not save — and it is the same headline as `monorepo_scaffold`, which makes it a pattern rather than an anecdote: ~1.25h against #7's ~1.5h is a wash, and #8 needed a reopen and a rejection where #7 needed neither.** The reason is worth stating precisely: **every single defect found across all four rounds of this feature was in a *guard*, never in the domain code.** The spec ports; the *enforcement* of the spec does not port, because #7's enforcement was an ESLint path glob and #8's is reflection over namespaces and assemblies, and each translation loses properties the original had for free.

**Three of the four defect classes found in phases 4–5 are the same shape**, and that is the most valuable thing this build has produced so far:

| Phase | Defect | The guard | Why it did not guard |
|---|---|---|---|
| 4 `messaging_topology` | `grep -c jetstream` on `/varz` | returns 1 either way | a check that **cannot fail** |
| 5 `monorepo_scaffold` D1 | `ResideInNamespaceEndingWith(".Domain")` | flat namespaces only | scope narrower than the rule's intent |
| 5 `shared_kernel` (reopen) | domain purity + no-`decimal` rules | never scanned `SharedKernel` at all — twice over | selector excluded the purest domain code in the repository, including `Money`, the type the `decimal` rule exists for |
| 5 `shared_kernel` D1 | R1's absence assertion + `DomainDecimalTests` | `typeof(decimal)` only | M1 bans "decimal, **floating-point** or fixed-point"; a `double` accessor on `Money` passed 42/42 |

Caught, in order, by: **the human at the gate**, **the reviewer agent**, **the leader**, and **the reviewer agent again**. The detection is moving earlier and the classes are converging, which is the harness working — but four instances in two phases says the cost of this build is not in writing code, it is in proving the guards are alive.

**Two smaller results worth carrying:**

- **The reviewer's D1 fix instructions produced a better answer than either option offered.** Rather than widening the rule named `decimal` or bolting on a duplicate, the implementer factored a shared `FindTypeOffences` behind **two** named rules — so *what is inspected* cannot drift while *what is forbidden* stays legible in the test name.
- **Fixing D1 exposed a second, unreported bug of the identical shape**: both absence checks skipped every `IsSpecialName` method to avoid double-counting property accessors, which silently also skipped every operator overload — so both docstrings' claim to cover conversion operators had always been false. Found by the implementer unprompted; armed by the reviewer, whose probe (`explicit operator double` on `Money`) was the row missing from the implementer's own table.

**D6 — a review finding promoted into the harness:**

The reviewer hit, and generalised, a hazard that undermines *every* arming table in this repository: restoring an armed file with a timestamp-preserving copy (`cp -a`/`cp -p`) leaves MSBuild's incremental check believing the correctly-reverted source is older than its already-compiled, still-armed output, so the confirming run silently tests the wrong binary. It produced a false **red** in the review; the false **green** direction is equally reachable. `CLAUDE.md` now carries an explicit arming protocol requiring a forced rebuild after every restore, and `docs/PROCESS.md` §11.2 records the general form — *a check is only worth what the artefact it ran against is worth* — alongside phase 4's `grep -c jetstream`. Both fix passes and both reviews used the corrected protocol thereafter. The coordinator's own re-verification of D1 independently produced a false "still green" first attempt (two project paths passed to `dotnet test`), which is the same lesson one layer out: **a check that cannot distinguish "the guard did not fire" from "the run did not happen" is not a check.**

**Notes for #9:**

- **Budget for guards, not for domain code.** The spec makes the value objects near-mechanical in any language; the whole cost is proving the enforcement is alive. Python has neither namespaces nor assemblies, so #9's purity and `decimal`/`float` bans will be an import-graph or module-path check — closer to #7's glob than to #8's reflection — and it will lose *different* properties in translation. Ask explicitly, for every ported rule: which member kinds does it inspect (fields? properties? return types? parameters? operators? dunder methods?), and arm one of each.
- **Ban the whole invariant, not the word you remember.** M1 says "decimal, floating-point or fixed-point". #8 guarded one of the three for a full round. In Python this bites harder — `float` is the *default* numeric type, so a `Money.amount` that is accidentally a float will not even look wrong.
- **After restoring an armed file, force the rebuild before the confirming run.** Python has no compile step but has `__pycache__`, stale `.pyc` files and import caching inside a long-lived test session; the same class of false result is available. This is D6, and it is now in `CLAUDE.md`.
- **An allowlist inside a guard needs a key that fully identifies what was reviewed.** #8's floating-point exemption keys on (type, method *name*, parameter *name*) and therefore silently covers a future `Quantity.From(float value)` too — proven by the reviewer, logged as advisory D7. Include the type.
- **Ratify a scoped test-matrix row when you create it.** R1's API half genuinely belongs to the gateway feature, and splitting the row was right — but matrix rule 3(b) wants the deferral to carry somebody else's name, and reconstructing that ten phases later is far more expensive than one sentence at the time.
- **Recompute check digits yourself.** The five "valid" GLNs held up under two independent formulations, but one of them is `0000000000000`, which is valid under *any* weighting and would survive a swapped-weights bug. #7's exhaustive single-digit-mutation sweep (justified by `gcd(3,10)=1`) is still the stronger pattern and is still unclaimed by #8.

---

## contracts_package (id 8, phase 5) — 2026-09-01

**Effort:** 1 session, ~2.7h wall-clock — human gate + oracle capture ~1.9h (08:02 → 09:58: the leader captured 12 real envelopes from #7's retained Kafka topics, established the MySQL-`json`-key-order finding on an `eventId` present in both #7's outbox and its topic, took the envelope-byte-exact / payload-semantically-equal ruling at the human gate, amended `CLAUDE.md` twice — the wire rule and the arming protocol — and rewrote feature 8's `acceptance` from 3 items to 5), implementation ~0.25h (10:03 → 10:17, from file timestamps), review ~0.5h. **APPROVED on the first pass**, zero blocking defects, four advisories. Wall-clock from file timestamps across the session, not a stopwatch — an estimate, recorded as one.
**#7 baseline:** 1 session, ~2.5h — implementation ~2h (including two generator-tooling surprises: a single-line `{}` root-interface regex bug and `title`-beats-key naming), review ~0.5h; **APPROVED first pass**, 22 tests.
**Spec:** n/a (`sdd: false`). Contract = feature 8's five-item `acceptance` array (rewritten at today's gate) + `specs/shared/asyncapi.yaml` + `CLAUDE.md`'s amended wire rule. No `R<n>` claimed; `specs/shared/test-matrix.md` untouched by this feature.
**Tests:** 21 xUnit tests in `tests/Contracts.UnitTests` (pure — test SDK, xunit, coverlet, one `ProjectReference`): 12 golden-envelope parity tests, 1 `stock.rejected.v1` shape test, 2 spec-parsing completeness tests, 6 `JsonWire` option tests. 95.8% line coverage on the Contracts-covering run. Solution total 65/65. **Seven reviewer arming probes, all killed** — including two applied to `specs/shared/asyncapi.yaml` itself (restored via `git checkout --`, md5 and a `diff` against the #7 checkout both confirming byte-clean), which is the only way to prove the completeness tests read the spec at test time rather than baking it into the assembly.

**What was built:**

`src/Contracts` — hand-written wire types, replacing feature 6's placeholder. `Wire/JsonWire.cs` (the one `JsonSerializerOptions`: camelCase, `WhenWritingNull`, compact, relaxed escaping, custom instant converter), `Wire/InstantJsonConverter.cs` (`yyyy-MM-ddTHH:mm:ss.fffZ` — the BCL's default `"O"` round-trip format writes seven fraction digits and `+00:00` and would have failed byte-exactness on all twelve goldens), `Envelopes/Envelope.cs` (one `Envelope<TPayload>` record whose seven positional parameters *are* the wire field order), six shared structures, **fourteen** payload records, and `Facts/FactCatalog.cs` as the registry the completeness test walks. Plus `tests/Contracts.UnitTests` with `JsonEquivalence` — a ~100-line recursive `JsonElement` comparison where object key order is immaterial and array order is significant.

**Deviations from #7, all deliberate:**

- **#7 generated its contract types; #8 hand-wrote them.** Decided at the human gate. #7 ran `openapi-typescript` and `json-schema-to-typescript` over the two specs with a `contracts:check` drift gate; .NET's AsyncAPI 3.0 codegen story is thinner, and the C# realisation wanted decisions no generator makes for you — one generic `Envelope<TPayload>` instead of fourteen wrappers, `long` minor units rather than `number`, primitives on the wire rather than `SharedKernel` value objects. The cost of hand-writing is the drift risk #7 bought off with `contracts:check`; **#8 replaces that with the two spec-parsing completeness tests**, which catch a spec-side addition (proven: a probe `const` inserted into `asyncapi.yaml` fails the suite with no rebuild) but would not catch a hand-written type drifting in a way the spec's `required:` list does not constrain.
- **#8 gained a parity oracle #7 never had.** Twelve real #7 wire envelopes, captured from its retained Kafka topics, committed under `tests/Contracts.UnitTests/GoldenEnvelopes/`. #7 could only assert its types against its own specs; #8 asserts against #7's actual bytes. This is the single strongest artefact produced in phase 5 and it is the reason the trilogy's parity claim is now testable rather than asserted — **#9 inherits it and should assert against the same twelve files.**
- **The wire rule was split, and it is a #8 convention, not a spec amendment.** The goldens show payload keys ordered by length then alphabetically — MySQL's `json` column normalisation reaching the wire through #7's outbox relay, verified on one `eventId` present in both stores. #8 uses `nvarchar(max)`; #9 on PostgreSQL could not comply either. So the envelope is byte-exact and the payload is semantically equal, key order unasserted. `specs/shared/` is silent on key ordering and **did not change**; the ruling is recorded in `CLAUDE.md` with its evidence.
- **The brief said 13 facts; the spec says 14.** The implementer read `components.schemas` instead of trusting the parenthetical and built all fourteen (`order.saga_failed.v1` is the extra). Because the completeness test parses the spec, building thirteen would have gone red — which is exactly what a spec-parsing test is for.
- **`stock.rejected.v1` has no golden** (#7's retained topics held no instance of the rare race). Disclosed in three places; its wire-byte parity with real #7 output is **unproven and stays unproven** until an instance is captured.
- **OpenAPI REST DTOs are entirely deferred to feature 25 and siblings 26, 40, 41**, named explicitly. `openapi.yaml`'s 57 schemas are 19 primitives shared with `asyncapi.yaml` (already covered by `Guid`/`string`/`long` on the payload records) plus ~38 REST bodies with no live caller until the Gateway exists.

**What the reuse saved — and what it did not:**

**Saved: writing the types, almost entirely.** Twenty wire types — fourteen payloads and six shared structures — went from spec to code in about fifteen minutes, and my independent property-by-property walk found all twenty exact against `asyncapi.yaml`: no missing field, no extra field, and every spec-optional field exactly the nullable C# parameter. `asyncapi.yaml` is precise enough that this is transcription, and the reuse dividend is real and large *on this part*.

**Did not save: the total.** ~2.7h against #7's ~2.5h. **86% of #8's elapsed time went to work #7 never did at all** — capturing the goldens, diagnosing why their payload key order looked wrong, ruling on it at the human gate, amending `CLAUDE.md`, and rewriting the acceptance criteria from 3 items to 5. #7's 3-item acceptance ("a test asserts a serialised envelope is byte-identical to #7's for the same input") was **unsatisfiable as written** for #8, because "#7's bytes" turned out to contain a MySQL storage artifact. Discovering that, and deciding what parity actually means across three storage engines, is the whole difference — and it is the third consecutive phase-5 feature where **the spec ported cleanly and the verification did not**.

**The phase-5 pattern, now with three instances:** `monorepo_scaffold` (guards translated from an ESLint glob to namespace reflection, losing scope), `shared_kernel` (four defects, every one in a guard, none in domain code), `contracts_package` (types near-free, the *oracle* for the types costing ~1.9h). **The reusable asset in this trilogy is the specification; the non-reusable cost is proving the new stack satisfies it.** For a benchmark whose headline is "does reuse make the third build faster", that is the finding — and on raw wall-clock, phase 5's answer is *no, it is a wash*, three times in a row.

**Two smaller results worth carrying:**

- **Arming beats eyeballing, in both directions.** I had provisionally written `EveryServiceMustUseTheSameSharedOptionsInstance` up as a defect — `Assert.Same` on the same static field read twice reads as a test that cannot fail. Probing it (field → `=> Build()` property) showed it kills a realistic refactor mutant and is the only test that does. The repository's standing lesson is that a green guard may be dead; the converse also holds — **a guard that looks dead may be alive, and the probe costs less than the argument.**
- **A spec-side probe is a different claim from a code-side probe.** The implementer armed the completeness test by deleting a `FactCatalog` entry; that proves the comparison runs, not that the spec is read at test time. Inserting a fake `const` into `asyncapi.yaml` and watching the suite go red **with no rebuild** proves the acceptance item as written. When a test's whole point is "notice a change in file X", the probe must edit file X.

**Notes for #9:**

- **Use the same twelve golden envelopes.** They are in `order-to-cash-dotnet/tests/Contracts.UnitTests/GoldenEnvelopes/`. Assert the envelope byte-exact and the payload semantically equal; PostgreSQL's `jsonb` will reorder keys differently again (it sorts by key length then bytewise, and `json` vs `jsonb` behave differently), which is a third data point for the same finding and a third reason not to assert payload key order.
- **Check your serialiser's default instant format before writing a single payload type.** Python's `datetime.isoformat()` emits `+00:00` and six-digit microseconds; #7's wire is `.fffZ`. This is one line of converter and it silently breaks every envelope assertion if missed.
- **Make the completeness test parse the spec, and arm it by editing the spec.** Both halves — fact types and required fields — and give each a non-vacuity assertion (`count > 0`), because a regex that matches nothing passes over an empty set. #7 shipped this oracle first and called it "the single test that catches silent type-dropping"; it has now earned that description in two stacks.
- **Budget the oracle, not the types.** If #9 hand-writes rather than generates, the types are an afternoon; deciding what "the same wire" means across a third storage engine is the real work, and it is best done at a human gate before any code exists — as it was here.
- **Do not read a doc-comment `R<n>` citation as coverage.** `Envelope.cs` cites R11 and validates nothing; R11 is `outbox_and_idempotency`'s row and wants a *refusal* test. The same trap exists in any stack where the envelope type and the envelope's invariants live in different features.

---

## db_orders (id 9, phase 6) — 2026-09-01

**Effort:** 1 session, **~2.3h wall-clock** end to end (10:17 → 12:35, from feature 8's close to this approval) — implementation pass ~1.4h (leader brief + implementer; feature files written 11:40–11:45, first migration `20260901094309` generated 09:43 UTC), **review ~0.35h → REJECTED**, fix pass ~0.27h (12:05 → 12:21, second migration `20260901100855`), re-review ~0.25h → **APPROVED**. Wall-clock from file timestamps and migration ids, not a stopwatch — an estimate, recorded as one; the leader's briefing time is inside the implementation figure and is not separately measurable.
**#7 baseline:** 1 session, ~1.5h — implementation ~1h, review ~0.5h; **APPROVED on the first pass**, zero blocking defects, 5 integration tests, 9 tables.
**Spec:** n/a (`sdd: false`). Contract = feature 9's 3-item `acceptance` array + `Order To Cash - Databases.EN.md` §4 (the authoritative table definitions) + `CLAUDE.md`'s DB-naming conventions. No `R<n>` claimed; `specs/shared/` untouched.
**Tests:** **12** xUnit integration tests in `tests/Orders.IntegrationTests`, all against a real MS-SQL 2022 container (Testcontainers.MsSql, image tag pinned to `docker-compose.infra.yml`'s own `mssql` service — never a mock, never SQLite-in-memory): `MigrationTests` ×2, `SchemaColumnTypeTests` ×2, `IndexTests` ×1, `ForeignKeyTests` ×1, `OutboxSeqIdentityTests` ×2, `UniqueConstraintTests` ×4. Solution total **77**. **Every assertion reads the live database** — `INFORMATION_SCHEMA.COLUMNS`, `sys.indexes`/`sys.index_columns`, `sys.foreign_keys`/`sys.foreign_key_columns`, `COLUMNPROPERTY(...,'IsIdentity')` — never EF's own model metadata. **Five implementer arming probes and four reviewer arming probes, all killed.**
**Gates:** `./quality.sh` green (**1 m 07 s** — up from seconds; it now starts a real MS-SQL container and therefore *requires a running Docker daemon*), `dotnet format --verify-no-changes` clean, `./init.sh` exit 0, NetArchTest 12/12 with the new `Microsoft.EntityFrameworkCore.SqlServer` reference in `Orders.csproj`.
**Schema:** 11 tables, 17 spec-named indexes with verified column order, 8 foreign keys, all matching Databases doc §4 and #7's committed `apps/orders/drizzle/0000_bizarre_champions.sql`.

**REJECTED on the first pass — two blocking defects, both incomplete translation of a fully-decided schema:**

- **D1: seven of the eight foreign keys named in §4.1/§4.2 were silently absent.** The POCOs carried `currency_id`, `company_id`, `retailer_id`, `product_id` as bare `Guid`s with no `HasOne`/`HasForeignKey`, so EF emitted no constraint. Live `sys.foreign_keys` returned **1** where #7's DDL emits **8**. Found by querying the database and diffing against #7's committed SQL — *not* readable from the implementer's report, which claimed coverage of "every table in §4" and listed `currency_id uniqueidentifier` with no mention that the FK was gone.
- **D2: `order_number_sequences.next_value` was `bigint`; §4.2 and #7 both say `int`** — and the guard meant to catch exactly that drift **asserted the wrong value**, locking the divergence in.

Four advisories (D3 `orders.request_id` filtered-index requirement → recorded against phase 14; D4 whitelist with no closure assertion; D5 stale `progress/current.md`; D6 hardcoded password fallback). D1, D2, D4, D6 fixed in one pass; the migration was **regenerated, not hand-edited**.

**What the reuse saved — and what it did not:**

**Saved: the design, completely — and one whole future migration.** Not one minute went to deciding a table shape, a column type, an index choice or a nullability. The rejection was never about a *wrong* shape; the 11 tables and all 17 named indexes were right on the first submission, down to column order. Two concrete dividends beyond that:

1. **#8 built the final schema in one pass.** #7's `db_orders` shipped **9** tables and had to add `saga_commands` and `saga_ignored_facts` later, at the saga features. #8 shipped all **11** immediately, because the Databases doc describes the end state rather than the state at phase 6.
2. **#8 shipped `outbox` correct the first time.** #7's own `db_orders` review recorded two *binding* carry-forwards to feature 14: `outbox` lacked `causation_id`, and `occurred_at` was `DATETIME(0)` with no deterministic relay tiebreak. #8 has `causation_id uniqueidentifier NOT NULL`, `occurred_at datetime2(3)`, and `seq bigint IDENTITY` with the `(published_at, seq)` poll index — all present at `db_orders` time. **That is #7's migration `0002` never needing to be written.** It is the clearest instance so far in this trilogy of reuse paying in *avoided rework* rather than in typing speed, and it does not show up in the wall-clock at all.

**Did not save: the wall-clock. #8 was ~50% slower than #7 — 2.3h against 1.5h — while holding the answer key.** The whole gap is the extra round: rejection + fix + re-review ≈ 0.87h. And the reason for the extra round is the finding.

**A decided schema removes design risk and leaves translation risk — and translation risk is not smaller.** Both blocking defects were transcription failures against a document that stated the right answer in plain text. §4 annotates `company_id | char(36) FK → companies`; §4.2 says `next_value int` verbatim. Nothing had to be worked out. Having the answer key does not make you copy it correctly, and — the sharper half — **it makes the reviewer's job harder, because the plausibility of the output goes up while its fidelity does not.** A green 10-test suite, every test genuinely interrogating the real database, sat on top of seven missing constraints.

**The proximate cause was the translation target's ergonomics, not the spec.** In Drizzle, a foreign key is written *inside* the column declaration — `char('currency_id', {length: 36}).notNull().references(() => currencies.id)` — so #7 could barely declare the column without confronting the FK. In EF Core, the column mapping (`builder.Property(...).HasColumnName("currency_id")`) and the relationship (`builder.HasOne<Currency>().WithMany().HasForeignKey(...)`) are **separate, optional statements in different parts of the file**. The FK is opt-in, its absence is invisible, and it was omitted seven times in a row without anything looking wrong. The spec was identical; the affordance was not.

**And the guard was missing, not merely the constraint.** This is the repository's standing lesson at its fourth occurrence: nothing in the suite read `sys.foreign_keys`, so seven absent constraints produced a fully green run. The fix pass closed both halves — the constraints *and* an exact-set assertion over table, column, referenced table, referenced column and delete rule.

**Two smaller results worth carrying:**

- **EF Core 10 guards model-vs-migration drift for free, and it is stronger than expected.** I armed `ForeignKeyTests` by deleting an FK from the *configuration* expecting to report a gap — these tests build their schema from `MigrateAsync()`, so a configuration-only edit should leave the created database untouched and the test green. Instead EF raised `PendingModelChangesWarning` **as an error**, failing the test at the migrate step. Configuration and migration cannot drift silently here. #7 had to reason about this explicitly (committed SQL + own migrator, `drizzle-kit push` banned); .NET gives it away. **Probing the guard you expect to be dead is how you find the one that is alive** — the converse of feature 8's lesson, and now proven in both directions.
- **The confirming run can consume a stale artefact even when the restore was correct.** Mid-probe, a `--no-build` run reported 4 failures with `MissingMethodException: Method not found: '...et_Default.get_ProcessedEvents()'`, and the immediately preceding identical command reported a different count; a clean rebuild gave 12/12. All false, all mine: alternating `dotnet build --no-incremental` with `dotnet test --no-build` left the test assembly and `OrderToCash.Orders.dll` out of step. `CLAUDE.md`'s arming protocol mandates a forced rebuild after restore but does not cover the confirming run consuming what that rebuild invalidated. **After restoring, let `dotnet test` build for itself; never pair `--no-incremental` with `--no-build`.** Non-determinism between two consecutive identical commands is the tell. This is #7's D6 in a new disguise, in the false-**red** direction.

**Notes for #9:**

- **The FK defect may not reproduce, and that is itself the measurement.** SQLAlchemy declares foreign keys *inside* the column — `Column('currency_id', Uuid, ForeignKey('currencies.id'))` — which is Drizzle's affordance, not EF's. If #9 gets all eight FKs right first time with no extra effort, that is direct evidence the defect was an ORM-ergonomics artefact rather than a spec or an attention failure. **Record whether it happened, either way.** This is the cleanest natural experiment the trilogy has produced so far.
- **Write the `sys.foreign_keys`-equivalent assertion before you write the schema.** In PostgreSQL that is `information_schema.table_constraints` + `key_column_usage` + `referential_constraints` (for the delete rule), or `pg_constraint`. Assert the **exact set** with referenced table, referenced column and delete rule — an additive existence check passes over a ninth FK and over one pointing at the wrong table.
- **Assert closure on columns and tables, not just presence.** A whitelist proves everything expected exists; it never proves nothing extra does. Both halves, and give the expectation table its own non-vacuity assertion (`len(expected_tables) == 11`) so the check cannot pass over a truncated whitelist.
- **Interrogate the database, never the ORM's model.** Asking SQLAlchemy what SQLAlchemy thinks proves nothing about PostgreSQL. #8's suite got this right from the first submission and it is why the review could be conducted at all — the defects were *findable* precisely because the tests were honest about their source.
- **Budget for a rejection round on the "easy" ported features.** Phase 6 is a transcription task with the answer key supplied, and it still cost #8 an extra 0.87h and two blocking defects. The features where the spec fully decides the outcome are exactly the ones where reviewers and implementers both relax.
- **Expect the schema to arrive complete.** #9 should ship 11 tables and a correct `outbox` at phase 6, as #8 did — not #7's 9 tables plus a later `0002`. That dividend is real, repeatable, and invisible in wall-clock.

**The phase-5/6 pattern, now at four instances:** `monorepo_scaffold` (guards lost scope in translation), `shared_kernel` (four defects, every one in a guard), `contracts_package` (types near-free, the oracle costing ~1.9h), `db_orders` (schema free, fidelity costing a rejection round). **The specification reuses; the verification does not.** Four features in, on raw wall-clock the reuse has not once made #8 faster than #7 — and the reason has been different each time, which is a more useful result than a uniform one.

---

## db_fulfillment (id 10, phase 6) — 2026-09-01

**Effort:** 1 session, **~0.55h wall-clock** (12:25 → 13:00, from feature 9's approval to this one) — implementation ~0.35h (leader brief + implementer; entity files written 12:29:32–12:29:53, test files 12:32:09–12:34:09, migration `20260901103111` generated 10:31 UTC, report at 12:48:07), review ~0.2h → **APPROVED on the first pass**, zero blocking defects, four advisories. Wall-clock from file timestamps and migration ids, not a stopwatch — an estimate, recorded as one.
**#7 baseline:** 1 session, ~1.25h (implementation ~0.75h, review ~0.5h), APPROVED first pass, 8 tests, 6 tables.
**#8 result: ~0.55h against #7's ~1.25h — the first feature in this repository that was genuinely faster than #7, by a factor of ~2.3.** It is also the first APPROVED-first-pass database feature.
**Spec:** n/a (`sdd: false`). Contract = the 2-item `acceptance` array + Databases doc §5 / §4.3 / §3 + `CLAUDE.md`.
**Tests:** `tests/Fulfillment.IntegrationTests` — 19/19 via Testcontainers `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` (same pin as compose): migrations from empty and re-applied after a genuine database **drop**; per-table round-trips through a second `DbContext`; `INFORMATION_SCHEMA` column/type assertions plus a **table-and-column closure** assertion; `sys.foreign_keys` **exact-set** assertion with referenced column and delete rule; `sys.indexes` with `key_ordinal` column-order matching; `COLUMNPROPERTY` identity check plus a real two-row increment; 5 unique-constraint probes including 2 pair-controls. Solution-wide `dotnet test`: **96 passed**.
**Gates:** `./quality.sh` green (**1 m 28.8 s**, up from feature 9's 1 m 07 s), `dotnet format --verify-no-changes` clean, `./init.sh` exit 0, NetArchTest 12/12 with the new `Microsoft.EntityFrameworkCore.SqlServer` reference in `Fulfillment.csproj`.
**Schema:** 7 tables, 9 spec-named indexes with verified column order, **exactly 2 foreign keys** (`reservations.stock_id → stock` NO_ACTION; `despatch_items.despatch_id → despatches` CASCADE), all matching Databases doc §5 and #7's committed `apps/fulfillment/drizzle/*.sql`. Verified by the reviewer against the live `otcnet-mssql` and against #7's SQL files, not from the report.

**The headline result — the warning worked, and it is worth separating from the spec reuse:**

Feature 9 was rejected for two defects (D1: seven of eight foreign keys silently missing and unguarded; D2: a sequence counter typed `bigint` where the spec says `int`, locked in by a test asserting the wrong value). Feature 10 was briefed on both. **Both were avoided, and avoided structurally rather than by care:**

- Both FKs are in the **first** generated migration; there was no fix pass. The FK **test** exists and asserts the closed set with referenced table, referenced column and delete rule — feature 9 had neither the constraints nor a test, and the review's point was that adding constraints without an assertion closes the symptom and leaves the defect.
- `next_value` is `int` in the entity's first version, and `DespatchNumberSequence.cs:17-25` carries an XML doc comment quoting §3 and naming the feature-9 mistake it is avoiding. **The reason lives on the type, not in a progress file.**
- The table/column closure assertion (feature 9's advisory D4) was written from the start rather than added under review.

**Two mechanisms, and they must be counted separately.** The **spec-reuse** dividend is the schema: 7 tables, 9 indexes, 2 FKs, every column width decided in advance. That dividend was equally available to feature 9 — and feature 9 still got two of them wrong. The **within-run learning** dividend is the three guards that exist here only because a review found their absence one feature earlier. **The spec told both features what to build; only the review told feature 10 how to prove it.** The ~0.7h saved against #7 is mostly the first mechanism (pattern copied file-for-file from feature 9, exactly as #7 predicted in its own notes); the *absence of a rejection round* is entirely the second.

**The one-pass dividend is larger here than at `db_orders`.** #7 needed **three** migrations for this database: `0000_nappy_mad_thinker.sql` (6 tables, an `outbox` with no `causation_id`/`seq`/`trace_parent`), `0001_outbox_causation_seq_trace_parent.sql`, and `0002_despatch_number_sequence_and_order_reference_unique.sql` (the `despatch_number_sequences` table and the `despatches.order_reference` unique that #7's own review flagged as missing against F8 "at most one DespatchAdvice per orderReference"). #8's single `InitialCreate` contains all of it. **That is #7's `0001` and `0002` never needing to be written** — and #7's `db_fulfillment` review's one carry-forward advisory is satisfied before the aggregate feature it was addressed to exists.

**Reliability-table parity was verified now rather than at feature 11.** `outbox` and `processed_events` in `otc_fulfillment` are byte-identical to `otc_orders`'s on the live databases — column names, ordinal positions, types, lengths, precision, nullability, identity, and every index including composite column order. The `diff` is empty. Feature 11's cross-context parity test will have nothing to find between these two, which is the payoff of the task's instruction to **copy** the Orders configurations rather than re-derive them from §4.3's prose. Checking it a feature early cost minutes; finding a divergence at feature 11 would have cost two features' rework.

**Four advisories, none blocking:** A1 the round-trip tests never assert a timestamp column on read-back and the report claims they assert "every field" (small over-claim; #7's equivalent explicitly covered UTC datetimes; and behind it sits a real forward risk — MS-SQL `datetime2` has no timezone, so EF returns `DateTimeKind.Unspecified`, which the first `occurred_at` ordering comparison will meet); A2 `ForeignKeyTests.cs:123-124` asserts the count before the diagnostic, so a failure reports `Expected: 2 / Actual: 3` and discards the list naming *which* FK — **this is feature 9's A2 copied through verbatim into a file correctly modelled on feature 9's**; A3 `IndexTests` is presence-only with no closure over the index set, the one object type where closure was not applied (deliberate and disclosed, since the EF FK-supporting indexes would fail an exact set); A4 `progress/current.md` stale again.

**Process note, and it is not a small one:** `progress/current.md` still said *"Feature: `db_orders` — rejected on review, fix pass in flight"* throughout feature 10. C2's fourth box, **second consecutive occurrence in #8**, and #7 logged the same miss at the same point. The file's own Notes section contains the sentence *"The leader is not exempt from the checkpoints it enforces"*, written one feature earlier about this exact miss. **A lesson written down is not a habit installed** — which is the same distinction, one level up, as the one between the spec reuse and the review learning above.

**Timing risk, raised with the number:** `./quality.sh` has gone seconds → **1 m 07 s** (feature 9, 1 MS-SQL container) → **1 m 28.8 s** (feature 10, 2 containers), and it now requires a running Docker daemon to pass at all. Four more services get integration suites, and Kafka/NATS containers start slower than MS-SQL; linear extrapolation puts the gate past **3 minutes**. That is the threshold where a gate stops being run per change and starts being run per commit — and #7's history records exactly what happens next. Two cheap mitigations exist **now** and get expensive later: share one MS-SQL container across the database suites via a single collection fixture (the two fixtures are already near-identical, and the per-test `CreateFreshDatabaseAsync` pattern means only the container would be shared, not the schema), or split `quality.sh` into fast and full modes. Do it at feature 11, with two suites to reconcile rather than six.

**Notes for #9:**

- **Hand the reviewer's findings forward, not just the spec.** This feature is the cleanest evidence in the trilogy so far that the two reuse mechanisms are separable: copying `specs/shared/` gives you the schema for free, and gives you **nothing** about how to prove you transcribed it. #9 will start with #8's feature-9 defects unknown to it unless someone puts `progress/review_db_orders.md` in the implementer's brief. Budget for that as an explicit step.
- **The second database feature costs roughly half the first — #7 said so, and #8 reproduced it** (~0.55h vs ~2.3h here, ~1.25h vs ~1.5h there). The ratio is steeper in #8 because #8's first one was rejected. Expect the same shape and copy the plumbing file-for-file.
- **Verify reliability-table parity when the *second* database lands, not when the parity test's own feature lands.** It is a `diff` of two `INFORMATION_SCHEMA` dumps and it costs minutes.
- **Put the reason for a non-obvious type on the type.** The `int`-not-`bigint` decision survives here because it is an XML doc comment on `DespatchNumberSequence.NextValue` citing the spec section and the earlier mistake — not because it is in a progress file nobody will open at feature 25.
- **Probe closure with an extra *table*, not just an extra column.** Injecting a table that also carries a foreign key kills the table-set assertion and the FK-set assertion in one cycle, and proves both are genuinely closed rather than additive.

**The phase-5/6 pattern, now at five instances:** `monorepo_scaffold` (guards lost in translation), `shared_kernel` (four defects, every one in a guard), `contracts_package` (types near-free, the oracle ~1.9h), `db_orders` (schema free, fidelity costing a rejection round), **`db_fulfillment` (schema free, fidelity free — because the previous feature's review had already been paid for)**. Four features in, the reuse had not once made #8 faster than #7. The fifth did, by 2.3×. **The reuse of the specification pays on the second use of a pattern, not the first — and what pays on the first use is a review that was allowed to reject.**

---

## db_billing (id 11, phase 6) — 2026-09-01 — **closes Phase 6**

**Effort:** 1 session, **~0.75h wall-clock** (13:00 → 13:45, from feature 10's approval to this one) — implementation ~0.5h (leader brief + implementer; entity files written 13:02:39–13:03, Billing migration `20260901110439` and Notifications migration `20260901110547` generated 11:04/11:05 UTC, test files 13:06–13:11, five arming probes and report to 13:29:28), review ~0.25h → **APPROVED on the first pass**, zero blocking defects, six advisories. Wall-clock from file timestamps and migration ids, not a stopwatch — an estimate, recorded as one.
**#7 baseline:** 1 session, ~0.75h (implementation ~0.5h, review ~0.25h), APPROVED first pass, 10 tests, 7 tables, 1 database.
**#8 result: ~0.75h against #7's ~0.75h — parity, no saving.** And that is the honest headline: **#8 matched #7's best time in the phase while delivering two `DbContext`s instead of one, 20 tables' worth of schema across four databases, 29 tests instead of 10, and a cross-context parity test #7 had written earlier and elsewhere.** More delivered in the same time is a real result; it is simply not a *wall-clock* result, and the ratio column hides it.
**Spec:** n/a (`sdd: false`). Contract = the 3-item `acceptance` array + Databases doc §6 / §7 / §4.3 / §3 + `CLAUDE.md`.
**Tests:** `tests/Billing.IntegrationTests` **22/22** + `tests/Notifications.IntegrationTests` **7/7**, both via Testcontainers `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` (same pin as compose): migrations from empty and re-applied after a genuine database **drop** in both projects; per-table round-trips through a second `DbContext`; `INFORMATION_SCHEMA` column/type assertions plus **table-and-column closure**; `sys.foreign_keys` **exact-set** assertion with referenced column and delete rule; `sys.indexes` with `key_ordinal` column-order matching; `COLUMNPROPERTY` identity check plus a real two-row increment; 7 unique-constraint probes including 2 pair-controls and the R47/R48 `payments.payment_reference` key; and the parity test. Solution-wide `dotnet test`: **125 passed**.
**Gates:** `./quality.sh` green (**2 m 39 s**, up from feature 10's 1 m 28.8 s), `dotnet format --verify-no-changes` clean, build 0 warnings, `./init.sh` exit 0, NetArchTest 12/12 with `Microsoft.EntityFrameworkCore.SqlServer` now in both `Billing.csproj` and `Notifications.csproj`.
**Schema:** `otc_billing` — 8 tables, 12 spec-named indexes with verified column order, **exactly 3 foreign keys** (`credit_items.credit_id → credits` NO_ACTION; `invoice_items.invoice_id → invoices` **CASCADE**; `payments.invoice_id → invoices` NO_ACTION). `otc_notifications` — **exactly 1 table**, `processed_events`, no outbox, zero foreign keys. All verified by the reviewer against the live `otcnet-mssql`, against Databases doc §6/§7 and against #7's committed `apps/billing/drizzle/*.sql`, not from the report.

**The distinctive deliverable, and it survived being attacked three ways.** The cross-context parity test (`ReliabilityTableParityTests`) migrates four fresh real databases and compares `outbox` (Orders/Fulfillment/Billing) and `processed_events` (all four) from `INFORMATION_SCHEMA.COLUMNS` and the `sys.indexes` catalogue — never EF metadata — against `Orders` as the star reference, naming both sides on every divergence. The reviewer armed it independently of the implementer's single length-probe:

- **Length probe, in Notifications:** `consumer` `nvarchar(50)` → `nvarchar(80)` → **FAIL**, `processed_events.consumer: character_maximum_length Orders=50 vs Notifications=80`. It is **not** a names-only comparison.
- **Index-drop probe, in Notifications and separately in Billing:** → **FAIL** both times, `index 'IX_...' present in Orders but missing in <context>`. The index sets are genuinely compared, with uniqueness and column order.
- **Silently-narrow probe:** both Notifications mutations killed the test *by name*, proving `otc_notifications` is genuinely inside the `processed_events` half of the comparison rather than quietly skipped — the guard-that-does-not-guard shape this build has hit repeatedly, and it is not present here.
- **Closure probe:** a rogue `outbox` table injected into `otc_notifications` killed **two** assertions in **two different test projects**, while the parity test's `outbox` half correctly passed over it — confirming the report's honest statement that the absence is proven by a dedicated closure test, not by omission from a dictionary.

Independently of the test, the reviewer diffed the four live databases directly: `outbox` byte-identical across three (12 columns, all indexes), `processed_events` byte-identical across four (5 columns, all indexes). Five `diff`s, all empty.

**Both of feature 9's blocking defects avoided again, on the largest schema of the four.** `invoice_number_sequences.next_value` is `int` — the third and last of the three sequence tables D2 named — in the entity's first version, in the configuration, in the migration, in the live database and in the test, with the reason as an **XML doc comment on the property** citing §6, #7's `0002_*.sql` and feature 9's mistake by name. All three foreign keys are in the first generated migration with a closed-set assertion over table, column, referenced table, referenced column and delete rule; the delete rules were checked against #7's `0000_brown_hammerhead.sql:94-96`, and the reviewer checked that claim rather than taking it.

**Two things this feature did that neither predecessor did:**

- **It closed an inherited advisory instead of copying it through.** `ForeignKeyTests` now asserts the diagnostic before the count, so a broken FK reports `credit_items.credit_id: no foreign key found` rather than `Expected: 3 / Actual: 2`. That was feature 10's A2, which was feature 9's A2, which that review had named as "a good file's warts copied along with its shape". Third occurrence, first fix — a one-line change noticed while the file was open.
- **It disclosed a gap its predecessor had over-claimed.** Feature 10's report said its round-trips "assert every field survived unchanged" when the datetimes were excluded (that review's A1). This report states the gap plainly, names where it was first recorded, and declines to re-file it as new.

**It also reported its own false signal.** A `quality.sh` run launched concurrently with `--no-incremental` builds had coverlet fail on a missing DLL; the run still summarised as success because **that project's 22 tests never executed**. Caught by counting per-project `Passed!` lines, discarded, re-run serially. A *silent gap* — the hardest of the three failure signals to notice — and reporting it rather than quietly re-running is the behaviour this harness exists to produce.

**One-pass dividend, same shape as feature 10 and again invisible in wall-clock.** #7 needed **three** migrations for `otc_billing`: `0000` (7 tables, an `outbox` with no `causation_id`/`seq`/`trace_parent`), `0001` (those three plus the relay poll index), `0002` (`invoice_number_sequences`, the `invoices.order_reference` unique that #7's own `db_billing` review raised as a B7 advisory and only closed at feature 21, and `idx_invoices_status_invoice_date`). #8's single `InitialCreate` contains all of it. **#7's `0001` and `0002` never needed to be written, and #7's own carry-forward advisory is satisfied ten features before the feature it was addressed to.**

**Six advisories, none blocking:** A1 `IndexTests` is still presence-only with no closure over the index set (**third consecutive feature**; deliberate and disclosed, because an exact set would fail on the two EF FK-supporting indexes — but the "schema is still changing" justification is running out); A2 the parity test compares six column properties but **not `IsIdentity`**, and `outbox.seq` is the one column where that matters — a `bigint` without `IDENTITY` would pass the parity test, guarded only by the three per-context `OutboxSeqIdentityTests` (verified live as `IsIdentity = 1` in all three; record against feature 14, the relay); A3 round-trip tests still do not assert timestamps on read-back, with the `DateTimeKind.Unspecified` forward risk unchanged (inherited, correctly disclosed); A4 the parity test lives inside `Billing.IntegrationTests`, which therefore project-references three other services — test-only and sanctioned, but #7 put its equivalent in a neutral `apps/seed`, which is the better shape; A5 `progress/current.md` is one status transition stale (**but C2's fourth box is met for the first time in three features** — it names the right session, written as the feature opened); A6 the coverage output cannot be gated on in its current shape (see below).

**A finding for feature 34, recorded nineteen features early because it is cheapest now.** `quality.sh` prints **seven** per-test-project line-rates over **overlapping** assembly sets: 95.8% / 91.3% / 85.0% / 77.3% / 68.0% / **20.1%** / 0.0%. The 20.1% is not a Notifications problem — it is the Notifications *test project* measured against `SharedKernel` + `Contracts` + `Notifications`, and `OrderToCash.SharedKernel` appears in six of the seven reports at six different rates. **"≥60% overall" is not computable from this output, and neither is "≥80% domain."** Feature 34 needs a *merged* solution-level report before it can gate anything, and `CLAUDE.md`'s requirement that the gate be "verified to fail when breached" cannot be honoured until the number being gated means something.

**Timing, now with the bigger number:** `./quality.sh` has gone seconds → 1 m 07 s (1 container) → 1 m 28.8 s (2) → **2 m 39 s (4)**. Per-suite container time is 72 s of the 159 s. Feature 10's review recommended consolidating onto one shared MS-SQL container **"at feature 11, with two suites to reconcile rather than six"**; that was not done, and it now costs four near-identical fixtures instead of two. Not feature 11's defect — the advisory was addressed to the leader, and the feature correctly copied the established pattern rather than inventing a new one mid-feature — but **the cheapest moment to consolidate has now passed once, and it will pass again.** At this trajectory the gate crosses 4 minutes before Phase 21, and it already requires a running Docker daemon to pass at all.

---

## Phase 6 closing assessment — three features, one rejection, and a dividend that converged to zero

| | #7 (NestJS) | #8 (.NET) | Ratio |
|---|---|---|---|
| `db_orders` | ~1.5h, approved first pass | **~2.3h**, **REJECTED**, fix pass, re-review | 1.5× **slower** |
| `db_fulfillment` | ~1.25h, approved first pass | **~0.55h**, approved first pass | **2.3× faster** |
| `db_billing` | ~0.75h, approved first pass | **~0.75h**, approved first pass | **1.0× — parity** |
| **Phase 6 total** | **~3.5h**, 3 databases, 22 tables, ~23 tests | **~3.6h**, **4** databases, 20 tables, **60** tests | **~1.03× — no saving** |

**Did feature 10's effect persist, decay, or was feature 10 simply the easier schema? The evidence supports: it persisted on quality, and it vanished on speed — and those are two different questions the ratio column blurs together.**

**On quality it persisted, and "easier schema" does not explain it away.** Billing is the *largest* of the four: 8 tables against Fulfillment's 7, three FKs against two, twelve indexes against nine, plus a second `DbContext` and a cross-context test neither predecessor had to write. Every guard feature 10 adopted under review pressure appears here unprompted and first-time — FKs in the first migration with a closed-set assertion, `int` counter with the reason on the type, table/column closure from the start, every assertion read from `INFORMATION_SCHEMA` rather than the ORM. And it went further: it **closed** an inherited advisory rather than copying it a third time, and it **disclosed** a gap its predecessor's report had over-claimed. That is a feature reading the previous review and acting on what was left open, not a feature repeating a pattern.

**On speed the dividend converged to zero, for a boring and predictable reason.** #7's `db_billing` took ~0.75h because it was **#7's third database** — #7 had already learned the pattern twice and was at its own floor. That floor is set by how much code must be typed, not by how much thinking must be done, and #8 had strictly more to type: two contexts, 29 tests, and a parity test #7 had written earlier and elsewhere.

**The curve across three points is coherent, and it predicts the opposite of the naive expectation.** Feature 9 (#7 still learning, #8 rejected): 1.5× slower. Feature 10 (#7 half-learned, #8 fully briefed by a review): 2.3× faster. Feature 11 (#7 fully learned, #8 fully briefed): parity. **The reuse dividend is largest where the baseline was still learning and smallest where the baseline had already learned** — so the *later* features in a phase should show smaller reuse gains than the middle ones, not larger. Reuse does not compound within a phase; it front-loads and then flattens against the baseline's own learning curve.

**The number that should not be smoothed over:** Phase 6 cost #8 **~3.6h against #7's ~3.5h**, on a phase where the entire schema was handed over in advance, in a document, with #7's committed SQL available for corroboration. **The rejection round at feature 9 ate the whole dividend of features 10 and 11 combined.** The specification reused; the verification did not.

**The phase-5/6 pattern, now at seven instances:** `monorepo_scaffold` (guards lost in translation), `shared_kernel` (four defects, every one in a guard), `contracts_package` (types near-free, the oracle ~1.9h), `db_orders` (schema free, fidelity costing a rejection round), `db_fulfillment` (schema free, fidelity free — because the previous review had already been paid for), **`db_billing` (schema free, fidelity free, and no time saved — because #7's baseline had already paid the same learning cost one feature earlier)**. **The spec is free, the proof is not, and one rejection costs more than two clean features save.**

**Notes for #9:**

- **Write the cross-context parity test as its own project, not inside the last service's test suite.** #7 put it in `apps/seed`; #8 put it in `tests/Billing.IntegrationTests`, which now project-references three other services for test-only reasons. In Python the equivalent temptation is to hang it off whichever package was built last. A dedicated `tests/parity/` with all four metadata modules imported is one file and avoids the ownership question entirely.
- **Compare *identity/serial-ness*, not just type, in the parity test.** #8's parity test compares name, ordinal, type, length, datetime precision, nullability, index name, uniqueness and column order — and misses `IsIdentity`. In PostgreSQL that is `column_default LIKE 'nextval%'` or `is_identity` in `information_schema.columns`. `outbox.seq` is the one column where it matters, and it is guarded only by three separate per-context tests.
- **Probe the parity test at a *length* and at an *index*, not just at a type.** A parity test that compares column names and types but not lengths or index sets is half a parity test and looks identical from the outside. Also probe it by mutating the **narrowest** context (the `processed_events`-only one) — that is where "included in the comparison" is most likely to have been quietly dropped.
- **Expect no wall-clock saving on the third database, and do not read that as a regression.** #7's third took ~0.75h and #8's third took ~0.75h. The reuse dividend appears on the *second* use of a pattern and flattens on the third, because the baseline has learned by then too. Record what was delivered in the time, not only the time.
- **Budget the phase, not the feature.** Phase 6 is a transcription task with the answer key supplied and it still came out at parity across three features, because one rejection cost more than two clean features saved. If #9 wants a phase-level dividend, the place to spend it is on getting feature 9 right the first time — the guards, not the schema.
- **Hand the reviewer's findings forward, not just the spec.** Restated from feature 10 because feature 11 is the evidence it was right: this feature closed an advisory and disclosed a gap *because both were written down in the previous review file*. Copying `specs/shared/` buys none of that.

---

## seed_job (id 12, phase 7) — 2026-09-01 — **closes Phase 7**

**Effort:** 1 session, **~1.9h wall-clock** (15:01 → 16:55, from the first seed source file to approval) across **two review rounds** — implementation ~0.55h (round 1: source files 15:01:01 → 15:35:07), review ~0.4h (round 1: 15:35 → 16:00:25), fix ~0.3h (round 2: 16:00 → 16:18:41, oracle fixture at 16:04:48, new project at 16:06, arming restored at 16:18:41), re-review ~0.6h (round 2: 16:18 → 16:55, dominated by five container-backed mutation probes and three full `quality.sh` runs). **REJECTED on the first pass** — one blocking defect (D1) plus five advisories — **APPROVED on the second**, zero blocking defects, five advisories carried. Wall-clock from file timestamps, not a stopwatch; an estimate, recorded as one.
**#7 baseline:** 1 session, ~0.5h — implementation ~22 min (file timestamps 10:39–11:01), review ~1h. **APPROVED on the first pass, zero defects.**
**#8 result: ~0.85h of authoring against #7's ~22 min — roughly 2.3× slower to write — and a rejection round on top.** No saving. On a feature where the entire dataset and the entire derivation scheme were handed over in executable, runnable form.
**Spec:** n/a (`sdd: false`). Contract = the 4-item `acceptance` array + Databases doc §8 (`order_timeline` shape) + `specs/shared/domain-model.md` (GLN check digit, money as minor units) + `specs/shared/saga.md` (fact sequence and compensation ordering) + #7's `apps/seed/src/` as the parity source.
**Tests:** `tests/Seed.UnitTests` **34/34** (pure — no `Testcontainers`, `MongoClient`, `DbContext` or broker reference anywhere in the project; runs in **192 ms** with no container runtime) + `tests/Seed.IntegrationTests` **5/5** via real Testcontainers `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` and `mongo:8.3.8`, both the same pins as compose. Solution-wide: **164 passed**.
**Gates:** `./quality.sh` green — **1 m 40.8 s** on an idle host, **down** from Phase 6's 2 m 39 s despite two more test projects and 39 more tests, because the D6 unit/integration split took 34 tests out from behind a container start. `dotnet format --verify-no-changes` clean, build 0 warnings, `./init.sh` exit 0, NetArchTest 12/12 with `OrderToCash.Seed.Domain` genuinely in scope.
**Dataset:** 3 currencies, 12 products, 7 retailers, 22 companies, 154 credit lines, 215 stock rows, 6 orders (5 completed + 1 cancelled, `credit_rejected`, total `24999`), 11 order lines, 50 already-published outbox rows across three databases, 6 MongoDB `order_timeline` documents.

**The verification that makes this entry worth reading: #8's seed was proved equal to #7's by *executing #7*, not by comparing prose.** The reviewer copied #7's own `apps/seed/src/deterministic.ts`, `clock.ts`, `data/*.data.ts` and `writers/mongo.writer.ts` plus `packages/shared-kernel/src/domain/*` into a scratch directory, repointed only the two unresolvable module specifiers (`@otc/shared-kernel`, `mongodb`), ran them under `node --experimental-transform-types`, and set-diffed the output against the live `otcnet-mssql` and `otcnet-mongodb` databases:

- **413 reference rows** — currencies, products, retailers, companies, credits, stock — value-diffed on **every business column** (ids, EANs, GLNs, prices, credit limits, stock levels, thresholds): **0 missing, 0 extra, 0 differences**.
- **6 orders and 11 order lines** including ids, dates, amounts, status, `cancellationReason` and `updatedAt`: **0 differences**.
- **50 outbox rows** across three databases — id, `eventId`, `eventType`, `aggregateId`, `correlationId`, `causationId`, `occurredAt`, `publishedAt` and the **parsed payload**: **0 semantic differences**.
- **All 6 `order_timeline` documents**, field by field against `§8` and against #7's own `toTimelineDocument` output: **0 differences**.
- The **17 hardcoded parity-oracle values** in `DeterministicParityTests.cs` all reproduced from #7's real modules — and #7's own `deterministic.spec.ts` contains **no** hardcoded values, confirming they could only have come from executing #7, never from copying its tests or recording #8's own output.
- The **deliberately-preserved wart** (`hex[13..16]`, skipping `hex[12]`) confirmed load-bearing: "fixing" it to `hex[12..15]` changes the third hyphen group of every id (`4507`→`4250` for `currency:USD`), so all seven `[InlineData]` rows would fail.

**The rejection, and why it was right.** The dataset was perfect on the first pass. The **guard over it was not**, in exactly the artefact the implementer's own `progress/current.md` had named as the feature's risk — *"a wrong shape here would surface five phases later as a projector bug, which is an expensive place to find it"* — and then not armed. The reviewer's two mutation probes against a **green 38-test suite**:

- **blanking every `causationId` in every seeded `order_timeline` document → 38/38 still passed.** The only assertion on the field was `Assert.NotEqual(Guid.Empty.ToString(), e.CausationId)`, true for `""`, for `null`, and for any wrong GUID.
- **corrupting every timeline `totals.totalAmount` by one cent → 38/38 still passed.** The only assertion was `Assert.True(TotalAmount > 0)`.

`causationId` is the one that mattered. #7's own `mongo.writer.ts` states that a document seeded without real causal edges *"stays on the eventId fallback permanently, even after the projector's own boot migration runs, **because the migration never invents an edge that was never recorded**"* — so a blank edge here is silent **and unrecoverable**, in code with no live caller until Phase 12. This is precisely CLAUDE.md's *"with double force where the branch has no live caller yet, because integration harnesses cannot reach it"* clause, and the eighth instance of the guard-that-does-not-guard pattern in this build.

**The fix, and how it was checked for the obvious cheat.** A committed oracle fixture (`OracleFixtures/order_timeline_from_number7.json`, 6 documents from #7's own `toTimelineDocument`) plus a value-level test asserting every field of every live document against it. The risk was a fix that arms exactly the two published probes; the reviewer ran **five**, of which **three were never published** — `items[].productCode`, `retailer.name`, `company.gln`, `updatedAt` and the totals — and **all five failed by name**. Independently, the reviewer diffed the committed fixture against the one **it had generated itself hours earlier**, before the fix existed: **0 differences across all six documents.** Two independent executions of #7's source by different agents, identical output — the fixture is genuinely #7's, not #8's own output written down.

**A reviewer error, recorded because a benchmark that only records the implementer's mistakes is not a measurement.** Round 1 raised D2, that 14 of 50 outbox payloads differ from #7 in JSON **key order**, citing CLAUDE.md's *"must match #7 byte for byte"*. **That wording was superseded in Phase 5 at the human gate** (`ba0ca8b`); the rule on disk reads *"envelope byte-exact, payload semantically equal"* and explains inline that #7's key ordering is **MySQL's `json` column normalisation** leaking onto its wire via the relay's read-back — a storage artifact #8's `nvarchar(max)` cannot and should not reproduce, and #9's PostgreSQL `jsonb` could not at all. **The reviewer's own evidence — 0 semantic differences across all 50 payloads — was proof of compliance, not of a defect.** D2 is withdrawn; `StockReservedPayload` was never touched. Root cause: the reviewer quoted the **auto-injected** copy of `CLAUDE.md` in its context, which carried the pre-amendment `b15e5cc` text, instead of the file on disk. **Correction adopted, and it generalises to every agent here: never enforce a convention from the injected `CLAUDE.md`; `grep` it from disk.** This repository amends its own conventions at human gates mid-project by design, so the injected snapshot is *expected* to go stale — a reviewer enforcing a stale rule is the guard-that-does-not-guard pattern inverted, a guard that fires on something no longer true. It cost one spurious advisory here; on a rule with teeth it would have cost a spurious rejection.

**A measurement caveat for feature 34 and Phase 21.** `./quality.sh` was measured twice on identical code and returned **1 m 40.8 s** and **13 m 03.3 s** — a **7.8× spread**. The slow run was taken while the host was still saturated from the reviewer's own container-backed probes (1-min load average 41.65, 15-min 66.21); every container-backed suite inflated ~4× in lockstep. There were no leaked containers. **The gate's wall-clock is dominated by contended container startup, not by test work**, so a fast/full split or CI timeout budget derived from a single measurement on a loaded machine will be wrong by most of an order of magnitude. Record the host state next to the number.

**Five advisories carried:** A1 a stale `csproj` comment naming a file that does not exist (`Seed.IntegrationTests.csproj:32`); A2 the `detail` comparison iterates the *expected* keys, so an extra key in a live `detail` block would pass — close it when the projector writes `detail` for real at Phase 12; A3 the idempotency checksum still covers a subset of tables (it catches drift, not a rewrite confined to the omitted ones) — raised round 1, still true, still not blocking; A4 the coverage gate remains inert by design, nine overlapping per-project line-rates that do not sum to anything gateable, deferred to feature 34 with an in-file rationale forbidding a fake gate meanwhile; A5 the injected-`CLAUDE.md`-is-a-stale-cache process finding above, addressed to the leader.

**Acceptance item 4 remains deferred, correctly and honestly.** The row-count/checksum comparison against #7's **live** MySQL needs #7's containers running alongside #8's — a decision the human has not taken. The report says plainly that it is not proven now, and the tests are structured to accept it additively: oracle values are `[InlineData]` rows on standalone theories, `ComputeChecksumAsync` is a single named reusable static, and row-count expectations are named constants. Nothing needs rewriting to add the source later.

### The benchmark question — #7 had to *invent* the derivation scheme and the dataset; #8 received both in executable form. Did that show as a saving?

**No. #8 was ~2.3× slower to author and needed a rejection round, on the feature with the highest possible reuse ratio in the whole project.** That is the headline and it should not be softened.

**Phase 6 closed with "the specification reused, the verification did not." Phase 7 sharpens that into something more useful, because here even the *specification* was not merely reused — it was reused as running code.** #7 handed over `deterministicId`, `makeGln`, `makeEan13`, 22 companies, 12 products, seven retailers, 154 credit lines, 215 stock rows and six fabricated saga histories, all of it executable, all of it correct. #8's port reproduced every byte of it on the **first** pass — the reviewer proved that on 413 rows, 50 payloads and 6 documents, and found **not one** data defect in either round. **The dataset transferred perfectly. The feature still failed its first review.**

**The reason is the sharpest finding of the phase: the reuse dividend is bounded by what the reference implementation *proved*, not by what it *produced*.** #7 produced a flawless `order_timeline` fixture and proved almost nothing about it — its own `deterministic.spec.ts` has no hardcoded oracle values at all, and it has no value-level assertion over the timeline documents anywhere. So there was **no guard to inherit**. #8 inherited the perfection and none of the proof, had to reconstruct seventeen oracle values by hand that #7 had never written down, reconstructed them for the *derivation helpers* — and left the *artefact that actually matters five phases downstream* guarded by `Assert.True(TotalAmount > 0)`. **The gap was invisible to assertion-reading and surfaced only because the reviewer ran mutations.**

**The corollary, and it is the transferable instruction for #9:** when a reference implementation hands over data, the thing to port is **not the data — it is the assertion set over the data**, and if the reference has none, generating one is the *first* task, not a review finding. Executing #7's `toTimelineDocument` into a committed fixture is a ten-minute step. Done before any writer code, it would have made this rejection impossible. Done after a rejection, it cost 0.3h of fix plus 0.6h of re-review plus 0.4h of the review that found it — **roughly 1.3h to recover ten minutes not spent.**

**Notes for #9 (FastAPI):**

- **Generate the expected `order_timeline` documents from #7's own `toTimelineDocument` into a committed JSON fixture *before* writing the Mongo writer.** Both #8 and its reviewer independently converged on the same technique — copy #7's TypeScript, repoint the two unresolvable imports, run it under Node with type-stripping. It works, it takes minutes, and it is the single highest-leverage step in this feature.
- **Assert values, not shapes, on anything no live caller will read for several phases.** `assert doc["totals"]["totalAmount"] > 0` is not a test; it is a comment that costs CI time. In Python the temptation is stronger, because `assertIsNotNone` reads like diligence.
- **Preserve #7's `hex[13..16]` wart and comment it at the point of the slice.** It looks like a bug, it is load-bearing for id parity, and every seeded id changes if someone "fixes" it. Guard it with a test that reconstructs the un-warted value and asserts the shipped one differs.
- **Use `GLN`'s own check-digit rule as the single source of truth rather than reimplementing mod-10** — #8 did this by trying the ten candidate digits through `GLN`'s validating constructor, since its `computeCheckDigit` is private. Slightly odd-looking, genuinely one source of truth, and it fails loudly instead of writing an invalid party identifier.
- **Split pure tests from container tests at creation, not after review.** #8 shipped 34 pure tests inside a Testcontainers project and paid two container starts for a 192 ms suite. The split cut that project's runtime from 31 s to 17 s *while adding a test*.
- **Read conventions from the file on disk, not from whatever your context was seeded with.** This repository amends its own rules at human gates mid-project; the reviewer here enforced a superseded one and raised a defect against correct code. Cheap to avoid, embarrassing to discover.
- **Expect Phase 7 to be roughly a wash, and budget the rejection rather than hoping to avoid it.** Three phases now have shown the same shape: the artefact is free, the proof is not, and one rejection costs more than two clean features save.

### `seed_job` — addendum: the deferred #7 live-database comparison, now performed

The feature's acceptance carried a **row-count and checksum comparison against #7's seeded databases**, deferred at submission because it needs #7's MySQL container running and that was a decision for the human. Authorised and performed after the feature closed. Recorded here rather than reopening the feature, because it changes no code — it either confirms the parity claim or refutes it, and it confirms it.

**Method.** #7's `otc-mysql` started read-only alongside #8's stack (different port, 3306 vs 1433; #7's six volumes untouched, verified after). Master data extracted from both engines as ordered, colon-joined business columns — one row per line — and diffed **row for row**, then hashed as a whole.

**Result: 413 master-data rows, identical on both sides, same SHA-256 (`7602fe78…`).**

| Table | Rows | |
|---|---:|---|
| `currencies` | 3 | identical |
| `products` | 12 | identical |
| `retailers` | 7 | identical |
| `companies` | 22 | identical |
| `stock` | 215 | identical |
| `credits` | 154 | identical |

The six **seeded orders** also match field for field — reference, status, cancellation reason and all three money amounts, including `ORD-000006` as `cancelled` / `credit_rejected`, which is the one cancelled order the acceptance requires.

**Why the transactional counts differ, and why that is not a divergence.** #7's live database holds **1 390 orders**, 3 425 order items, 1 174 despatches and 1 170 payments. Those are not seed data: #7's stack ran for hours with the n8n order generator, and the seed contributed only the first six. Comparing raw table counts on those tables would have compared #8's seed against #7's seed **plus its entire demo history** — a meaningless number that would have looked alarming. The comparison is therefore scoped to master data plus the seeded-order subset, and that scoping is itself the finding: **a parity comparison against a database that has been *used* must isolate the seeded subset first, or it measures the wrong thing.** #9 will hit this too.

**Three flaws in my own comparison, each caught before it produced a false result** — worth recording because each would have yielded a confident, wrong answer:

1. **`STRING_AGG` without an explicit `nvarchar(max)` cast** truncated silently on the MS-SQL side, producing three *identical* hashes for three different tables. Three identical checksums is impossible for different data, and that tell is what exposed it.
2. **MySQL's `GROUP_CONCAT` truncates at 1 024 bytes by default** (`group_concat_max_len`). The #7-side hashes were being computed over truncated strings. Neither side warns.
3. **The MySQL client did not emit UTF-8**, so `Aldi España` came out as `Aldi Espa?a` and the first diff reported *three* tables differing. Those were my extraction's mangling, not the data's.

The fix for all three was to stop hashing aggregates and **dump rows to files and `diff` them** — transparent, per-row, and structurally incapable of silent truncation. A hash is a fine *summary* of a comparison you have already seen; it is a poor *substitute* for seeing it.

---

## money_column_width (id 44, phase 6) — 2026-09-02 — **a correction to Phase 6, made in Phase 8**

**Effort:** 1 session, **~0.7h wall-clock** (09:17 → 10:00) — human gate + `CLAUDE.md` Money-row amendment 09:17, implementation ~0.33h (09:20:00 first entity file → 09:37:51 report; entities 09:20, snapshots 09:21–09:22, seed writers 09:25, schema-test corrections 09:25, new guards 09:27), review ~0.35h (five arming probes across two projects with four forced full rebuilds, four `has-pending-model-changes` runs, a two-pass migration apply against two scratch databases, an independent numeric-type sweep of all four live databases, one full `quality.sh`). **APPROVED on the first pass**, zero blocking defects, three advisories. Wall-clock from file timestamps, not a stopwatch — an estimate, recorded as one.
**#7 baseline:** **no counterpart — #7 never faced this, because its language has no narrowing.** JavaScript numbers are IEEE-754 doubles, so `int` money columns cost #7 nothing at all: there is no `long → int` boundary in a language with no `long`. The narrowing existed only in #8, and only because #8 copied #7's column width. A #7 baseline for this feature is not "missing"; it is *structurally unavailable*, and the ratio column has nothing to say about a defect the reference implementation could not have had.
**Spec:** n/a (`sdd: false`). Bears directly on the reused `specs/shared/requirements.md` **R1** (*"an integer count of minor units"*) and `domain-model.md` **M1** (*"integer minor units only"*) — neither of which specifies a width, which is the whole point of the feature. `specs/shared/` was **not** amended: the code changed, the spec did not.
**Tests:** two new — `Orders.IntegrationTests.NoMoneyColumnIsIntTests.No_Money_Column_Is_Int` and `Billing.IntegrationTests.NoMoneyColumnIsIntTests.No_Money_Column_Is_Int`, both Testcontainers-backed against a real migrated MS-SQL schema. Both `SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` corrected from `"int"` to `"bigint"` on thirteen columns (and `invoice_number_sequences.next_value` correctly left `"int"`). Suite 164 → **166, all green, 1 m 38.7 s** on an idle host.

**What was built:** thirteen money columns widened `int` → `bigint` across `otc_orders` (`products.price`, `orders.initial_amount` / `.initial_discount` / `.total_amount`, `order_items.price` / `.discount`) and `otc_billing` (`credits.credit_limit`, `credit_items.amount`, `invoices.amount` / `.discount` / `.total_amount`, `invoice_items.price`, `payments.amount`); the eight owning entity properties changed to `long`; the two Phase 6 `InitialCreate` migrations **amended in place** — same ids, same filenames, one `__EFMigrationsHistory` row per database, no `ALTER` scar; and **thirteen `(int)` narrowing casts deleted** from `OrdersSeedWriter` (6) and `BillingSeedWriter` (7) — deleted, per the gate ruling, not made `checked`.

**Deviations from the spec/plan:** this feature *is* the deviation, corrected. The plan justified `int` money columns as "spec parity with #7"; the shared spec never said so. The human gate ruled the columns to `bigint` and the narrowing deleted rather than made loud, and the migrations amended in place rather than superseded, on the grounds that they never left this machine and an `ALTER` documenting a three-day-old mistake is worse than a clean schema. Both rulings were followed exactly. `CLAUDE.md`'s Money row was amended in the same session to say `long` *in the domain and in the column*, and that a narrowing cast on money is a defect rather than something to make loud.

**What the reuse saved — and what it did not:** **this is reuse's bill, not its dividend, and it is the first time in this build that reuse has been measurably *negative* rather than merely a wash.** Six features have now reported "the spec is free, the proof is not". This one reports something worse: **a detail of the reference implementation, copied without being traced back to the specification, actively injected a defect into #8 and cost 0.7h to remove three phases later** — plus whatever the two intervening database features spent building on top of it. Nothing was saved here. What #7 handed over on this point was not a specification, not even a neutral default, but a *stack-specific choice that was silently wrong for the target stack*.

The corrective is cheap and worth stating as a rule: **grep `specs/shared/` for the constraint before writing "spec parity" in a plan.** The words "integer minor units" were four greps away for three phases.

**The pattern, and it now has two instances — which is what makes it a pattern rather than an incident.** This is the **second** time #8 has mistaken a detail of #7's implementation for a requirement of the shared specification, and both times the misread detail turned out to be a property of #7's **storage engine**:

1. **Phase 5, JSON payload key ordering.** #7's payload keys come out ordered by key length then alphabetically. That is MySQL's `json` column normalisation, reaching the wire because #7's outbox relay reads the payload back out of that column and republishes it. Read as a serializer contract, it would have obliged #8 to emulate another engine's storage quirk forever, and #9 on PostgreSQL could not have done it at all. Resolved at the Phase 5 gate: envelope byte-exact, payload semantically equal.
2. **Phase 6→8, `int` money columns.** #7's MySQL `INT`, read as "spec parity". #7 pays nothing for it because JS numbers are doubles; #8 paid a `long → int` truncation boundary at every one of thirteen money writes.

Same shape, same cost signature, same root cause: **an artifact of the reference's engine mistaken for a requirement of the shared design.** Neither was findable by reading the spec, because in both cases the spec is *silent* — and silence is exactly what makes the trap work, since there is nothing to contradict the copy. Both were found only by asking *why* #7 does it that way.

**Notes for #9 (FastAPI):**

- **When #7 exhibits a detail `specs/shared/` does not mention, the default assumption is that it is an artifact of #7's stack, not a requirement.** The burden of proof runs that way round. Silence in the spec is not a licence to copy; it is a signal to decide on the target stack's own terms.
- **Money columns: `BIGINT` in PostgreSQL, and a Python `int` in the domain.** Python's ints are arbitrary-precision, so #9 will not feel a narrowing at all — which means #9 is at *greater* risk than #8 of copying `INT` and never noticing, right up until a value crosses 2 147 483 647 in production. Set the column width from the domain type deliberately, and write down why.
- **Guard the *negative*, not the positive, when the schema carries no semantic signal.** There is no way to ask a database "which of these columns is money". A whitelist of known money columns proves only that those stayed wide; the closed check is to enumerate **every** narrow column that exists and assert the set equals a short, reasoned allow-list of legitimate counters. That fails on a fourteenth money column *and* on an unrelated new counter, forcing a decision either way. #8's `NoMoneyColumnIsIntTests` is the shape to copy — and widen its type predicate beyond `int` to `smallint`/`decimal`/`money`/`float`, which #8 did not (advisory A1).
- **Arm every guard you ship, not one representative of a family.** #8's implementer armed the Orders guard and inferred the Billing one from structural similarity; the reviewer armed Billing and it fired, so nothing came of it — but "structurally identical" is precisely the eyeball judgement the arming protocol exists to replace, and the allow-list is where a copy-paste slip would land. The marginal cost is one rebuild.
- **Amending an unpushed migration in place beats a corrective `ALTER`.** #8 regenerated both `InitialCreate` migrations and manually restored the original ids and filenames, so the diff is the type change and nothing else — no rename, no `ProductVersion` churn. Verify it the way this review did: migration ids unchanged, exactly one `__EFMigrationsHistory` row per database, `has-pending-model-changes` clean on **every** context, and the migration applied twice to a genuinely empty database.
- **A destructive `git` command on an uncommitted file will silently undo work.** #8's implementer ran `git checkout -- feature_list.json` to discard a bad re-serialization and instead reverted the file to its last *commit*, dropping feature 44's own uncommitted backlog entry. Recovered only because the block happened to be in the session's tool output. This is the same hazard `CLAUDE.md`'s arming protocol already warns about for source files — it applies to every uncommitted file, harness files included. Take the backup first, always.

---

## orders_aggregate (id 13, phase 8) — 2026-09-02 — **the first `"sdd": true` feature of #8**

**Effort:** 1 session, **~1.15h of authoring and review** — spec pass elapsed 06:02 → 09:58 (`requirements.md` created 06:02, `design.md` finalised 09:58) but **interleaved** with the human gate and with the parallel `money_column_width` correction, so the elapsed window is not effort and is not recorded as such; implementation **~0.7h** (10:18 → 11:00, file timestamps: vocabulary and errors 10:18–10:20, test project 10:23, the four test files 10:23–10:28, `OrderTests` strengthened after arming row 8 at 10:42, `OrderStateMachine` 10:44, `OrderCancellationTests` strengthened after arming row 10 at 10:45, then a **second pass** 10:48–11:00 for the architecture rule after a scope conflict was resolved); review **~0.43h** (11:04 → 11:30: six mutation probes with twelve forced `--no-incremental` rebuilds, one full `quality.sh`, `init.sh`, an independent coverage computation from the cobertura XML, a scope walk over five `git diff`s, and a citation check against #7's own source). **APPROVED on the first pass**, **1 non-blocking defect, 5 advisories**; **5 of 5 re-armed guards KILLED, 1 unrequested probe SURVIVED** (that survivor is the defect). Wall-clock from file timestamps, not a stopwatch — an estimate, recorded as one.
**#7 baseline:** 1 session, ~1.5h — spec pass ~0.5h, implementation ~32 min, review ~25 min. **16 open points at its gate, 2 post-gate amendments and 1 addition**, which grew its `tasks.md` from 38 to 44 tasks. Approved first pass, **6 minor defects**, 4/4 tasked hostile edits killed and **one probe survived** (its D1: `CREATION_TRANSITION` dead code).
**#8 result: implementation ~1.3× slower than #7's; the gate ~16 open points cheaper.** That asymmetry is the finding, and §"the benchmark question" below is about it.
**Spec:** `specs/orders_aggregate/{requirements,design,tasks}.md` — 128 / 546 / 92 lines, 52 tasks, **zero open points survived to the human gate**, zero amendments, zero additions. `requirements.md` is a **pointer document**: R5–R10 are reproduced verbatim from `specs/shared/requirements.md` with only line-unwrapping, and it introduces no new `R<n>` and no local `OA<n>`. `specs/shared/` was **not** amended; the only edit to it was the six R5–R10 Status cells and the two derived count rows.
**Tests:** `tests/Orders.UnitTests` **24/24**, pure — four packages, no mocking library, no container, no `DbContext`, runs in **77–93 ms**. Plus a thirteenth architecture rule, `OrdersDomainMustNotDependOnContracts`, scoped to `OrderToCash.Orders.Domain*`. Solution-wide **191 passed, 0 failed** across ten projects. Orders domain line coverage **88.1% (830/942)**, recomputed by the reviewer from the cobertura file, above the ≥ 80% target that feature 34 will arm.
**Gates:** `./quality.sh` exit 0 in **153 s** on an idle host; `dotnet format --verify-no-changes` clean; build 0 warnings under `TreatWarningsAsErrors`; `./init.sh` exit 0; NetArchTest **13/13**.

**What was built:** the `Order` aggregate root and the whole of Table T-1 around it — nine statuses and three cancellation reasons with explicit snake_case token tables (never `ToString().ToLower()`), the eleven status-to-status edges as a `FrozenSet` of `readonly record struct` with each row number in a comment, nine intention-revealing transition methods **none of which takes an `OrderStatus` parameter**, a single private `TransitionTo` as the only writer of `Status`, candidate-then-commit line mutation, four domain events stamping six of the seven envelope fields inside the domain, eleven domain errors, and a totals-free `Rehydrate`. `src/Orders/Infrastructure/`, `src/Orders/Application/`, the migration and the seed were **not touched** — the repository adapter is designed by §8 and built by features 14/15, the identical line #7 drew at its own gate.

**The three things a reviewer of this feature must actually check, and what they showed.**

1. **Five of the twelve T-1 edges must emit *nothing*.** They do, and the suppression is guarded in the direction that matters: the reviewer added a real emission to `MarkDespatched` — a *different* edge from the one the implementer armed — and `O8_Order_AppendsNoDomainEventOnTheFiveSilentEdgesOfTableT1` failed with `Assert.Empty() Failure: Collection was not empty`. This is CLAUDE.md's *"with double force where the branch has no live caller yet"* case in its purest form: nothing calls this aggregate, so the unit test is the only thing that will ever execute these branches before feature 16.
2. **The snapshot carries no totals fields at all.** `Rehydrate` has fourteen parameters and none of them is a total; all three are re-derived by the same private routine `Place` uses, so stored/derived drift is **unrepresentable** rather than detected. Verified against #7's `order-snapshot.ts`, whose header comment says exactly that (OA3), not against #8's own prose.
3. **No narrowing cast on money, no `decimal`/`float`/`double` anywhere in the domain.** Grep-clean. After feature 44 this is a `CLAUDE.md`-level defect class, and there is none.

**The verification that makes this entry worth reading: the design's #7 citations were checked in #7's checkout, not taken on trust.** `design.md` cites #7 by file and line throughout, and those citations *are* the evidence that a decision was inherited rather than invented — so the reviewer opened `order-transitions.ts`, `order.ts`, `order-totals.ts` and `order-snapshot.ts` and read them. All four hold exactly: `emits: null` at lines 36, 42, 54, 60, 66 and a fact type at the other seven; the reason↔status pairing at `order.ts:413-419` **in the same order of checks** #8 uses; `const orderDiscount = Money.zero(currency)` in the totals formula; and a snapshot with no totals whose `reconstitute` performs the **same four validations in the same order** as `Rehydrate`. The three places #8 diverges — `OrderStateMachine` public rather than internal, `OrderLine` replaced rather than mutated, a new `OrderLineRequest` input type — are each forced by C# and each declared in the implementation report *and* in the type's own XML documentation. **No silent improvement on #7 was found**, which for this repository is the point: a "better" choice that diverges without being recorded is a defect here, because the benchmark compares implementations of the same design.

**The one defect, and it was found by a probe nobody asked for.** `Rehydrate` performs the four checks §8.3 requires; **two of them survive their own deletion**. Deleting both the empty-lines check and the entire per-line currency loop leaves the suite at 24/24 green. No `R<n>` is left unproven — R5 and O2 are well covered on the `Place`/`AddLine`/`RemoveLine` paths — and `Rehydrate` has no live caller, so it is not blocking. It is worth recording because of *where* it is: the currency check is precisely the hole **#7's own review raised as its defect D3** (*"`Order.reconstitute` lets the kernel's `CurrencyMismatchError` escape … a real O2 hole on the path feature 15's adapter will use"*). #8 read that, **wrote the check #7 lacked** — and then did not arm it. The inherited lesson was half-learned: the code is right and the guard is absent, which is the exact shape that lets right code become wrong later. Two test cases on `OrderRehydrationTests` close it, required before feature 15 lands its adapter.

**Two guards were caught lying by the arming step itself, before submission.** The implementer's rows 8 and 10 both initially passed while the thing they guarded was broken — row 8's `R7_…` test used only two-line orders, so the freeze/structural guard order was undetectable by it, and row 10's pairing test asserted only the *wrong* pairings and never the two legal ones. Both were strengthened before being counted as armed. That is the arming protocol doing its job rather than a failure of it, and both strengthened tests were independently killed by the reviewer's own probes (P2 and P4) using mutations the implementer had not run.

**A process precedent, and it is the reason this feature is worth reading twice.** The invocation brief put `tests/Architecture.Tests/**` under "do not touch". The gate-approved `tasks.md` names that directory in its own *files this feature may touch* preamble, for exactly one new rule, and §5.5 requires it. **The implementer stopped, left both files untouched, un-ticked the two boxes with an inline note, and reported the conflict** rather than obeying the brief and shipping an incomplete feature, or obeying the spec and hiding a violated constraint. The coordinator resolved it in the spec's favour — **a gate-approved spec outranks a leader's brief, because the brief's constraint list was not authorised by any gate** — and the two tasks were completed in a scoped second pass. Both halves of that are right, and the second half is the transferable one: a constraint that appears only in an invocation brief has no gate behind it, and where it contradicts a ratified spec, the spec wins. The cost of getting it right was one round-trip; the cost of either silent resolution would have been a feature that looked done and was not, or a rule quietly broken with no record. **Recorded here because `docs/PROCESS.md`'s Phase 8 entry currently records only the `feature_list.json` race, and this precedent belongs beside it.**

**Five advisories carried:** A1 the implementation report's headline test total reads 178 where the true figure — and the sum of its own per-project table — is 191, and its coverage line reads 415/471 where the cobertura file gives 830/942 (same ratio, half the counts); A2 `CancellationReason` is assigned on the line *after* `TransitionTo` returns rather than inside its accepted branch as §6.1 and #7 both have it, leaving a momentary `Cancelled`-with-null-reason state that O6 says cannot exist; A3 `Rehydrate` reuses the business code `order.cancellation_reason_not_applicable` for a load-time corruption, where #7 keeps snapshot failures in their own error — features 14/15/41 should settle it before branching on `details.code`; A4 an eleventh error type (`order.status_unknown`) where §9.1 fixes ten, declared and accepted as realisation of a check §8.3 mandates without naming, but §9.1's table now needs the row; A5 the spec-outranks-brief precedent lives only in the implementation report and this entry.

### The benchmark question — #7 had to *decide* this design; #8 received it with citations. Where did that show?

**In the gate, not in the keyboard. #7 carried 16 open points to its human gate and came back with 2 amendments and 1 addition; #8 carried zero.**

That is not a difference in diligence, and the artefact proves it: `requirements.md` §3 and §6 list the questions #8's spec pass actually raised, and they are **the same questions** — how to read invariant O8 against Table T-1 when the fact catalogue is closed at fourteen, the missing `order_discount` column against R6's "plus any order-level discount", the missing ordering column against "ordered list", the missing version column, the totals-free snapshot, the reason↔status pairing. Every one was closed **before** the gate, on evidence from #7's code or its `progress/history.md`, each citation carrying a file and a line. The rule that did it is stated in `requirements.md` §6 and is the most portable thing this feature produced: **before a question goes to the gate, ask "did #7 face this, and what did it do?" — the answer is in its code or its history file, and fetching it is cheaper and more reliable than a gate round-trip. Only what #7 *could not* face, because the language or the engine differs, is genuinely a decision.** Applied here, that left exactly two real decisions, both language-forced and both resolved inside the design: a C# `enum` parameter cannot be absent, so R10's unwanted branch lives at the parse boundary; and full typestate would cost a nine-way switch on every load, so "impossible to express" is discharged in three layers with the residue stated honestly rather than glossed.

**Implementation time was not saved: ~0.7h against #7's ~32 min, roughly 1.3× slower, on a feature whose design was handed over complete.** This is the sixth consecutive feature to report some version of *the spec is free, the proof is not* — and it is much the best of them, against feature 12's 2.3×. The difference between this feature and that one is the shape of what was inherited: feature 12 inherited **data** and no assertions over it, and was rejected; this feature inherited a **design with citations to the reference implementation's source**, and passed first time with one minor gap. The citations are the reusable artefact. A design document that says *"#7 does X"* is worth a fraction of one that says *"`apps/orders/src/domain/order-transitions.ts:36,42,54,60,66` set `emits: null`"*, because only the second can be checked in a minute by someone who does not believe you — and this review checked all four.

**And the cost, stated so it is not read as free.** The spec pass was long in elapsed terms, and part of that time went on re-reading #7's code to close points that could have been carried to the gate in minutes each. The trade was worth taking, because a gate round-trip costs a human's attention rather than an agent's tokens — but "we closed 16 open points on evidence" and "we spent the morning doing it" are the same sentence, and the benchmark should carry both halves.

**Notes for #9 (FastAPI):**

- **Write the design with citations to #7 by file and line, not by claim.** It is the single highest-leverage habit this feature demonstrates: it collapsed 16 gate open points to zero, and it makes the review a lookup rather than an argument. Where you cannot cite, you have found a genuine decision — take it to the gate and say why the citation was unavailable.
- **Table T-1 as a data table, not a switch cascade.** #7 recommended this and it holds in a third language: eleven edges as a frozen set of value-equal pairs makes the exhaustive 9 × 8 = 72-pair proof possible, makes terminality need no code at all (`completed` and `cancelled` simply never appear as a source), and makes the "the table equals the spec" test a set comparison in both directions. In Python, freeze it — `frozenset` of a `NamedTuple` — and **transcribe T-1 into the test from the specification, never import the constant the code reads**.
- **Arm the *suppression* direction, and on a different edge from the one the implementation report used.** Five of T-1's twelve edges must emit nothing, and a test that only checks the seven emitting edges will pass forever while a sixth fact leaks onto a closed catalogue. The probe is "add an emission and watch a named test fail", and it must be run on an edge nobody has armed before, or it proves only that one line of one method is guarded.
- **When you write a validation the reference implementation lacked, arm it in the same commit.** #8 correctly fixed #7's D3 currency hole in `Rehydrate` and left it guarded by nothing; deleting the whole check kept the suite green. Fixing an inherited defect without arming the fix produces code that is right today and unprotected tomorrow — and it will read as *more* trustworthy than the reference precisely because someone thought about it.
- **A constraint that appears only in an orchestration brief has no gate behind it.** Where it contradicts a ratified specification, the specification wins, and the right move on discovering the conflict is to stop and report rather than to pick a side silently. One round-trip is cheap; a feature that looks complete and is not is not.
