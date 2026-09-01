# Review — feature 8, `contracts_package`

**Verdict: APPROVED**

Every one of the five acceptance items is satisfied by a named test that I independently proved dies when the thing it guards is broken. The two failure modes the brief told me to hunt — an envelope test that compares parsed objects and would survive a field reorder, and a payload comparison so loose it would pass on a wrong value — are both absent: I reordered `Envelope`'s properties (13 tests fail), and I mutated a payload value through the real serialisation path (1 test fails, `$.totalAmount: expected 8934, found 8935`). The completeness test genuinely reads `specs/shared/asyncapi.yaml` at test time in both halves — I proved it with spec-only edits that required no rebuild at all. The twelve golden envelopes are untouched. `stock.rejected.v1`'s missing oracle is disclosed in the implementer's report, in the test's own name, and in its XML doc. The OpenAPI deferral names its target features.

One probe changed my mind and is worth recording: I had provisionally written up `EveryServiceMustUseTheSameSharedOptionsInstance` as a vacuous test — `Assert.Same` on the same static field read twice looks incapable of failing. It is not: it kills the realistic `public static readonly ... = Build()` → `public static ... => Build()` mutant, and it is the only test that does (probe P6). Eyeballing it would have produced a wrong defect; arming it produced the right answer. No blocking defects. Three advisories are carried below.

---

## 1. What I ran, and what I deliberately did NOT re-run

**Ran independently:**

- Full solution baseline before touching anything — `dotnet test OrderToCash.sln` → 32 + 21 + 12 = **65 passed, 0 failed**. Run in full because the feature adds a whole test project and I needed a trustworthy pre-probe baseline to attribute every probe failure to.
- **Six arming probes of my own** (table in §3), each using CLAUDE.md's forced-rebuild protocol (`dotnet build OrderToCash.sln --no-incremental` after both the mutation and the restore), each restored and verified by `md5sum` against a pre-edit copy.
- An independent spec-vs-code walk of **all 20 wire types** (14 payloads + 6 shared structures) with my own YAML parser: for each type, the spec's `properties` key set and `required` list against the C# record's parameter names and nullability. Result: 20/20 exact, zero missing, zero extra, and every spec-optional field is exactly the nullable C# parameter (§5).
- `diff -r --brief specs/shared /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/specs/shared` → only `test-matrix.md` differs (C7 box 1, and it also confirms my own spec probes were restored byte-clean).
- Dependency check of the built `Contracts` assembly: no `Microsoft.*`, `Confluent.*`, `NATS.*`, `MongoDB.*` or `Newtonsoft.*` assembly reference — only `System.*` and its own name.
- `./init.sh` → **exit 0**. `./quality.sh` → **exit 0** (format clean, build clean, 65/65, Contracts line coverage 95.8%, Architecture-covering run 91.3%). `dotnet format OrderToCash.sln --verify-no-changes` → **exit 0**.
- `git status --porcelain` before and after every probe; golden-file `md5sum` and `mtime` before and after (§4).

**Did NOT re-run, and why:**

- **The leader's `KebabCaseLower` naming probe.** Recorded as established in the brief (15 of 21 fail, restore returns 21/21). Repeating it is duplicated cost; I armed the *other* two `JsonWire` settings (`WriteIndented`, and the shared-instance mutant) that neither the leader nor the implementer had touched.
- **The implementer's four arming rows verbatim** (camelCase → null, null-omission → `Never`, `FactCatalog` entry removal, `TotalAmount` rename). All four are mutations of the *implementation* side. I arced at the *spec* side instead (P3, P4), which is strictly the harder claim — the acceptance item is about noticing a spec change, and a catalog-entry deletion cannot prove that. My P3/P4 needed no rebuild to fail, which is itself the proof that the spec is read at test time rather than baked into the assembly.
- **The `SharedKernel` and `Architecture` suites in isolation.** They were re-run as part of the baseline and of `quality.sh`; this feature changes nothing they cover, and feature 7's review armed all twelve architecture rules one session ago.
- **Testcontainers / integration / n8n / API-script checks.** No such code exists yet at phase 5; C4's integration boxes and all of C7's runtime boxes are not applicable.

---

## 2. `CHECKPOINTS.md` boxes walked

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model — re-verified by `init.sh`'s own model-pin checks, all `[OK]`.
- [x] `./init.sh` exits 0.

### C2 — State is coherent

- [x] At most one feature `in_progress` — zero were `in_progress`; feature 8 was `in_review` and is set `done` by this review.
- [x] Every status is in `rules.valid_status` (34 `pending`, 7 `done`, 1 `in_review` → 8 `done` after this review).
- [x] Every `done` feature has passing tests associated with it — 65 tests across three projects, all green on my own run.
- [x] `progress/current.md` describes no active session — **advisory A3**: its header still reads "Phase 4 closed, awaiting Phase 5" while phase 5 is now complete. Stale rather than wrong, and it is the leader's file, not this feature's.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — Architecture is respected

- [x] No `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` inside any `Domain/` folder — verified by running the twelve-test NetArchTest suite, not by eye (12/12 green).
- [x] No cross-service database access — no data access code exists yet; `Contracts` carries wire DTOs only.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts` — `Contracts` is the second and last permitted shared assembly, and it references nothing but the shared framework (not even `SharedKernel`, by a documented and correct design decision).
- [x] `src/SharedKernel` still has zero `PackageReference` — asserted by `SharedKernelHasNoPackagesTests`, green.
- [x] No `decimal` in domain arithmetic — `grep` for `decimal|double|float` across `src/Contracts` returns nothing; all money fields are `long`, quantities `int`. `DomainDecimalTests` green.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — this feature adds the fact envelope and the 14 fact payloads only; the fourteen RPC request/reply schemas in `asyncapi.yaml` are deliberately not in `Contracts` yet and belong to the RPC features. No Kafka-as-request-bus, no RPC-for-facts is introduced.
- [x] No stray debug logging, no context-free TODOs — `grep -rn "TODO\|FIXME\|Console\.\|Debug\.Write"` over `src/Contracts` and `tests/Contracts.UnitTests` returns nothing.

### C4 — Verification is real (partial: the coverage gate is feature 34's)

- [x] `./quality.sh` (format check + build + test + coverage) passes — exit 0, re-run by me end to end after restoring every probe.
- [x] Domain tests are pure — `Contracts.UnitTests` references only the test SDK, xunit, coverlet and one `ProjectReference`; no DB, no broker, no mocks.
- [ ] Integration tests use Testcontainers — **not applicable at phase 5**; no integration code exists. Box left empty deliberately, not failed.
- [ ] Coverage thresholds enforced — **feature 34's job**, as the brief states. `quality.sh` reports the numbers (95.8% on the Contracts-covering run) and does not yet gate on them. Recorded as reported-not-enforced, exactly as feature 6's review left it.
- [x] No Jest anywhere — xUnit only; no Node dependency in the backend.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — `bin/`, `obj/` and `TestResults/` are all matched by `.gitignore` (`git check-ignore -v` confirms); the untracked entries are the feature's own new source and progress files.
- [x] `progress/history.md` has an entry for the feature just finished, including its effort record — appended by this review.
- [x] `feature_list.json` reflects the true state — feature 8 set `done` by this review; its diff against HEAD is status fields plus the human's acceptance rewrite, nothing else.
- [x] The human has been told what was done and how to test it manually — `progress/impl_contracts_package.md` plus this file; the leader reports at the gate.
- [x] Claude did not commit — no `git commit`, no `git push` in this review. My only `git` write was `git checkout -- specs/shared/asyncapi.yaml`, twice, to restore my own spec probes (§3, P3/P4); `md5sum` confirms the file is back to `33bc7ff8ed0d942631b650a888c688d0` and `diff` against #7 confirms it byte-clean.

### C6 — Spec-Driven Development

**Not applicable.** Feature 8 is `"sdd": false`; there is no `specs/contracts_package/`, and none is required. The contract is `feature_list.json` feature 8's five acceptance items plus `specs/shared/asyncapi.yaml` and `CLAUDE.md`.

### C7 — Spec-reuse fidelity and benchmark honesty (the boxes that apply at phase 5)

- [x] **`specs/shared/` is still byte-identical to #7's except `test-matrix.md`** — verified by a real `diff -r` against the #7 checkout, run *after* my own spec probes were restored.
- [x] **Every deviation is a recorded amendment.** One deviation applies here and it is recorded in `CLAUDE.md` at the human gate today: envelope byte-exact / payload semantically equal, with the MySQL-`json`-normalisation evidence and the explicit statement that `specs/shared/` is silent on key ordering so this is a #8 convention, not a spec fork. No file under `specs/shared/` changed.
- [x] **The `R<n>` ids are #7's** — this feature claims none. R11 stays `TODO` and correctly so (§6, advisory A2).
- [ ] n8n workflows fire green — not applicable, no Gateway yet.
- [ ] The black-box API script proves the same saga — not applicable, no Gateway yet.
- [x] **`progress/history.md` effort records are complete and honest, including what was not faster** — the entry appended below records that #8's total wall-clock is *not* materially better than #7's and says exactly where the cost moved.
- [ ] The README benchmark section — not yet due; phase-level task.

---

## 3. My own arming table (forced-rebuild protocol)

Protocol for every row: apply the mutation → `dotnet build OrderToCash.sln --no-incremental` → run the named test → record the verbatim message → restore from a pre-edit copy → `touch` + `--no-incremental` rebuild → confirm green. Baseline before all of it: 65/65. Final state after all of it: 65/65, `md5sum` of every mutated file back to its pre-probe value.

| # | Guard under test | Mutation I introduced | Named test that fired | Verbatim failure message | Restored + forced rebuild |
|---|---|---|---|---|---|
| P1 | Envelope field **ORDER** (acceptance 3) | `src/Contracts/Envelopes/Envelope.cs`: swapped positional parameters `CorrelationId` and `CausationId` | `GoldenEnvelopeParityTests.OrderPlacedV1_IsByteExactSemanticallyEqualAndRoundTrips` (+12 more) | `Envelope field order must be [eventId, eventType, aggregateId, correlationId, causationId, occurredAt, payload], found [eventId, eventType, aggregateId, causationId, correlationId, occurredAt, payload]` | Yes — **13 of 21 failed**, restore → 21/21 |
| P2 | Payload **VALUE** equality, not merely key sets (acceptance 4, the "too loose" direction) | `src/Contracts/Facts/Payloads/OrderPlacedPayload.cs`: added `public long TotalAmount { get; init; } = TotalAmount + 1;` so the real serialisation path emits a wrong value | `GoldenEnvelopeParityTests.OrderPlacedV1_IsByteExactSemanticallyEqualAndRoundTrips` | `$.totalAmount: expected 8934, found 8935` | Yes — exactly 1 of 21 failed, restore → 21/21 |
| P3 | Completeness test **reads the spec at test time** (acceptance 1) | `specs/shared/asyncapi.yaml`: inserted a 15th fact schema `ProbeFakeEvent` pinning `const: probe.fake.v1` — **spec only, no code change, no rebuild** | `FactCatalogCompletenessTests.EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType` | `asyncapi.yaml declares fact type(s) with no representing type in FactCatalog: probe.fake.v1` | Yes — `git checkout --`, md5 back to `33bc7ff8…`, `diff` vs #7 clean |
| P4 | Required-**field** half also reads the spec at test time (acceptance 1) | `specs/shared/asyncapi.yaml`: added `- probeField` to `OrderPlacedPayload`'s `required:` list — **spec only, `--no-build` run** | `FactCatalogCompletenessTests.EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields` | `OrderPlacedPayload (order.placed.v1) has no property for required field 'probeField' (expected 'ProbeField')` | Yes — same, md5 back to `33bc7ff8…` |
| P5 | Payload **key-set** detection reaches nested structures | `src/Contracts/Facts/OrderLine.cs`: renamed `UnitPrice` → `UnitPriceX` | `GoldenEnvelopeParityTests.OrderPlacedV1_IsByteExactSemanticallyEqualAndRoundTrips` | `$.lines[0]: key set differs — missing {unitPrice}, unexpected {unitPriceX}` | Yes — restore verified by `diff` against the mutated copy with the rename reversed |
| P6 | "One shared options instance" is a real guard, not decoration | `src/Contracts/Wire/JsonWire.cs`: `public static readonly JsonSerializerOptions Options = Build();` → `public static JsonSerializerOptions Options => Build();` (a realistic refactor mutant) | `JsonWireOptionsTests.EveryServiceMustUseTheSameSharedOptionsInstance` | `Assert.Same() Failure: Values are not the same instance` | Yes — exactly 1 of 21 failed, restore → 21/21 |
| P7 | Compact output — the whitespace half of "byte-exact" | `src/Contracts/Wire/JsonWire.cs`: `WriteIndented = false` → `true` | `JsonWireOptionsTests.OptionalNonNullFieldIsWritten` | `Assert.Contains() Failure: Sub-string not found` (asserts the contiguous `"notes":"urgent delivery"`) | Yes — exactly 1 of 21 failed, restore → 21/21 |

**Live proof that does not need a probe:** the golden payloads are in #7's MySQL key order (`order_placed_v1.json` starts `lines, buyerGln, currency, …`) while the C# records serialise in declaration order (`orderReference, retailerCode, companyCode, …`). The twelve parity tests pass anyway. That is direct evidence that payload key order is **not** asserted — the brief's failure mode (a), a test that secretly asserts order in contradiction of today's ruling, is impossible here. Equally, the six scalar envelope fields' raw-text comparison passes against real #7 GUID and `occurredAt` bytes, which is a live proof of the `InstantJsonConverter` format (`.fffZ`, not the BCL's seven digits and `+00:00`) rather than a claim about it.

---

## 4. Golden files — the falsification check

This is the most damaging possible defect in this feature, so it got its own evidence, and the evidence is not `git diff`.

- `src/` and `tests/` are **entirely untracked** in this repository right now (`git status` shows `?? tests/`, collapsed) — so `git diff` over `tests/Contracts.UnitTests/GoldenEnvelopes/` is silent by construction and proves nothing either way. Saying "git diff was clean" here would have been a guard-that-does-not-guard, which is exactly the pattern this harness exists to catch, so I did not rely on it.
- What does hold: **all twelve golden files share the identical mtime `2026-09-01 08:01:56.080730269`** — the second they were written as a batch by the leader's capture — while every implementer-authored file in the same directory carries a distinct mtime between `10:03:17` and `10:16:09`. An edit, however small, would have moved a golden file into that later window. None did.
- Their aggregate `md5sum` (`dd52631b7fe7966e2215a2cb5d4e48c0` over the twelve per-file hashes) is unchanged across my entire review, including after all seven probes.
- Corroborating evidence that the goldens were treated as an oracle rather than as adjustable data: **P2 shows the payload comparison failing on a one-unit value change**, so a wrong implementation could not have been made green by anything short of editing a golden; and the implementer's report independently observes the MySQL key-order artifact in `credit_approved_v1.json` — an observation you can only make by reading the files as given.

**Note for the human, since only you can close this properly:** the goldens are the parity oracle for the rest of the trilogy and they currently exist only as untracked files on this machine. They should be in the feature-8 commit.

---

## 5. Acceptance walk, item by item

**Item 1 — "types hand-written from the copied asyncapi.yaml and openapi.yaml, with a test asserting every spec-declared fact type and required field is represented".** **Met for AsyncAPI; OpenAPI deferred with a named target.**

- Fact types: `FactCatalogCompletenessTests.EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType` extracts every `eventType:` / `const:` pair from the live spec file and diffs the set **both ways** against `FactCatalog.PayloadTypesByEventType`, with a non-vacuity assertion (`declaredEventTypes.Count > 0`) so a broken regex cannot pass over an empty set. Armed by me at the spec side (P3).
- Required fields: `EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields`, likewise with a per-schema non-vacuity assertion. Armed by me at the spec side (P4).
- **The "14, not 13" call is correct.** My own parse of `components.schemas` finds fourteen `*Payload` schemas and fourteen `const` fact types. The brief's parenthetical "(13 of them)" was wrong and the implementer resolved it by reading the spec, which is precisely what acceptance item 1 asks for. Building the 14th (`order.saga_failed.v1`) rather than making the test pass over a deliberately short set is the right call.
- **Beyond what the test checks, I verified all 20 types by hand** (script, §1): for every payload and every shared structure, the spec's full `properties` key set matches the C# record's parameters exactly — 20/20, no missing, no extra — and each spec-optional field (`notes`, `retailerCode`, `creditCode`, `description`, `eventId`, `summary`) is exactly the nullable C# parameter. The types are faithful well past the required-field floor the test enforces.
- **OpenAPI DTOs: deferred, disclosed, and named.** `progress/impl_contracts_package.md` defers them to feature 25 (`gateway_rest_auth`) and siblings 26, 40, 41 — the first features with a live consumer and with `openapi.yaml` conformance in their own acceptance. I checked the claim's arithmetic: `openapi.yaml` declares 57 schemas of which 19 are the primitive scalars shared with `asyncapi.yaml` (mapping to bare `Guid`/`string`/`long` already), leaving ~38 REST bodies — the report's "roughly 35" is honest. The brief permitted this deferral on condition it name which and to where; it does.

**Item 2 — "one shared JsonSerializerOptions in Contracts: camelCase, nulls omitted, no $type discriminator, no PascalCase envelope".** **Met.** `src/Contracts/Wire/JsonWire.cs` exposes exactly one public member. Five tests in `JsonWireOptionsTests` cover camelCase on payload and on envelope, null-omitted, optional-non-null-still-written, no `$type`, and single-instance. Armed: camelCase by the leader (`KebabCaseLower`, 15/21 fail), null-omission by the implementer, the shared-instance property by me (P6), compactness by me (P7).

**Item 3 — "ENVELOPE byte-exact … field set and order".** **Met, and it is genuinely an order assertion.** `AssertEnvelopeFieldOrder` enumerates the re-serialised document's property names in document order and `SequenceEqual`s them against the seven-name array, so both a missing/extra field and a reorder fail; the six scalars are then compared by `GetRawText()` against the golden's, which is a literal byte comparison of each field's value including quoting and format. P1 kills it: a two-field swap fails 13 tests. **Precise characterisation, because "byte-exact" deserves one:** this is token-level, not whole-string `==`. Given both sides are compact JSON with an identical field-name sequence and identical raw scalar bytes, the envelope bytes are identical up to the payload interior — and the residual (inter-token whitespace) is covered by P7 via a different test. A strictly literal comparison of the prefix up to `"payload":` would close the last millimetre; see advisory A1.

**Item 4 — "PAYLOAD semantically equal … key ORDER deliberately not asserted".** **Met, in both directions.** Not too strict: the twelve tests pass while golden key order and C# declaration order visibly differ (§3). Not too loose: `JsonEquivalence` compares key sets both ways at every level (P5 kills a nested rename), compares `ValueKind` before values so `8934` and `"8934"` are a kind mismatch, compares strings ordinally, and compares numbers by raw text with a decimal fallback (P2 kills a one-unit change). Array element order is significant, object key order is not — exactly the distinction today's ruling draws.

**Item 5 — "round-trip: every golden envelope deserialises into the Contracts types and re-serialises to a semantically equal document".** **Met.** Every one of the twelve parity tests *is* the round trip — `Deserialize<Envelope<TPayload>>(goldenJson)` then `Serialize`, with all assertions made against the re-serialised output. Deriving items 3–5 from one round trip rather than hand-transcribing the goldens into C# initialisers is the right call and the implementer's reasoning for it is sound: a transcription would be a second, fallible copy of the oracle.

---

## 6. Defects and advisories

**No blocking defects.**

**A1 — advisory. Envelope byte-exactness is asserted token-wise, not as literal bytes.** `tests/Contracts.UnitTests/GoldenEnvelopeParityTests.cs:132-168` parses both sides. Why it matters: the assertion is strong (P1, plus raw-text scalar comparison) and the whitespace gap is closed incidentally by `OptionalNonNullFieldIsWritten` (P7) — but that coupling is invisible to a reader of the parity test, and a future edit to `JsonWireOptionsTests` could remove the only whitespace guard without anything going red here. If the file is touched again, consider asserting `reserialised.StartsWith(goldenJson[..goldenJson.IndexOf("\"payload\":")])` alongside what is already there. Not blocking: no acceptance item is unproven.

**A2 — advisory, carried to feature 14.** `src/Contracts/Envelopes/Envelope.cs:6` cites "(R11, R12)" in its XML doc, but the type is a plain DTO that refuses nothing. R11's row in `specs/shared/test-matrix.md:104` demands a test that *refuses* an envelope with an absent, null or empty field and an `eventType` that does not match `^[a-z]+\.[a-z_]+\.v[0-9]+$`. The implementer left the row `TODO` and explained why — I agree, and the placement (validation in the emitting domain path, not in the wire DTO) is right. Why it matters: a doc-comment R-citation must not later be mistaken for coverage. Feature 14 (`outbox_and_idempotency`) owns R11 and must build the refusal; nothing in `Contracts` today would stop a malformed envelope being constructed.

**A3 — advisory, leader's file.** `progress/current.md` still reads "Feature: none — Phase 4 closed, awaiting Phase 5" while phase 5's three features are now all `done`. Stale session state; C2's box is about leftovers. Reset it to the template at session close.

**A4 — advisory, cheap to act on later.** Nothing guards `Contracts`'s own dependencies. `SharedKernelHasNoPackagesTests` inspects `SharedKernel`'s `AssemblyRef` list only; `src/Contracts/Contracts.csproj` has no `PackageReference` today and the built assembly references only `System.*` (I verified), but a `Confluent.Kafka` reference added to `Contracts` tomorrow fails no test. Why it matters: `Contracts` is one of only two shared runtime assemblies and every service will depend on it, so a transport package leaking into it leaks everywhere. The existing test is ~20 lines and generalises to a second assembly almost for free — worth doing the next time `tests/Architecture.Tests` is opened.

**Not a defect, recorded so it is not rediscovered:** the required-field completeness test walks `FactCatalog`'s payload types only, so the six shared sub-structures have no spec-driven field check. I verified all six by hand against the spec (20/20, §5) and P5 shows a rename inside `OrderLine` dies against the goldens. Five of the six appear in at least one golden; `Shortage` appears only in `stock.rejected.v1`, which is the fact with no golden — so `Shortage` is the one type in the package with neither a spec-driven field assertion nor an oracle. It is three fields and it is correct today.

**`stock.rejected.v1` — disclosure judged adequate.** The gap is stated in three places (the implementer's report, the test method's name `StockRejectedV1_HasNoGoldenFile_ButStillProducesTheCorrectEnvelopeShape`, and its XML doc), the weaker provable claim is what the test actually asserts (field order plus omission of an unset optional field), and the report says plainly that wire-byte parity for this fact "remains unproven". That is the honest handling the brief asked for: 12 goldens were not quietly treated as 13.

---

## 7. Traceability

Feature 8 claims **no `R<n>`** and `specs/shared/test-matrix.md` was not modified by it — I verified the file's diff against HEAD contains only feature 7's R1–R4 rows. The matrix groups every requirement under one of eight *other* features, and R11 — the only row a reader might expect here — belongs to `outbox_and_idempotency` and names a domain-unit refusal test (A2). Leaving it `TODO` is correct, not an oversight.

The acceptance items are the contract instead, and each maps to named tests:

| Acceptance item | Named test(s) | Armed by |
|---|---|---|
| 1 — every spec-declared fact type represented, parsing the spec | `FactCatalogCompletenessTests.EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType` | reviewer P3 (spec-side), implementer (catalog-side) |
| 1 — every spec-declared required field represented | `FactCatalogCompletenessTests.EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields` | reviewer P4 (spec-side), implementer (property-side) |
| 2 — one shared `JsonSerializerOptions` | `JsonWireOptionsTests` ×6 | leader (camelCase), implementer (nulls), reviewer P6, P7 |
| 3 — envelope byte-exact, field set and order | `GoldenEnvelopeParityTests` ×12 (+ the `stock.rejected.v1` shape test) via `AssertEnvelopeFieldOrder` and the raw-text scalar loop | reviewer P1 |
| 4 — payload semantically equal, order not asserted | the same 12 via `JsonEquivalence.AssertSemanticallyEqual` | reviewer P2 (values), P5 (nested key sets); order-not-asserted proven live by the passing suite |
| 5 — round trip | the same 12 (deserialise → re-serialise is the test's own mechanism) | inherent — P1/P2/P5 all fail through this path |

---

## 8. Effort record

Recorded in `progress/history.md` with the `#7 baseline` field and the `What the reuse saved — and what it did not` section, per that file's format. Headline: **~2.7h against #7's ~2.5h — a wash on the clock, with the cost moved from generator tooling to oracle capture and a wire-shape ruling**, and a parity oracle #7 never had as the durable gain.
