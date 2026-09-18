# Review — `final_checkpoint` (id 38, phase 25): the CHECKPOINTS.md C1–C7 walk

**Walked:** 2026-09-18, 15:58 → 16:40 CEST, by the `reviewer` agent, read-only.
**Scope:** `CHECKPOINTS.md` C1–C7 only. The other four acceptance bullets of id 38 (backlog closure, the `specs/shared/test-matrix.md` stale coverage summary, the stack-comparison promotion) are the leader's, per the brief.
**Boxes:** 40 walked — **33 `[x]`, 7 `[ ]`**.

## Verdict: NOT YET SATISFIED — the checkpoint cannot close today

Four of the seven empty boxes are closeable within this session (write seven missing `progress/history.md` entries; fix one malformed markdown row; fix one stale sentence; correct or relabel six README benchmark rows). Two are honest, recordable deviations (n8n 1-of-4 fired; the spec commit not preceding the implementation commit). One (`progress/current.md`) closes when the leader writes the phase-25 close.

**The engineering is in excellent shape.** `./quality.sh` passed end to end on my own live run — 18/18 test projects, **2128 tests, 0 failed, 0 skipped**, domain coverage **96.83%**, overall **93.84%**, `apps/web` lint/typecheck/Vitest/build/integration all green at 98.67% line coverage. The architecture suite ran for real (50/50). `specs/shared/` is genuinely byte-identical to #7's on six of seven files. **What fails is the record, not the code** — and on a benchmark assessment whose entire purpose is the record, that is the part that matters.

**I did not take any claim on report.** I ran `./quality.sh` myself (the claim under test *was* the whole suite), ran `./init.sh` myself, `cmp`-ed every shared-spec and n8n file against the #7 checkout myself, and ran two of my own mutation probes against the newest unreviewed change in the tree. I did not write `feature_list.json` — the leader is its single writer and was editing it during this walk.

---

## C1 — The harness is complete — 5/5

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist — `ls -1` on all five, all present.
- [x] `progress/current.md` and `progress/history.md` exist — present (1861 and 3229 lines).
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer — all five present, plus `premise_checker` and `suite_runner` (7 files).
- [x] **Every agent definition declares its model** — 4 pinned in frontmatter (`implementer: sonnet`, `premise_checker: sonnet`, `suite_runner: haiku`, `test_maintainer: haiku`); the 3 without a `model:` line (`leader`, `reviewer`, `spec_author`) each state deliberate inheritance verbatim in their `description` ("Deliberately has NO pinned model, so it inherits the session model…"). Enumerated with `grep -l "^model:" .claude/agents/*.md` and its complement — no agent is silent.
- [x] `./init.sh` exits 0 — run live, **exit 0**. Its own §5d independently re-derived the shared-spec parity ("byte-identical to #7 across 6 file(s); test-matrix.md exempt").

## C2 — State is coherent — 4/5

- [x] At most **one** feature `in_progress` — **zero** in_progress (`python3` over `feature_list.json`).
- [x] Every status is in `rules.valid_status` — 110 features, 0 invalid.
- [x] Every `done` feature has passing tests associated with it — `./quality.sh` green across all 18 projects, 2128 tests (see C4).
- [ ] **`progress/current.md` describes the active session** — **FAILS at walk time.** mtime `14:55`, unchanged while six features flipped to `done` between 15:47 and 16:16. Its `**Feature:**` line still names *"id 37 `documentation_demo` — `done` (phase 24 COMPLETE)"*, followed by the brief for phase 25. `init.sh`'s §4 lockstep check passes because it reads that one line only — which is exactly the blind spot its own note records ("a stale Goal/Decisions/Notes body below it is invisible here"). Closes when the leader writes the phase-25 close. → **D7**
- [x] Every `blocked` feature records *why* — zero `blocked` features; vacuously true and stated as such.

## C3 — Architecture is respected — 8/8

- [x] **No framework reference inside any `Domain/` folder — verified by running the NetArchTest suite, not by eye.** `OrderToCash.Architecture.Tests` **50/50 passed** (35 s) inside my full `dotnet test` pass. The suite carries `DomainMustNotDependOnEntityFrameworkCore`, `…ConfluentKafka`, `…Nats`, `…MongoDb`, `…AspNetCore`, `…SystemTextJson`, plus `DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly` and `DomainAssembliesAllContainsExactlyTheSevenServicesPlusSharedKernel` as its own non-vacuity guards. Independent corroboration: `grep -rn -E "(Microsoft\.EntityFrameworkCore|Confluent\.Kafka|NATS\.|MongoDB\.|Microsoft\.AspNetCore|System\.Text\.Json|JsonSerializer)" src/*/Domain/` → **0 hits, exit 1**.
- [x] **No cross-service database access.** Enumerated every `MSSQL_DB_*`/`MONGO_DB_*` literal in `src/` outside `Seed`: each service names only its own (`Orders→MSSQL_DB_ORDERS`, `Fulfillment→…FULFILLMENT`, `Billing→…BILLING`, `Notifications→…NOTIFICATIONS`; Gateway and Projector only `MONGO_DB_READMODEL`). Every FK's `principalTable` across all migrations stays inside its own service (Orders: companies/currencies/orders/products/retailers; Fulfillment: despatches/stock; Billing: credits/invoices; Notifications: none). `src/Seed` touches three service databases, but it is an offline CLI seeding tool, not a service reading a sibling at runtime — and the architecture suite pins even that with `SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings`.
- [x] **No shared runtime code beyond `src/SharedKernel`, `src/Contracts` and `src/Cqrs`.** Enumerated every `ProjectReference` in all 10 `src/*.csproj`: each of the six services references exactly those three and nothing else. No service references another service. `src/Seed` references three services, as a leaf consumer.
- [x] **No `Domain/` namespace references `OrderToCash.Cqrs`** — `grep` 0 hits, and `DomainMustNotDependOnCqrs` green.
- [x] **`src/SharedKernel` still has zero `PackageReference` entries** — the csproj contains no `<PackageReference>` element (its single occurrence of the string is the do-not-add comment). `SharedKernelCsprojDeclaresZeroPackageReferences` **and** `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework` both green — the second is the stronger one, since it checks what the compiler built rather than what the file says.
- [x] **No `decimal` in domain arithmetic** — `grep -rn "\bdecimal\b" src/*/Domain/` → 0 hits, exit 1. `NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType` and `NoDomainTypeHasAFloatingPointFieldPropertyParameterOrReturnType` both green. The only `float`-ish hit anywhere in `Domain/` is the word "double" inside a prose comment about a clock *test double*.
- [x] **Every inter-service interaction is classifiable as Kafka-fact or NATS-RPC.** Enumerated every topic and subject literal in `src/`: 3 Kafka topics, all `otc.*.facts.v1` (DLQs derived as `<topic>.dlq`) — no command topic exists; 14 NATS subjects, each matching an `address:` in `specs/shared/asyncapi.yaml` exactly. Two further literals were classified rather than assumed: `orders.saga` is the Kafka consumer-group / dedup-ledger name (`KafkaFactStreamSubscriber.cs:130`, `ConsumerName.cs:25`), and `orders.cancel.requested` is an internal `saga_commands` row `eventType` (`OperatorCancelRequestedEnvelope.cs:39`), not a wire subject. Confinement is enforced at build time by `OnlyTheFactStreamConsumerAdapterMayReferenceTheKafkaConsumerClient` and `OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient`.
- [x] **No stray debug logging, no context-free TODOs** — `grep -rn -E "\bTODO\b|\bFIXME\b|\bXXX\b|\bHACK\b" src/` → **0**. `Console.WriteLine` appears 11 times, **all** in `src/Seed/Program.cs` and `src/Seed/Presentation/SeedRunner.cs`, where stdout is the tool's interface, not debug output.

## C4 — Verification is real — 5/5

- [x] **`./quality.sh` passes** — run live by me, `16:15 → 16:36`, **exit 0**, every section: `dotnet format --verify-no-changes` clean; build 0 warnings / 0 errors; 18/18 test projects green.

  | project | passed | project | passed |
  |---|---:|---|---:|
  | SharedKernel.UnitTests | 82 | Architecture.Tests | **50** |
  | Cqrs.UnitTests | 23 | Seed.IntegrationTests | 6 |
  | Contracts.UnitTests | 24 | Notifications.IntegrationTests | 29 |
  | Notifications.UnitTests | 114 | Fulfillment.IntegrationTests | 64 |
  | Fulfillment.UnitTests | 146 | Billing.IntegrationTests | 90 |
  | Billing.UnitTests | 278 | Orders.IntegrationTests | 157 |
  | Gateway.UnitTests | 248 | Projector.IntegrationTests | 68 |
  | Orders.UnitTests | 503 | Gateway.IntegrationTests | 78 |
  | Seed.UnitTests | 47 | | |
  | Projector.UnitTests | 121 | **Total** | **2128** |

  **0 failed, 0 skipped.** Log: `scratchpad/quality_run2.log`.

  *Caveat, recorded rather than hidden:* a first attempt at 15:59 failed the format check because a concurrent implementer session (id 110) was mid-write on `tests/Orders.IntegrationTests/NatsSagaCommandsAdapterReplyDecodeGuardTests.cs`. I waited for that session's own `dotnet test` to exit (16:14), confirmed both files quiet, and re-ran. The green run above therefore covers the id-110 change too.
- [x] **Domain tests are pure** — every `*.UnitTests.csproj` references only `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` and (in some) `Microsoft.Extensions.DependencyInjection` / `Logging.Abstractions`. `MongoDB.Driver`/`Confluent.Kafka` appear in `Gateway.UnitTests` and `Projector.UnitTests` only. I enumerated every unit-test file touching a broker or Mongo (18 files) and classified each: 16 touch no `.Domain` type at all; the two that do (`Orders.UnitTests/OutboxRelayParityTests.cs`, `Projector.UnitTests/DeltaToPipelineTests.cs`) are Infrastructure parity tests that merely name a domain type — they are not domain tests. No DB, no broker, no framework in any domain test.
- [x] **Integration tests use Testcontainers** — all 7 integration projects reference `Testcontainers` in 1–9 files each; no mocked broker anywhere in them. The 12 m 42 s / 9 m 24 s / 5 m 29 s durations in the table above are real container startup, not mocks.
- [x] **Coverage thresholds met** — gate output from my run: **domain layer 96.83% (2717/2806 lines), threshold ≥80.0%**; **overall 93.84% (15703/16733 lines), threshold ≥60.0%**; `[OK] coverage gate passed`. The gate merges all 18 cobertura reports per (file, line) rather than averaging line-rates — a real gate, not a printed number.
- [x] **No Jest anywhere** — no `jest.config*` exists in the repository (`find`, excluding `node_modules`). The only `jest` string in `apps/web` is `@testing-library/jest-dom`, consumed through its `/vitest` entrypoint in `tsconfig.json` and `src/test/setup.ts`. Root `package.json`: 0 occurrences. Runners are xUnit (backend), Vitest (web), Playwright (e2e).

## C5 — The session closed cleanly — 3/5

- [x] **No suspicious untracked files** — `git status --porcelain -uall` lists 7 untracked entries, every one an intentional new artefact: `.config/dotnet-tools.json` (id 107's fix), six `progress/impl_*.md` reports, and `tests/Orders.IntegrationTests/NatsSagaCommandsAdapterReplyDecodeGuardTests.cs` (id 110). No `*.tmp`, no `*.log`, no `bin/`, `obj/`, `TestResults/`, `node_modules/` or `.next/` escaping `.gitignore`.
- [ ] **`progress/history.md` has an entry for the feature just finished, including its effort record** — **FAILS.** See **D3**: seven `done` features carry no `progress/history.md` entry at all, four of them closed during this phase. `progress/history.md` has not been written since `14:54` while six statuses flipped between `15:47` and `16:16`.
- [x] `feature_list.json` reflects the true state of every feature touched — every status change I sampled matches what is on disk (id 110 is `done` and its fix + test are present and green; id 107's `.config/dotnet-tools.json` and `package.json` change are present; id 108's `.editorconfig` change is present; id 106's `apps/web` change is present with a real new assertion). 109 `done`, 1 `pending` (id 38, this checkpoint).
- [ ] **The human has been told what was done and how to test it manually** — **not yet, at walk time.** `progress/current.md` still holds phase 24's close. → **D7**
- [x] **Claude did not commit.** `HEAD` is still `1423d2e` *"docs(phase-24): close the phase at 1 of 1…"*, with 19 uncommitted changes in the tree. **I ran no `git commit`, no `git push`, and no git command that writes the index or working tree.** My two mutation probes were restored from a `cp` backup and `cmp`-verified, never with `git checkout`/`restore`. The one `git show HEAD:<path> >` I used was to place the *pre-fix* content for probe A, and was reverted from the same backup immediately after.

## C6 — Spec-Driven Development — 4/5

- [x] **Every `"sdd": true` feature has `specs/<name>/` with all three documents** — 8/8 sdd features (`orders_aggregate`, `outbox_and_idempotency`, `order_saga_orchestrator`, `fulfillment_stock`, `billing_credit`, `billing_invoicing`, `projector_read_model`, `observability_reliability`), each with `requirements.md`, `design.md`, `tasks.md`. All 8 are `done`.
- [x] **`requirements.md` uses strict EARS notation, every requirement carrying an `R<n>` id** — parsed `specs/shared/requirements.md` (the authority the per-feature docs cite): **63/63 ids `R1`–`R63` present with no gaps**, every one defined as `**Rn.** …`, **every one containing `SHALL`**, and each classified by EARS pattern: 27 `WHEN` (event-driven), 17 ubiquitous (`THE SYSTEM SHALL`), 14 `IF` (unwanted behaviour), 3 `WHILE` (state-driven), 2 `WHERE` (optional feature). Zero "other".
- [x] **Every `done` sdd feature has all its tasks ticked** — 554 ticked boxes, **0 unticked**, across the eight `tasks.md`.
- [x] **Every `R<n>` is covered by at least one concrete named test, recorded in `specs/shared/test-matrix.md`** — all **63** rows name at least one `tests/**/*.cs` path; **every path named exists on disk** (0 dangling). I then extracted **169 named test methods** from the Status cells and checked each against the concatenated source of every test file: **169/169 found**. The coverage-summary table sums correctly (10+8+11+8+8+5+6+6+1 = 63 = the Total row) and reads 63 green / 0 scoped / 0 not-yet-green.

  The two rows the leader corrected in parallel with this walk (**R48**, **R56**) landed before I finished, so I verified the corrected file directly rather than noting a caveat, and probed both claims rather than reading them:
  - **R56** — the corrected cell rests on `Criterion5_R56_OneTraceIdSpansTheComposedRealStack` carrying no `[Skip]`. `grep -n "Skip" tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs` → **no match, exit 1**; the case is `[Fact(Timeout = 180_000)]` at line 421-422. Confirmed.
  - **R48** — the corrected cell claims the API-level proof reads Billing's *own* database rather than the HTTP reply. `BlackBoxApiTests.cs:284-323` opens a real `BillingDbContext` and asserts `payments.CountAsync(p => p.PaymentReference == paymentReference) == 1` on the actual generated reference, after a real 201-then-200 round trip through the spawned Gateway. Not vacuous. Confirmed. (That project ran 78/78 in my full pass.)
- [ ] **The spec commit precedes the implementation commit in git history** — **FAILS for 7 of 8.** Only `projector_read_model` has a separate, earlier spec commit (`766f21f docs(spec): projector_read_model triple-doc — spec_ready, one gate decision open`). For the other seven, `specs/<name>/requirements.md` is added in the *same* commit as the implementation (`a20a1cd`, `d1715d9`, `a7a04a6`, `5a84e81`, `17ce0d1`, `dad02dd`, `d8d71c7`). **#7 does this correctly** — `e18b467`, `e5641b3`, `ed5f343` are all standalone `docs(spec): … triple-doc, approved at the human gate` commits — so this is a genuine #8 divergence, not a template mismatch. → **D6**

## C7 — Spec-reuse fidelity and benchmark honesty — 4/7

- [x] **`specs/shared/` is still byte-identical to #7's, except `test-matrix.md`'s Status column.** `cmp` against `../order-to-cash-nestjs/specs/shared/` file by file: `asyncapi.yaml`, `domain-model.md`, `n8n-workflows.md`, `openapi.yaml`, `requirements.md`, `saga.md` — **6/6 IDENTICAL**. For `test-matrix.md` I did not compare line by line (the per-assessment coverage-summary prose differs in length and shifts every following line); I parsed both files into rows keyed by `R<n>` and compared **columns 1–4** of each: **63/63 rows present in both, none missing on either side, and columns 1–4 identical on every row.** The only differences are the Status column and the per-assessment coverage-summary asides — which #7's own text explicitly licenses each assessment to rewrite ("#8 and #9 write their own equivalent and may delete this paragraph outright"). **One structural defect introduced today → D1.**
- [x] **Every deviation is a recorded amendment, in both repositories.** The count is **still 5**. Enumerated every #8 commit touching a *non*-`test-matrix.md` file under `specs/shared/`: `226c707` (SA-5, `openapi.yaml`), `1affd4a` (SA-4, `asyncapi.yaml`+`openapi.yaml`+`saga.md`), `5ec5264` (SA-3, `asyncapi.yaml`), `ea2dac6` (SA-2, `asyncapi.yaml`), and `b6c6506` (the initial verbatim copy). **No silent fork — there is no sixth.** All five are named in `README.md:41-45`. Back-port to #7 confirmed by commit in #7's own checkout, not assumed: `015b97a` (SA-1), `bf45af0` (SA-2), `5723874` (SA-3), `63f130e`+`65f1c5d` (SA-4), `6dafee0` (SA-5). *Minor deviation, disclosed rather than hidden:* SA-1 is not its own commit **in #8** — it is folded into `b6c6506`, whose own message states so explicitly and in detail; it is a standalone commit in #7. Not worth reopening.
- [x] **The `R<n>` ids are #7's.** Columns 1–4 (id, requirement text, level, #7's test-file sketch) are byte-identical to #7's on all 63 rows, so no id has been quietly repurposed; and the .NET realisation behind each is a named test that exists (169/169 checked). Two rows probed behaviourally, above.
- [ ] **`n8n/workflows/*.json` are unchanged from #7 apart from the base-URL environment variable, and all four fire green against the .NET Gateway.** First half: **`cmp` → all four byte-IDENTICAL** to #7's (`1-order-generator.json`, `2-payment-robot.json`, `3-stock-replenishment.json`, `4-burst.json`) — a stronger result than the box asks for, since even the base URL is unchanged (it was made overridable in compose instead). Second half **FAILS**: only `4-burst.json` was ever fired against the .NET Gateway (`progress/history.md` id 33: it placed `ORD-000043`, confirmed by read-back). The other three were reasoned from shared shape ("all four share the identical login/env/Gateway-call shape"), not observed. `grep` across `history.md` and `README.md` for the other three workflow names returns no firing evidence. Honestly disclosed — but a sample is not the population. → **D5**
- [x] **The black-box API script proves the same saga steps, the same facts and the same compensation as #7's.** #7's `apps/gateway/src/black-box-api.integration.spec.ts` has four scenarios (happy path; compensation; payment idempotency; auth/error shapes). #8's `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs` carries scenarios 1–3 as named `[Fact]`s with causal-order assertions on the timeline, **plus** R49's three API cases #7 never closed at API level; #7's scenario 4 (401/400/404) is covered by `AuthAndRateLimitHttpTests.cs` and `DocsAndAnonymousRouteHttpTests.cs`. The file also carries two meta-guards against its own `AssertCausalOrder` helper going vacuous — the "no causal edges were checked" case is exactly the defect class this project has been bitten by. **78/78 green in my run.**
- [ ] **`progress/history.md` effort records are complete and honest — including the features that were not faster.** **Honesty: outstanding.** The "did not save" disclosures are unflinching and specific ("#8 was NOT meaningfully faster than #7 on this feature", "~50% slower while holding the answer key", "the dividend converged to zero", "three consecutive phase-13 features whose rejection was a guard #7's own suite carried and #8's port did not… it belongs in the 'not faster, and our fault' column"). I spot-checked figures against **#7's own** `progress/history.md` rather than accepting them: #7's `db_orders` ~1.5h and `db_fulfillment` ~1.25h reconcile exactly with #8's phase-6 comparison table. **Completeness: FAILS** → **D3**.
- [ ] **The README's benchmark section gives what the reuse saved, what it did not, and what it cost, in comparable detail.** The *structure* is right — two tables plus a "What was NOT faster, stated plainly" section — and the second table ("Where reuse paid off", cited by feature id) checks out row by row against `history.md`. But **all six rows of the first table, "Where reuse cost more, not less", are mislabelled**, and the build's largest gap is missing entirely. → **D4**

---

## Defects

### D1 — BLOCKING (mechanical, one character): `specs/shared/test-matrix.md:189` is a malformed table row

The `R56` Status cell, rewritten in today's uncommitted correction, contains an **unescaped `|`** inside an inline-code grep command:

```
(`grep -n "id 28\|Id 28\|saga_e2e_verification" progress/history.md` returns nothing, confirmed live; …)
```

GitHub-flavoured Markdown splits table cells on `|` **even inside backticks**, so that row parses as **7 fields where the table declares 5**. Verified mechanically over both files: #8 has exactly one malformed row (line 189, 7 vs 5); **#7's copy has zero**. The row renders with two spurious trailing cells and the requirement's evidence broken across them.

**Why it matters:** this is the file `#9` inherits, and it is the one artefact in `specs/shared/` that #8 is permitted to write. Shipping a broken table into the trilogy's shared surface is the exact class of defect SA-1 exists to prevent.

**Disposition — fix now, before the wrap-up commit.** Escape as `\|` or reword the command. **This is not a spec amendment**: `test-matrix.md`'s Status column is explicitly the per-assessment region #8 owns, so it needs no `SA-n` and no human gate.

### D2 — Minor (same file, same pass): a stale present-tense sentence contradicting its own table

`specs/shared/test-matrix.md`, the "Scoped rows, and what closing them would take" block, second paragraph: *"`R24` remains scoped, with its named closer corrected to feature 31."* Three lines above, the summary table reads **Green 63, Scoped 0**, and `R24`'s own row reads **"BOTH HALVES DONE"**. The surrounding paragraph is written as history ("This paragraph replaces the prior state…"), but this clause is present tense and reads as a live claim.

**Disposition — fix in the same pass as D1** (reword to past tense, e.g. "`R24` was scoped at that point, with its closer corrected to feature 31").

### D3 — BLOCKING (C5 + C7): seven `done` features have no `progress/history.md` entry, and therefore no effort record

`feature_list.json`'s own `rules.require_effort_record_to_close` is `true`, and the reviewer contract in `CLAUDE.md` is blunter: *"A feature without an effort record is not closeable — that record is assessment #8's measurement against the #7 baseline, and the whole point of this repository."*

I enumerated all 109 `done` features against `progress/history.md` (matching a heading naming the feature, a heading of the form `(id n,`/`## Id n`, or any `id n` mention in the body). **Seven have zero mention of any kind:**

| id | name | phase | note |
|---|---|---|---|
| 28 | `saga_e2e_verification` | 15 | Long-standing. Already self-disclosed inside `test-matrix.md`'s own R56 cell ("There is no `progress/history.md` entry for id 28 itself… confirmed live"). Its record lives in `progress/impl_id28_saga_e2e_verification.md` and commit `b144788`. |
| 65 | `gateway_order_detail_totals_transposition_survives` | 13 | Long-standing. |
| 92 | `nestjs_late_credit_approval_has_no_end_to_end_coverage` | 14 | **Dispositioned, not implemented** — ACCEPTED, NOT FIXED at the human gate. |
| 93 | `orders_and_catalog_rpc_payloads_are_still_duplicated…` | 15 | Filed and closed; disposition in `notes`. |
| 107 | `dotnet_sonarscanner_has_no_local_tool_manifest` | 21 | **Closed today**, in this phase. |
| 110 | `orders_saga_commands_adapter_reply_decode_guard` | 25 | **Closed today**, at 16:16, in this phase. |
| 111 | `outbox_relay_poison_payload_retry_loop_unverified` | 25 | **Closed today**, in this phase. |

Additionally, ids **52** and **106** flipped to `done` today and are mentioned only inside *other* entries, with no effort record of their own.

`progress/history.md` mtime is `14:54`; six statuses flipped between `15:47` and `16:16`. Each of the seven carries a substantive `notes` disposition inside `feature_list.json`, so nothing is *undocumented* — but a `notes` field is not an effort record, and the effort record is the deliverable this repository exists to produce.

**Disposition — the leader writes the missing entries before the phase closes.** For 92 and 93, which were dispositioned rather than implemented, an honest one-line record ("0 implementer sessions, 0 reviews; dispositioned at the human gate on <date>") satisfies the rule exactly; inventing hours would be worse than the gap. For 28, 65, 107, 110 and 111 the sessions and wall-clock are recoverable from the `progress/impl_*.md` reports and file mtimes, as every other entry in the file already does.

### D4 — BLOCKING (C7, benchmark honesty): every row of the README's "Where reuse cost more, not less" table is mislabelled

`README.md:230-239`. The section's own preamble promises: *"Every figure below is read from a `progress/history.md` — this repository's own, or #7's, both committed and **both cited by phase or feature id so the number can be re-derived rather than trusted**."* Five of the six rows cannot be re-derived from the phase they name. Each figure is real and recorded — it simply belongs to a different feature.

| README row label | README figures | Where those figures actually are | What that phase's own record says |
|---|---|---|---|
| Phase 6 (EF Core models), **≈3.3×** | ≈65 min vs ≈20 min | `billing_credit_simulator` (id 20, **phase 10**) — `history.md:1269` | **≈1.03× — "no saving"**; ≈3.6 h vs ≈3.5 h (`history.md:484`) |
| Phase 8 (Orders + saga), 1.3× / 6.3× | ≈1 h 27 / ≈19 min vs ≈1 h 07 / ≈3 min | `billing_invoicing` (id 21, **phase 10**) — `history.md:1313-1314` | Comparable subset **≈1.76×** (Phase 8 closing assessment) |
| Phase 9 (Fulfillment), 1.17× / 2.5× | ≈56 / ≈32 min vs ≈48 / ≈13 min | `billing_remittance_intake` (id 22, **phase 10**) — `history.md:1384` | Comparable subset **≈1.25×** (Phase 9 closing assessment) |
| Phase 10 (Billing), 1.4× / 1.9× | ≈2 h 20 / ≈50–60 min vs 1 h 39 / 29 min | `notifications_service` (id 23, **phase 11**) — `history.md:1494-1495` | Phase total **≈1.69×** (`history.md:1420`) |
| Phase 12 (Projector), ≈1.4× | ≈5 h 04 vs ≈3 h 41 | `projector_read_model` (id 24, phase 12) | **CORRECT** |
| Phase 14 (guard-hardening audit) | #8: 6 sessions / 4 reviews / 3 rejections / ≈4 h 10 + ≈2 h 30 — #7: 14 implementer passes, 8 reviews | **Both halves are the `web_app` comparison** (id 29, phase 16/17) — `history.md:2876` and `:2878` | #7's phase 14 was **6** implementer passes + 1 review, approved first time, ≈7 h 45 min (`../order-to-cash-nestjs/progress/history.md:1057`) |

Two consequences worth naming separately:

1. **The row-6 `#7` column reports #8's own number as #7's.** "14 implementer passes" is #8's phase-14 count (`history.md:2124`). #7's phase 14 was six passes, approved first time. The comparison as printed inverts the direction of the finding.
2. **The single largest gap in the build is absent from the table.** #8's real phase 14 — `observability_reliability` — was **≈24 h 10 min against #7's ≈7 h 45 min, ≈3.1×** (`history.md:2139`), with three rejections against #7's first-pass approval. It dwarfs every row that *is* in the table in absolute cost, and it is the strongest evidence the section's thesis has. Meanwhile the row that *claims* "the largest gap in the build" (phase 6) is the one phase whose own closing assessment is titled **"a dividend that converged to zero"**.

The section's **conclusion survives** — early/mid phases genuinely were slower, late phases genuinely faster, and the "What was NOT faster, stated plainly" prose is accurate and well-earned. The table underneath it is not.

**Disposition — fix before the wrap-up commit.** Either relabel each row with the feature id its numbers belong to, or replace the table with the phase-level ratios the closing assessments already record and which can be re-derived on sight: **phase 6 ≈1.03×, phase 8 ≈1.76×, phase 9 ≈1.25×, phase 10 ≈1.69×, phase 12 ≈1.4×, phase 13 ≈1.38×, phase 14 ≈3.1×.** Per `CLAUDE.md`'s own commit discipline: *"A number that does not reconcile with the last run is a finding, not a footnote."* This is the trilogy's headline deliverable and the one artefact a reader will check.

### D5 — C7 shortfall, needs routing not narrating: three of four n8n workflows were never fired

`4-burst.json` was fired live against the .NET Gateway and placed `ORD-000043`. `1-order-generator.json`, `2-payment-robot.json` and `3-stock-replenishment.json` were **never observed green** — `progress/history.md` id 33 says so plainly and gives the reason (they are schedule-triggered; the burst workflow was chosen because it fires synchronously via webhook, and "all four share the identical login/env/Gateway-call shape"). The disclosure is honest. The box is not satisfied.

**Disposition — one of these two, named explicitly, not deferred to "whoever next touches n8n":**
(a) fire the three remaining workflows manually against the phase-23 full-compose stack — they can be executed on demand from n8n's own UI/CLI without waiting for their schedules — and record the result; or
(b) **file a numbered backlog entry** naming the three workflows and the observation each still owes, so it outlives this feature.
I cannot write `feature_list.json`; the leader files it.

### D6 — Not retroactively fixable (C6): the spec commit does not precede the implementation commit

7 of 8 sdd features (all but `projector_read_model`) have their `specs/<name>/` triple-doc added in the same commit as the implementation. #7 commits them separately, every time. The *substance* of the rule held — the spec was authored and human-gated before implementation in every case, evidenced by `progress/spec_*.md` (six files) and the gate records in `progress/history.md` (e.g. `orders_aggregate`'s "16 open points at its gate") — but git history does not show it, which is what the box asks for. The cause is #8's commit discipline: the maintainer commits **one commit per feature** at wrap-up, which necessarily bundles the spec with the code.

**Disposition — accept with evidence, and route the lesson.** Record the box as failed with the reason and the substitute evidence, and add one line to `docs/lessons.md` for **#9**: *commit the triple-doc on its own at `spec_ready`, before the implementer is dispatched* — which costs nothing and makes the box true by construction.

### D7 — Procedural (C2 + C5): `progress/current.md` is one phase behind

mtime `14:55`; still describes phase 24's close and the phase-25 brief. `init.sh`'s §4 passes because it reads only the `**Feature:**` line — the blind spot its own message names. The human has therefore not yet been told what phase 25 did or how to test it. **Disposition — closes when the leader writes the phase-25 close**, which the brief says is already planned.

---

## What I probed rather than read

Per `CLAUDE.md`'s mutation-family rule, I ran two families against the newest and least-reviewed change in the tree — id 110's reply-decode guard (`src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`), which landed during this walk and has had no separate review.

Backup taken with `cp` (sha256 `2311a2b0004d9295…`); restored from the backup and `cmp`-verified after each probe; never `git checkout`.

1. **Substitute a sibling identifier** — the thrown `SagaCommandTransportError` replaced with its sibling `SagaCommandTimeoutError`. `R110_AMalformedNonJsonReplyOnASagaCommandSubject_ThrowsSagaCommandTransportError_NeverABareJsonException` **FAILED**, and the message named the claim:
   ```
   Assert.Throws() Failure: Exception type was not an exact match
   Expected: typeof(OrderToCash.Orders.Application.Ports.SagaCommandTransportError)
   Actual:   typeof(OrderToCash.Orders.Application.Ports.SagaCommandTimeoutError)
   ```
2. **Delete the behaviour** — the pre-fix committed version put back in place (`git show HEAD:<path>`), so the `catch (JsonException)` does not exist. The same test **FAILED**, with the bare `JsonException` visible escaping `RpcJson.IsErrorBody` in the inner stack trace — the exact defect the guard exists to prevent.
   *(A first attempt at this family used `catch (JsonException) when (false)`; the build rejected it with `error CS8360: Filter expression is a constant 'false'`. Recorded because it is a useful fact about this repository's strictness, not hidden as a failed attempt.)*
3. **Restored** — `cmp` byte-identical to the backup, `dotnet build src/Orders/Orders.csproj --no-incremental` (0 errors), test re-run **green, 1/1**.

I also verified the ported-idiom ledger's claims rather than its existence, on the two rows most likely to be assumed:
- **RL4** (`progress/impl_outbox_and_idempotency.md`, added today by id 52): both halves check out against the actual sources — #7's `outbox-envelope-mapper.ts:12-27` does validate seven fields and throws a plain `Error` with no parse step, and #8's `OutboxEnvelopeMapper.ToWireBytes` does call `JsonDocument.Parse(row.Payload)`, a decode step #7 genuinely does not have. The row's honest "no #7-vs-#8 asymmetry found" conclusion, and its disclosure that the audit's own 10-minute stopping rule fired, are both borne out — and it produced backlog id 111 rather than a silent fix.
- **RL5** (id 110): the cited #7 counterpart `nats-saga-commands.adapter.ts:170-178` exists exactly there and reads `reply payload was not valid JSON` — the string #8's fix deliberately mirrors.

**What I did not re-run, and why.** Nothing material: I ran the full `./quality.sh` myself because the claim under test was the whole suite, and `./init.sh` myself. I did not re-run the four n8n workflows (see D5) and did not stand up a live Jaeger for R56 — closure there rests on the shipped, unskipped composed-stack test, which I confirmed carries no `[Skip]`, plus the independent post-fix Jaeger re-confirmation already recorded under id 35.

---

## What must change before this checkpoint can close

1. **D1** — escape the `|` in `specs/shared/test-matrix.md:189`. One character. Not an `SA-n`.
2. **D2** — reword the stale "`R24` remains scoped" sentence in the same file.
3. **D3** — write the seven missing `progress/history.md` entries with their effort records (ids 28, 65, 92, 93, 107, 110, 111), plus own-entry records for 52 and 106.
4. **D4** — correct the README's "Where reuse cost more, not less" table, and add the phase-14 row (≈3.1×) that the build's own record makes its strongest evidence.
5. **D5** — fire the three unfired n8n workflows, **or** file a numbered backlog entry naming them. Not "the next feature that touches n8n".
6. **D6** — record the spec-commit-ordering deviation as accepted-with-evidence and add the one-line lesson for #9.
7. **D7** — write the phase-25 close into `progress/current.md`.

None of these touches `src/`. Items 1–4 and 7 are documentation and record; items 5 and 6 are routing decisions. The code, the tests, the coverage and the architecture are all in order and were verified live.

**I did not write `feature_list.json`.** The leader is its single writer and was editing it throughout this walk (16:16); the brief bounds me out of it explicitly. Id 38 remains `pending`, which is the correct state until the seven items above are dispositioned.
