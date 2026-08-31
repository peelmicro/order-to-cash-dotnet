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
