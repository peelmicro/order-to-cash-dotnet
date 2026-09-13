# Implementation record — `test_matrix_rows_that_outlived_their_named_closer` (backlog id 72)

## Summary

Three `specs/shared/test-matrix.md` rows (`R1`, `R24`, `R61`) deferred their API half to "the gateway feature" (id 25, `gateway_rest_auth`), which shipped in phase 13 without any acceptance bullet naming any of them, so none closed. This feature:

1. Built and armed `R1`'s API half — a new Gateway integration test porting #7's `money-representation.integration.spec.ts` + `money-field-sweep.ts` — and flipped the row to `DONE`.
2. Armed `R61`'s already-existing API-half test (`FulfillmentStockEndToEndTests.cs › ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase`), which had never been armed, and flipped the row to `DONE`.
3. Corrected `R24`'s cell to name feature 31 `api_tests` (whose acceptance already carries the R24 bullet, verified — not assumed) as its closer, since `R24`'s API half genuinely does not exist yet.
4. Added the "Scoped rows, and what closing them would take" paragraph the coverage-summary legend (`test-matrix.md:67`) already points to.
5. Recounted the coverage summary row by row from the Status column.

Only column 5 (Status) and the coverage summary / new paragraph of `test-matrix.md` were touched, per the brief's scope. No other `specs/shared/` file was touched.

## Files touched

- `specs/shared/test-matrix.md` — Status cells for `R1`, `R24`, `R61`; coverage-summary table; new "Scoped rows" paragraph.
- `tests/Gateway.IntegrationTests/MoneyFieldSweep.cs` — new. Ports #7's `test-support/money-field-sweep.ts` (`sweepForMoneyFields`) onto `System.Text.Json.JsonElement`.
- `tests/Gateway.IntegrationTests/MoneyRepresentationHttpTests.cs` — new. Ports #7's `money-representation.integration.spec.ts`.
- `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` — one assertion strengthened (`response.EnsureSuccessStatusCode()` → an `Assert.True` that surfaces the Problem-JSON body on failure), so a subject-substitution defect names itself instead of only reporting a bare status code. No test added or removed; same two `[Fact]`s as before.

## R1 — #7's assertion-by-assertion enumeration

Read in full: `apps/gateway/src/money-representation.integration.spec.ts` and `apps/gateway/src/test-support/money-field-sweep.ts` (order-to-cash-nestjs checkout at `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`).

**`money-field-sweep.ts` (the recogniser):**

| # | #7 behaviour | Classification | #8 rendering |
|---|---|---|---|
| 1 | `hasOwnCurrency` — does this object carry its own `currency` key | ported | `node.TryGetProperty(CurrencyKey, out var ownCurrency)` |
| 2 | `effectiveCurrency` inherits the nearest ancestor's `currency` when the current object has none | ported | `hasOwnCurrency ? ownCurrency : inheritedCurrency` |
| 3 | `isCanonicalMoneyAmount` — the `amount` key beside its own `currency`, flagged regardless of runtime/value type | ported | `property.Name == CanonicalAmountKey && hasOwnCurrency`, independent of `ValueKind` |
| 4 | `isNumberUnderCurrencyContext` — any number-typed sibling under an effective currency | ported | `value.ValueKind == JsonValueKind.Number && effectiveCurrency is not null` |
| 5 | A recognised money field is a leaf — never itself walked into | ported | `continue` immediately after `findings.Add(...)` |
| 6 | Arrays walked with an indexed path (`items[0]`) | ported | `EnumerateArray()` with an incrementing index in the path |
| 7 | Only plain objects/arrays are walked; scalars stop the recursion | ported | `ValueKind` gate (`Object`/`Array`) before recursing |
| 8 | Disclosed over-inclusion: an unrelated integer beside `currency` (e.g. `quantity` beside `unitPrice`) is also flagged, deliberately, over a name-based denylist | ported (as design + observed) | Same shape-based rule; `OrderReadModelItem.Quantity` (an `int`) sits beside `unitPrice`/`lineDiscount` under the order's inherited `currency` and is discovered as a finding by the same mechanism — not a separate assertion, the identical algorithm as #7's |
| 9 | `MoneyFinding` never asserts anything itself — caller decides | ported | `MoneyFinding` carries `Path`/`Amount`/`Currency` only; `SweepForMoneyFields` returns, never asserts |

**`money-representation.integration.spec.ts` (the test):**

| # | #7 behaviour | Classification | #8 rendering |
|---|---|---|---|
| 1 | `assertEveryFindingIsIntegerMinorUnitsWithCurrency` — vacuity guard: `findings.length > 0`, named per endpoint | ported | `Assert.True(findings.Count > 0, "expected at least one monetary field discovered in {endpointLabel}'s response — found none...")` |
| 2 | Per-finding: `Number.isInteger(finding.amount)` | ported | `finding.Amount.ValueKind == JsonValueKind.Number && finding.Amount.TryGetInt64(out _)` |
| 3 | Per-finding: currency matches `/^[A-Z]{3}$/` | ported | `_isoCurrencyShape.IsMatch(currency.GetString() ?? "")`, same pattern |
| 4 | Boot: a real NestJS app, real MongoDB + NATS test fixtures, TEST-ONLY stub RPC responders reached over the real wire | **not ported (reason: substituted)** — a real Kestrel host (`GatewayTestHost`) with `IRpcClient`/`IOrderReadModel` substituted by fakes in DI, the SAME seam this repository's own `OrdersHttpTests.cs`/`InvoicesHttpTests.cs` already establish. #7's own file states the reason this substitution is faithful: "R1's claim is about SHAPE, which the Gateway alone decides on every payload it hands back, whichever upstream produced it" — a real Mongo/NATS round trip proves nothing about SHAPE that a fake does not, and #8 already proves the real-infrastructure claims this file would otherwise duplicate elsewhere (`MongoOrderReadModelIntegrationTests`, `FulfillmentStockEndToEndTests`, `NatsRpcClientIntegrationTests`) |
| 5 | `POST /orders` via a stubbed `orders.create` | ported | `RecordingRpcClient.EnqueueReply(GatewaySubjects.OrdersCreate, ...)` |
| 6 | `GET /orders/{id}` and `GET /orders` via a seeded read-model document, field-for-field matching (`totals` one level down with no own `currency`, `items[]` two levels down with no own `currency`) | ported | `SeedOrderDocForSweep` reproduces the identical nesting via `InMemoryOrderReadModel` |
| 7 | `GET /invoices` via a stubbed `billing.invoice.list` | ported | `RecordingRpcClient.EnqueueReply(GatewaySubjects.InvoiceList, ...)` |
| 8 | `GET /credits` via a stubbed `billing.credit.list`, deliberately using vocabulary (`creditLimit`/`activeHolds`/`openExposure`/`availableCredit`) a name-based recogniser would miss | ported | Same four fields, same comment preserved |
| 9 | The assertion applied uniformly in a loop over `findingsByEndpoint` | ported | Identical loop shape |
| 10 | Narrative about #7's own traceability-audit history (why this file was written) | not applicable | Historical to #7's own repository, not a portable assertion |
| 11 | "Tested against (review finding N6): the Gateway alone" — Orders/Billing not spawned | ported (a fortiori) | #8's version never spawns any other service at all, real or stub-over-the-wire — the substitution above is strictly narrower |

## Arming — verbatim table

Protocol per CLAUDE.md: `cp` a backup, mutate, `dotnet build --no-incremental`, run the ONE named test, record the verbatim failure, restore from backup, `cmp`, forced rebuild, confirm green.

| # | Row | Mutation | Backup / restore | Named test run | Verbatim failure | Restored + green? |
|---|---|---|---|---|---|---|
| 1 | R1 arm A (non-integer) | `GatewayRpcPayloads.cs`: `InvoiceViewPayload.TotalAmount` `long`→`double`; test stub `213_950`→`213_950.7` | `cmp` clean on both files after restore | `MoneyRepresentationHttpTests.EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode` | `GET /invoices $.items[0].totalAmount: R1 requires an integer count of minor units, got 213950.7 (kind Number)` | Yes — forced `--no-incremental` rebuild, 1/1 green |
| 2 | R1 arm B (no currency) | `OrderReadModelMapper.cs`: `ToOrderDetail`'s `doc.Currency` argument → `null` | `cmp` clean after restore | same test | `expected at least one monetary field discovered in GET /orders/{id}'s response — found none, which would make this sweep vacuous rather than proving R1` | Yes — forced rebuild, 1/1 green |
| 3 | R61 arm A (corrupt quantity) | `StockEndpoints.cs`: `l.Units` → `l.Units + 1` | `cmp` clean after restore | `FulfillmentStockEndToEndTests.ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase` | `Assert.Equal() Failure: Values differ\nExpected: 35\nActual: 36` | Yes |
| 4 | R61 arm B (substitute subject) — first attempt | `GatewaySubjects.cs`: `StockReplenish` → `"billing.payment.register"` (a real sibling subject, for a service not booted in this test) | mutation reverted before proceeding | same test | `System.Net.Http.HttpRequestException: Response status code does not indicate success: 503 (Service Unavailable).` — **did not name the subject**; rejected as evidence per the acceptance's own "a swap that fails for the wrong reason is not evidence" | — |
| 5 | Durable fix | `FulfillmentStockEndToEndTests.cs`: `EnsureSuccessStatusCode()` → `Assert.True(response.IsSuccessStatusCode, $"...got {(int)response.StatusCode} {response.StatusCode}: {responseBody}")`, reading the Problem-JSON body first | — (permanent change, not an arming mutation) | both `[Fact]`s in the class | baseline re-run: `Passed! Failed: 0, Passed: 2, Total: 2` | Confirmed green before re-arming |
| 6 | R61 arm B (substitute subject) — re-armed | Same mutation as #4, against the strengthened assertion | `cmp` clean after restore | same test | `expected /stock/replenish to succeed; got 503 ServiceUnavailable: {"type":"about:blank","title":"The owning context is unreachable","status":503,"detail":"RPC call to \"billing.payment.register\" failed: no responder is subscribed to this subject.","code":"UPSTREAM_UNAVAILABLE",...}` — names the subject, and is `RpcTransportError`'s immediate "no responder" refusal (`NatsRpcClient.cs`), never the request timeout | Yes — forced rebuild, 2/2 green |

Row 4's rejected attempt is recorded deliberately: the acceptance bullet explicitly distinguishes a failure that names the subject from one that does not, and the first attempt was a genuine miss against that bar — `EnsureSuccessStatusCode()`'s exception carries only the HTTP status, not the response body where the subject actually appears (`ProblemJsonMiddleware`'s `detail` field, which is `RpcTransportError.Message`). The fix (row 5) is a legitimate, durable strengthening of the test's own failure diagnostics, not a one-off scaffold — it stays in the file after arming.

## Coverage-summary recount

One classification line per `R`-row, read from the Status column exactly as it stands after the two flips (`R1`, `R61`) and the one rename (`R24`). `DONE`/`RETRY-CLAUSE ROW DONE …DEAD-LETTER ROW DONE` with no disclosed shortfall = **Green**; a disclosed-and-ratified partial = **Scoped**; `TODO` = **Not yet green**.

```
R1  Green   R2  Green   R3  Green   R4  Green   R5  Green
R6  Green   R7  Green   R8  Green   R9  Green   R10 Green
R11 Green   R12 Green   R13 Green   R14 Green   R15 Green
R16 Green   R17 Green   R18 Green   R19 Green   R20 Green
R21 Green   R22 Green   R23 Green   R24 Scoped   R25 Green
R26 Green   R27 Green   R28 Green   R29 Green   R30 Green
R31 Green   R32 Green   R33 Green   R34 Green   R35 Green
R36 Green   R61 Green   R37 Green   R38 Green   R39 Green
R40 Green   R41 Green   R42 Green   R43 Green   R44 Green
R45 Green   R46 Green   R47 Green   R48 Green   R49 Green
R50 Green   R51 Green   R52 Green   R53 Green   R54 Green
R55 Not-yet-green   R56 Scoped   R57 Green   R58 Green
R59 Green   R60 Green   R62 Green   R63 Green
```

Counted (`grep -c` against the enumeration above, then by hand): 63 rows total. Green = 60. Scoped = 2 (`R24`, `R56`). Not-yet-green = 1 (`R55`). 60 + 2 + 1 = 63 ✓.

Per section, reconciled against the table now committed in `test-matrix.md`:

| Feature | Rows | Green | Scoped | Not yet green | Row ids counted |
|---|---:|---:|---:|---:|---|
| 1. `orders_aggregate` | 10 | 10 | 0 | 0 | R1–R10, all Green (R1 flipped) |
| 2. `outbox_and_idempotency` | 8 | 8 | 0 | 0 | R11–R18, all Green (unchanged) |
| 3. `order_saga_orchestrator` | 11 | 10 | 1 | 0 | R19–R29; R24 Scoped, rest Green (unchanged) |
| 4. `fulfillment_stock` | 8 | 8 | 0 | 0 | R30–R36, R61, all Green (R61 flipped) |
| 5. `billing_credit` | 8 | 8 | 0 | 0 | R37–R44, all Green (unchanged) |
| 6. `billing_invoicing` | 5 | 5 | 0 | 0 | R45–R49, all Green (unchanged) |
| 7. `projector_read_model` | 6 | 5 | 0 | 1 | R50–R55; R55 Not-yet-green, rest Green (unchanged) |
| 8. `observability_reliability` | 6 | 5 | 1 | 0 | R56–R60, R62; R56 Scoped, rest Green (unchanged) |
| 8.1 gateway edge protection | 1 | 1 | 0 | 0 | R63, Green (unchanged) |
| **Total** | **63** | **60** | **2** | **1** | 60+2+1 = 63 ✓ |

Before this feature the table read 63/58/4/1 (58+4+1=63). This feature moved exactly two rows from Scoped to Green (`R1`, `R61`) and left the third (`R24`) Scoped with a corrected closer — 58+2=60 Green, 4−2=2 Scoped, 1 Not-yet-green unchanged: 60+2+1=63 ✓, matching the arithmetic that produced the new table.

## `R<n>` traceability

- **R1** (`api/money-representation.spec`) — `tests/Gateway.IntegrationTests/MoneyRepresentationHttpTests.cs` › `EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode`, armed both ways (table rows 1–2 above).
- **R61** (`api/stock-replenishment.spec`) — `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` › `ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase`, armed both ways (table rows 3, 6 above).
- **R24** — no new test; its cell now names feature 31 `api_tests` as the closer, and the leader's own addition to id 31's acceptance was verified (not assumed) to carry the R24 bullet.

## Quality gates

- `./quality.sh` → `/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/quality_feature72_run2.log`. First run failed format check (`IDE1006`, a private static field missing its `_` prefix); fixed, `dotnet format --verify-no-changes` clean, re-ran full `./quality.sh`: **18 projects, 0 failed, 1879 tests total** (`grep -oE "Total:\s*[0-9]+" ... | awk '{s+=$1} END{print s}'` → 1879; `grep -c "Total:" ` → 18). Reconciled against the brief's stated baseline of 1878 (18 projects, 0 failed): **1878 + 1 = 1879**, the one new fact — `MoneyRepresentationHttpTests`'s single `[Fact]` (`FulfillmentStockEndToEndTests.cs` gained no test, only a strengthened assertion in an existing one). `Gateway.IntegrationTests` itself read 60/60 (was 59 before this feature). No red; neither of the two named-intermittent tests (`Projector…PR38_…ReadFromTheBroker`, `Gateway…OR4_TwoConcurrentCalls_…`) appeared in the failure set because there was none.
- `./init.sh` → `/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/init_feature72.log`, exit 0. §5d confirms: "shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt (per-assessment Status column)" — the expected shape given this feature's edits are confined to `test-matrix.md`.

## What could not be done / scope notes

- `R24` was NOT flipped to Green — its API half genuinely does not exist (feature 31 `api_tests` is still `pending`). Bullet 3 of the acceptance asks only for the correct named closer, not for the row to close; the coverage-summary recount reflects `R24` remaining Scoped.
- `R61`'s subject-substitution arming needed one durable, in-scope strengthening of `FulfillmentStockEndToEndTests.cs`'s own assertion (recorded as table row 5) beyond a pure mutate/restore cycle, because the pre-existing `EnsureSuccessStatusCode()` call could not produce a subject-naming failure message no matter which real sibling subject was substituted — the subject only appears in the HTTP response body, which that call never reads. This is a one-line, permanent improvement to an existing test's diagnostics, not a new test or a scope change.
- Nothing else in `specs/shared/` needed touching; no `SA-n` proposal arose from this feature.

## Surprises

- The RPC-error taxonomy in `NatsRpcClient.cs`/`IRpcClient.cs` was clearly built with exactly this kind of evidence in mind — `RpcTransportError`/`RpcTimeoutError`/`RpcBusinessError` are distinct types and each embeds its `subject` argument directly into `Exception.Message`, so once the test read the response body the "not a timeout" bar in the acceptance bullet was satisfied for free, with no change needed to production code.
- #7's over-inclusion property (a `quantity` field getting swept up as a "monetary" finding because it sits beside `unitPrice`) required no separate proof in #8 — it falls out of the identical algorithm operating on the identical nested shape (`OrderReadModelItem`), which is itself a small piece of evidence that the port is faithful rather than merely similarly-shaped.

---

# Fix round 2 (2026-09-12) — response to `progress/review_test_matrix_rows_that_outlived_their_named_closer.md`

Round 1 was REJECTED for one blocking finding (B1: the class was never enumerated repository-wide, and it was still live in `R48`/`R49`) and four advisories (A1–A4). This round does the enumeration first, corrects `R48`/`R49`, and answers all four advisories. Nothing in round 1's own five bullets was re-opened — the reviewer verified all of them directly and said so.

## 1. Class enumeration — BEFORE any cell was edited (acceptance bullet 6)

Population: all 63 `| **R<n>** |` rows' Status cell (column 5). Command, run first, before any edit in this round:

```
python3 - <<'EOF'
import re
text = open('specs/shared/test-matrix.md', encoding='utf-8').read()
rows = []
for ln in text.split('\n'):
    m = re.match(r'^\| \*\*(R\d+)\*\* \|', ln)
    if m:
        rows.append((m.group(1), ln.split(' | ')[-1]))

patterns = [
    r"does not exist yet", r"no Gateway", r"the gateway feature", r"exists yet",
    r"features 25/29", r"one layer below", r"no claim of API-level coverage",
    r"not yet", r"outstanding", r"still `pending`", r"owed to", r"named closer",
    r"unproven", r"deferred to", r"belongs to", r"no live caller",
    r"split inherited", r"inherited", r"carried without re-raising", r"remains unproven",
]
for rid, status in rows:
    for p in patterns:
        for m in re.finditer(p, status, re.IGNORECASE):
            print(f"{rid} | pattern={p!r} | ...{status[max(0,m.start()-40):m.end()+40]}...")
EOF
```

Complete output (24 raw regex hits, before de-duplicating overlapping patterns on the same row):

```
R1  | pattern='the gateway feature'
R24 | pattern='no Gateway'
R24 | pattern='the gateway feature'
R24 | pattern='exists yet'
R24 | pattern='outstanding'
R24 | pattern="still `pending`"
R24 | pattern='named closer'
R29 | pattern='split inherited'
R29 | pattern='inherited'
R29 | pattern='carried without re-raising'
R61 | pattern='the gateway feature'
R46 | pattern='no live caller'
R48 | pattern='does not exist yet'
R48 | pattern='features 25/29'
R48 | pattern='one layer below'
R48 | pattern='no claim of API-level coverage'
R49 | pattern='no Gateway'
R55 | pattern="still `pending`"
R55 | pattern='owed to'
R55 | pattern='unproven'
R55 | pattern='remains unproven'
R56 | pattern='unproven'
R56 | pattern='unproven'
R56 | pattern='deferred to'
```

One classification line per **row** that hit (population of hits collapses to 8 distinct rows; `R55`/`R56` each hit more than once on overlapping patterns describing the same fact):

| Row | Shape | Classification |
|---|---|---|
| `R1` | (a) named "the gateway feature" as closer | **Already closed by this feature** (round 1) — the closer shipped and this row was corrected to cite the real test directly, not a feature reference. Live, correct. |
| `R61` | (a) named "the gateway feature" as closer | **Already closed by this feature** (round 1), same shape as `R1`. Live, correct. |
| `R24` | (a) named "the gateway feature", now corrected — **but see round 3** | Round 2 classified only the CLOSER half ("names feature 31 `api_tests`, still `pending` — a live, honest deferral naming someone") and missed that the PREMISE half was still false ("no Gateway/API surface for order timelines exists yet" — it does; `OrdersEndpoints.cs:21`/`:80`, `OrderReadModelMapper.cs:22`'s `Events` member, `StreamEndpoints.cs:43`). **STALE in the premise half — found by the leader after this round, corrected in round 3 (§ below).** |
| `R48` | (a) named a closer whose premise is false ("does not exist yet"/"features 25/29"); (b) shortfall stated while counted Green | **STALE — the blocking finding.** Corrected in this round (§2 below). |
| `R49` | (a) "no Gateway yet"; (b) shortfall stated while counted Green | **STALE — the blocking finding.** Corrected in this round (§2 below), written standalone per the brief (not "same as R48"). |
| `R55` | names ids 29/30 as still owed | **Not stale** — ids 29/30 verified `pending` in `feature_list.json` today. A live, correctly-standing deferral. |
| `R56` | names id 28 as still owed | **Not stale** — id 28 `saga_e2e_verification` verified `pending` today. A live, correctly-standing deferral. |
| `R29` | "split inherited from #7's own gate ruling", "carried without re-raising" | **Not applicable** — provenance prose about where the retry/DLQ split originated, not a deferral naming a closer. No closer to go stale. |
| `R46` | "no live caller yet" | **THIS CLASSIFICATION WAS WRONG — see Fix round 4 and re-review round 2's finding B2.** It was recorded as **Not applicable** — a note about the fact-emission rule's double-force clause (no integration harness reaches the branch yet), not a deferral naming a shipped closer. |

Shape (b) was additionally checked with a narrower command restricted to cells whose Status begins `DONE` (i.e. counted Green) and which also contain shortfall language (`one layer below`, `no claim of`, `proven one layer`, `substitution`, `partially`, `shortfall`, `narrower than`): only `R48` and `R49` matched. No other Green-counted row discloses a shortfall against its own requirement's wording.

**Result as originally reported (INCORRECT — see Fix rounds 3 and 4 below): "exactly two stale instances of the class, both already known to the leader (`R48`, `R49`), zero new ones."** It was **four** — `R48` and `R49` (this round), `R24` (found by the leader, fixed in round 3) and `R46` (found by re-review round 2's finding B2, fixed in round 4). This sentence was itself corrected once, to "three", and was still wrong: **`R46` was sitting in this very enumeration's own hit list**, classified away as *"not applicable"*. Corrected by the leader at approval per re-review round 3 advisory A7. (The sentence that stood here between rounds 3 and 4 said "It was three"; that figure is superseded by the four named above.) `R24`'s premise half ("no Gateway/API surface for order timelines exists yet") was stale in exactly the same shape as `R48`/`R49`, and this enumeration missed it. No unclassified line was produced by the search command itself — the miss was in the classification step, not the search: `R24` DID appear in the hit list (patterns `no Gateway`, `exists yet`, twice), and the classification written against it addressed only whether the row named a closer, never whether the reason it gave for not being closed was still true. A deferral cell makes two claims — who closes it, and why it is not closed yet — and hardening one (R24's closer, corrected in round 1) does not harden the other; the next time this enumeration runs, both halves of every hit must be re-read against the codebase as it stands today, not just the half a prior round touched.

## 2. R48 / R49 corrected

Both cells (`specs/shared/test-matrix.md`) rewritten in Status column only:
- Dropped the false premise. Both state the true fact instead: `POST /invoices/{id}/payments` ships at `src/Gateway/Presentation/Endpoints/InvoicesEndpoints.cs:32` (feature 25 `gateway_rest_auth`, `done`, phase 13).
- Named why the shortfall is real anyway: the four Gateway tests that exercise it (`tests/Gateway.IntegrationTests/InvoicesHttpTests.cs:117,133,153,169`) stub `IRpcClient` and never reach Billing's own database.
- Named the real closer: **feature 31 `api_tests`** (phase 18, `pending`) — bullet 3 for `R48`'s idempotency, bullet 5 for `R49`'s three refusals (verified present in `feature_list.json`, not assumed).
- Moved both **Green → Scoped**, ratification named as this correction itself (backlog id 72's class-enumeration finding, `progress/review_test_matrix_rows_that_outlived_their_named_closer.md` finding B1, `feature_list.json` id 72 acceptance bullet 6, 2026-09-12) — the same shape rule 3(b) requires and the same shape `R24` already carries.
- `R49` written **standalone**, not by cross-reference to `R48` — deliberately, because #7's own `R49` cell still reads "same NATS-level caveat as R48 (no Gateway yet)" after #7's `R48` stopped carrying that caveat (`8a3a3d3`); that stale cross-reference is exactly the residue the brief said not to inherit.
- All existing unit/integration evidence for both rows kept unchanged, explicitly framed as "the deeper, race-focused proof beneath the API-level closer, not a substitute for it."

Columns 1–4 of both rows verified byte-identical to the pre-feature baseline (md5, §5 below).

## 3. Recount, re-derived by name

Moved: `R48` Green→Scoped, `R49` Green→Scoped. Nothing else in the file changed class in this round (`R1`/`R24`/`R61` were already settled and re-verified, not re-classified).

Per-section table, `billing_invoicing` (§6, `R45`–`R49`):

| Row | Class before this round | Class after |
|---|---|---|
| `R45` | Green | Green (unchanged) |
| `R46` | Green | Green (unchanged) |
| `R47` | Green | Green (unchanged) |
| `R48` | Green | **Scoped** |
| `R49` | Green | **Scoped** |

Section 6: Rows 5, Green 5→**3**, Scoped 0→**2**, Not-yet-green 0 (unchanged). 3+2+0=5 ✓.

Full coverage summary, reconciled row-id by row-id against round 1's own count (63 rows, no duplicates, none missing — re-verified, unchanged from round 1's P2):

| Feature | Rows | Green | Scoped | Not yet green |
|---|---:|---:|---:|---:|
| 1. `orders_aggregate` | 10 | 10 | 0 | 0 |
| 2. `outbox_and_idempotency` | 8 | 8 | 0 | 0 |
| 3. `order_saga_orchestrator` | 11 | 10 | 1 | 0 |
| 4. `fulfillment_stock` | 8 | 8 | 0 | 0 |
| 5. `billing_credit` | 8 | 8 | 0 | 0 |
| 6. `billing_invoicing` | 5 | **3** | **2** | 0 |
| 7. `projector_read_model` | 6 | 5 | 0 | 1 |
| 8. `observability_reliability` | 6 | 5 | 1 | 0 |
| 8.1 gateway edge protection | 1 | 1 | 0 | 0 |
| **Total** | **63** | **58** | **4** | **1** |

Reconciliation: 10+8+10+8+8+3+5+5+1 = **58** Green; 0+0+1+0+0+2+0+1+0 = **4** Scoped; 0+0+0+0+0+0+1+0+0 = **1** Not-yet-green. 58+4+1=63 ✓. Against the round-1 total (60/2/1): Green 60−2=58, Scoped 2+2=4, Not-yet-green 1 unchanged — exactly the two rows that moved, by name, no other movement. The "Scoped rows" paragraph rewritten to say "**Four** rows are currently scoped" and names all four (`R24`, `R48`, `R49`, `R56`) with who ratified each and what closing each takes; the narration paragraph below it states the movement by name (`R48`, `R49`, Green 60→58, Scoped 2→4).

## 4. Advisories

**A1 — re-armed with a message naming both endpoint and field.** The reviewer's own arm (`src/Gateway/Domain/Projection/OrderReadModelMapper.cs:94`, `doc.Currency` → `doc.Currency?.ToLowerInvariant()`) was re-run by me: built (`dotnet build --no-incremental tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj`, 0 Error(s)), ran `MoneyRepresentationHttpTests`, failure `GET /orders/{id} $.totals.initialAmount: R1 requires an ISO 4217 alpha-3 currency code accompanying the amount, got "eur"` — names the endpoint (`GET /orders/{id}`) and the field (`$.totals.initialAmount`). Restored via `cp` from a fresh backup, `cmp` identical, forced `dotnet build --no-incremental`, re-ran: `Passed! - Failed: 0, Passed: 1, Total: 1`. The old currency→`null` arm is kept in the test-matrix cell as a *second, disclosed* guard against a total currency loss (it still only trips the vacuity message, which names the endpoint but no field, so it is explicitly not counted as satisfying this bullet). `test-matrix.md`'s `R1` cell and this record's arming table row 2 both updated to cite the new arm's verbatim message.

**A2 — added a direct case that makes the clause the discriminator.** `isCanonicalMoneyAmount` (`MoneyFieldSweep.cs:122`) has no case in any HTTP response `MoneyRepresentationHttpTests` sweeps, because the standalone `Money{amount,currency}` shape only appears in `RegisterPaymentRequest` (a request body) and `RegisterPaymentResponse` never echoes it back — checked against `specs/shared/openapi.yaml:1800-1819`. Rather than leave the clause undischarged, added `tests/Gateway.IntegrationTests/MoneyFieldSweepCanonicalAmountTests.cs` › `ACanonicalMoneyAmountThatHasRegressedToADecimalStringIsStillDiscovered`, which calls `MoneyFieldSweep.SweepForMoneyFields` directly (no HTTP, no host) on a body shaped exactly like `openapi.yaml`'s own `Money` example (`:689-692`) with `amount` regressed to a JSON string. Armed: mutated `isCanonicalMoneyAmount = false && property.Name == CanonicalAmountKey && hasOwnCurrency`, built (0 Error(s)), ran the named test — `Assert.Single() Failure: The collection was empty` (the clause was the only path that could have found this string-typed `amount`; `isNumberUnderCurrencyContext` requires `ValueKind == Number`). Restored via `cp`, `cmp` identical, forced rebuild, re-ran: `Passed! - Failed: 0, Passed: 1, Total: 1`. `test-matrix.md`'s `R1` cell updated to cite this new file and its verbatim arming message.

**A3 — cited the deeper Fulfillment test that already proves the other two clauses.** `R61`'s sketch (column 4, unchanged) names three clauses: units up, reservations/orders untouched, no fact emitted. The Gateway test asserts only `Units == 35`. `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs` › `HappyPath_UnitsUp_ReservedUnitsAndReservationsUntouched_OutboxEmpty` (read in full) already asserts, against the same real MS-SQL database: `row.ReservedUnits == 4` (unchanged), the existing reservation's `Status == "reserved"` (untouched), and `OutboxMessages` count `== 0` (no fact) — the identical three properties #7 proves in `apps/fulfillment/src/stock-replenish.integration.spec.ts:19` (read in full, see A4 below), and strictly more than #7's own Gateway test (which only echoes a stubbed reply). Chose to **cite the deeper test in the `R61` cell** rather than mark it Scoped, since between the two tests the whole sketch is now proven — no shortfall remains to disclose, and no count moves.

**A4 — #7's R61 guards enumerated, assertion by assertion.**

`apps/gateway/src/billing-fulfillment.integration.spec.ts` (read in full) — the whole file's Group C section, `R61`-relevant part only:

| # | #7 assertion | Classification | #8 rendering |
|---|---|---|---|
| 1 | `it('R61 — POST /stock/replenish translates to fulfillment.stock.replenish')`: `response.status === 200` | ported | `FulfillmentStockEndToEndTests.cs` asserts the real HTTP response succeeds (`IsSuccessStatusCode`) |
| 2 | `response.body.items[0].units === 200` (a STUBBED reply's echoed value — the RPC responder is a test double, never a real Fulfillment) | **not ported (reason: strengthened)** — #8 boots the REAL `FulfillmentHost` against real MS-SQL and asserts `Units == 35` was actually written to Fulfillment's own database, not merely echoed by a stub. #7 never reaches a database for this endpoint at all. | `FulfillmentStockEndToEndTests.cs` — real Gateway→NATS→Fulfillment→MS-SQL round trip |
| 3 | No RPC-subject-substitution or corruption arming exists anywhere in this file for `/stock/replenish` — the test is unarmed in #7 | not applicable to a port ledger (nothing to port; #7 has no guard here) | #8 adds both arms itself (round 1's table rows 3/6) — a strengthening with no #7 precedent to lose |

`apps/fulfillment/src/stock-replenish.integration.spec.ts` (read in full):

| # | #7 assertion | Classification | #8 rendering |
|---|---|---|---|
| 1 | `it('happy path: units up, reserved_units and reservations untouched, outbox empty (R61)')` — reply's `items` equals the expected units/reservedUnits/availableUnits/lowStockThreshold shape | ported | `StockReplenishTests.cs` › `HappyPath_UnitsUp_ReservedUnitsAndReservationsUntouched_OutboxEmpty` — same reply-shape assertion |
| 2 | `stockRow.units === 16` (post-replenish DB read) | ported | `row!.Units == 30` (this file's own fixture numbers) |
| 3 | `stockRow.reservedUnits === 4` (untouched) | ported | `row.ReservedUnits == 4` |
| 4 | `reservationRows` has length 1, `status === 'reserved'` (untouched) | ported | `Assert.Single(reservations).Status == "reserved"` |
| 5 | outbox rows filtered by `aggregateId` have length 0 (no fact) | ported | `db.OutboxMessages` count `== 0` (this codebase has one outbox table, not filtered by aggregate, since the seeded fixture is the only stock row touched in the test) |
| 6 | `it('replies NOT_FOUND and replenishes no line when any line names an unknown product')` — `reply.code === 'NOT_FOUND'`, `stockRow.units` unchanged (all-or-nothing) | ported | `StockReplenishTests.cs` › `FS14_RepliesNotFoundAndReplenishesNoLine_WhenAnyLineNamesAnUnknownProduct` |

Nothing lost — A3 is where the one real gap lived (the Gateway-level citation not reaching the deeper test), and it is now closed by citation rather than by a new test, since the deeper test already existed and already passed.

## 5. Re-verification of columns 1–4 (unit: the column, per row touched this round)

```
for r in R1 R24 R48 R49 R61; do old=$(git show HEAD:specs/shared/test-matrix.md | grep -P "^\| \*\*$r\*\* \|" | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); new=$(grep -P "^\| \*\*$r\*\* \|" specs/shared/test-matrix.md | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); echo "$r old=$old new=$new"; done
R1  old=0c65fc31c3641a37d35754fb14b3dd3c  new=0c65fc31c3641a37d35754fb14b3dd3c
R24 old=e12e4ca539c2e8c981e72bc92c727082  new=e12e4ca539c2e8c981e72bc92c727082
R48 old=743805041a2f1b6bb85cd0dd044fbc2e  new=743805041a2f1b6bb85cd0dd044fbc2e
R49 old=bf5f1088aa3e557a5eb0bf4d9c901b14  new=bf5f1088aa3e557a5eb0bf4d9c901b14
R61 old=7b2035fa026244f20ee82b25d9883ccc  new=7b2035fa026244f20ee82b25d9883ccc
```

Identical for all five. `git diff --stat specs/shared/test-matrix.md`: 1 file changed, 12 insertions(+), 8 deletions(-) — Status cells for `R1`/`R48`/`R49`, the coverage-summary table, and the "Scoped rows" paragraph only. `git status --porcelain specs/shared/` shows only `test-matrix.md`. `feature_list.json` was not edited by me this round (`git status` shows it modified from the leader's own prior edits, confirmed by `git diff --stat` showing the same 72-insertion/6-deletion shape the brief described as already done).

## 6. Arming table addendum (round 2)

| # | Row | Mutation | Backup / restore | Named test run | Verbatim failure | Restored + green? |
|---|---|---|---|---|---|---|
| 7 | R1, A1 fix (currency shape) | `OrderReadModelMapper.cs:94`: `doc.Currency` → `doc.Currency?.ToLowerInvariant()` | `cmp` clean after restore | `MoneyRepresentationHttpTests.EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode` | `GET /orders/{id} $.totals.initialAmount: R1 requires an ISO 4217 alpha-3 currency code accompanying the amount, got "eur"` | Yes — forced `--no-incremental` rebuild, 1/1 green |
| 8 | R1, A2 fix (canonical clause) | `MoneyFieldSweep.cs:122`: `isCanonicalMoneyAmount = property.Name == CanonicalAmountKey && hasOwnCurrency` → `false && …` | `cmp` clean after restore | `MoneyFieldSweepCanonicalAmountTests.ACanonicalMoneyAmountThatHasRegressedToADecimalStringIsStillDiscovered` | `Assert.Single() Failure: The collection was empty` | Yes — forced `--no-incremental` rebuild, 1/1 green |

## 7. Quality gates (round 2)

- `dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --no-build` (full project, no filter): `Passed! - Failed: 0, Passed: 61, Skipped: 0, Total: 61, Duration: 6 m 12 s`. 60 (round-1 baseline) + 1 new fact (`MoneyFieldSweepCanonicalAmountTests`) = 61 ✓.
- `./quality.sh` → `/tmp/claude-1000/.../scratchpad/quality_feature72_fix_round.log`. `[OK]` at format, build, test and finish. 18 projects, 0 failed. Per-project `Total:` lines summed: 23+50+24+107+211+130+238+459+6+44+120+26+59+22+64+61+146+90 = **1880**. Reconciled against round 1's 1879: 1879 + 1 (the new `MoneyFieldSweepCanonicalAmountTests` fact) = 1880 ✓. `Gateway.IntegrationTests` 60 → 61, matching the standalone run above; no other project's count moved.
- `./init.sh` → exit 0. §5d: "shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt." §5b backlog tripwire clean. No new warnings beyond the expected "uncommitted changes, mid-session" and "run quality.sh before closing" (already run, above).

## What could not be done / scope notes (round 2)

- `feature_list.json` was not touched, per the brief's bound — both needed edits (bullet 6 on id 72, bullet 5 on id 31) were already present before this round started, verified rather than assumed.
- No other `specs/shared/` file was touched.
- `R48`/`R49` were moved to Scoped, not closed — closing them still requires feature 31 `api_tests` to ship, exactly as the corrected cells now state.

---

# Fix round 3 (2026-09-12) — a third stale instance found by the leader after round 2

The leader independently re-verified every claim of round 2 (columns 1–4 md5-identical across all 63 rows, only `test-matrix.md` changed under `specs/shared/`, `feature_list.json` untouched, `init.sh` exit 0, the 1880-total reconciliation, A1/A2/A4, and A3's cited Fulfillment test read directly) and accepted all of it. One finding remained: **`R24`'s Status cell still carried a false premise** — "no Gateway/API surface for order timelines exists yet" — the same defect class as `R48`/`R49`, missed by round 2's own enumeration.

## Why it survived round 2's enumeration (the transferable lesson)

Round 2's search command DID hit `R24` (patterns `no Gateway`, `exists yet`, `outstanding`, `still `pending``, `named closer` — see §1's hit list above). The miss was not in the search, it was in the **classification**: round 2 read the hit, saw that round 1 had already corrected `R24`'s CLOSER half (from "the gateway feature" to "feature 31 `api_tests`, still pending"), and classified the whole row as settled. It never separately asked whether the PREMISE half — the sentence stating *why* the row is not yet closed — was still true. It was not: the Gateway surface for order timelines exists (`GET /orders/{id}` at `src/Gateway/Presentation/Endpoints/OrdersEndpoints.cs:21`,`:80`, whose `OrderDetailView` carries the timeline via its `Events` member, `src/Gateway/Domain/Projection/OrderReadModelMapper.cs:22`, mapped from the projector's own `order_timeline` document; `GET /orders/stream` ships alongside it, `StreamEndpoints.cs:43`). What is actually still missing is narrower and different: no test AT that surface asserts R24's own subject, the completion triple present and causally ordered — confirmed by a content search, `grep -rl "order.completed.v1" tests/ --include="*.cs"`, whose 14 hits are all Orders/Projector/Notifications/Contracts files, none Gateway.

**A deferral cell makes two claims — who closes it, and why it is not closed yet.** `R48`/`R49` were stale in both halves in round 1's enumeration and both were caught, because nothing had touched either half before. `R24` was stale in only the premise half, because round 1 had already hardened its closer half — and that prior hardening is exactly what caused round 2 to read the row as fully dealt with and stop checking. This is the general form CLAUDE.md already states for ledger rows: *tightening one half of a two-part claim does not harden the other, and will quietly borrow attention from it.* The corollary for enumeration work: **when a hit's closer half was corrected in a prior round, that is precisely the row whose premise half must be re-read against the codebase as it stands today, not skipped as "already handled."**

## What changed

`specs/shared/test-matrix.md`, `R24`'s Status cell only: replaced "no Gateway/API surface for order timelines exists yet" with the true statement (the surface exists, cited above) and the true reason the row stays open (no Gateway-level test asserts the completion triple's presence and causal order — the content-search evidence above). Kept unchanged: the integration-half citation, the gate-ratification provenance (2026-09-04, `progress/spec_order_saga_orchestrator.md` row 13), and feature 31 `api_tests` as the named closer with its bullet.

Checked the "Scoped rows" paragraph's `R24` sentence for the same false premise: it does not carry one — it says only "the `order_saga_orchestrator` gate ratified the API half's deferral," never restating "no Gateway exists" — so no edit was needed there.

**No count moves.** `R24` stays Scoped; the Total stays 63/58/4/1; section 6 stays 5/3/2/0; the Scoped paragraph's "Four rows" is unchanged. Only the stated reason inside `R24`'s own cell was wrong, and only that sentence changed.

## Verification (unit: the column, per row)

```
for r in R1 R24 R48 R49 R61; do old=$(git show HEAD:specs/shared/test-matrix.md | grep -P "^\| \*\*$r\*\* \|" | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); new=$(grep -P "^\| \*\*$r\*\* \|" specs/shared/test-matrix.md | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); echo "$r old=$old new=$new"; done
R1  old=0c65fc31c3641a37d35754fb14b3dd3c  new=0c65fc31c3641a37d35754fb14b3dd3c
R24 old=e12e4ca539c2e8c981e72bc92c727082  new=e12e4ca539c2e8c981e72bc92c727082
R48 old=743805041a2f1b6bb85cd0dd044fbc2e  new=743805041a2f1b6bb85cd0dd044fbc2e
R49 old=bf5f1088aa3e557a5eb0bf4d9c901b14  new=bf5f1088aa3e557a5eb0bf4d9c901b14
R61 old=7b2035fa026244f20ee82b25d9883ccc  new=7b2035fa026244f20ee82b25d9883ccc
```

Columns 1–4 identical for all five rows I have touched across this feature (this round's `R24` edit is Status-column-only, same as every prior edit). `git status --porcelain specs/shared/` shows only `test-matrix.md`. `feature_list.json` not touched this round (bound honoured).

`./init.sh`: exit 0, §5d unchanged ("shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt"), §5b backlog tripwire clean, no new warnings.

Per the brief: no source or test changed this round, so no build and no suite run was performed — none was authorised or required.

## What could not be done / scope notes (round 3)

- Nothing outside the one sentence in `R24`'s Status cell was changed.
- `feature_list.json` was not touched.

---

# Fix round 4 (2026-09-12) — the fourth stale instance, `R46`, found in re-review round 2

Re-review round 2 REJECTED for one blocking finding (**B2**): `R46`'s Status cell still read *"No live caller yet (feature 22's seam)"*, and every clause of that premise is false today — feature 22 `billing_remittance_intake` (`done`, phase 10) is the live caller, wired and reachable, and proven against real infrastructure. Everything else in round 2's re-review (B1 resolved, A1–A4 closed, R48/R49/R24 citations exact, counts exact, columns 1–4 md5-identical) was accepted and is not re-opened here.

## 1. `R46` corrected

`specs/shared/test-matrix.md`, `R46`'s Status cell, verified against source before writing (all citations read at the exact lines cited, this round, independently — not copied from the reviewer's finding):

- `src/Billing/Application/PaymentRegisterService.cs:12` — doc-comment: *"feature 22, the sole LIVE caller of `Invoice.MarkPaid` and `BuyerCredit.Release`, both delivered uncalled by features 21/19"*; the call itself is at `:131` (`var paymentEventId = invoice.MarkPaid(markPaidInput, ctx, UniqueId.New);`) and its persistence at `:144`.
- `src/Billing/Presentation/BillingRpcResponder.cs:68` subscribes `InvoiceSubjects.PaymentRegister`; `:214` dispatches to `HandlePaymentRegisterAsync`; `:326` sends `RegisterPaymentCommand`.
- `feature_list.json` id 22 `billing_remittance_intake`: `done`, phase 10 — verified by reading the entry, not assumed.
- `tests/Billing.IntegrationTests/PaymentRegisterTests.cs:13-21`'s own header: *"the sole LIVE caller of `Invoice.MarkPaid` and `BuyerCredit.Release`… this file is what proves their existing guards hold through the live path"*, against real MS-SQL/NATS/Kafka; the named test is `R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` (`:26`) — already cited two rows below, in `R47`'s own cell.

New cell text (Status column only): kept the existing named unit test and the `F8` double-force arming note, reframed as historical (*"armed by deletion (`F8`) when no live caller existed"*), dropped *"No live caller yet (feature 22's seam)"*, and added the live-caller citations and the integration-proof citation above, in the same shape as R24/R48/R49's corrections. Columns 1–4 untouched.

**Per the leader's ruling: `R46` stays Green, no count moves.** *"No live caller"* was never a shortfall against R46's own requirement (the issued→paid transition rule, `B8`/`B9`) — it was a note justifying extra arming under the fact-emission rule's double-force clause, and that clause's premise is simply obsolete now. The Total stays 63/58/4/1, §6 `billing_invoicing` stays 5/3/2/0, the Scoped paragraph's "Four rows" is unchanged (it never named `R46`). I did not move it, and I am recording that I considered moving it and rejected it, per the brief's instruction to stop and say so rather than move a count I believed should change — I do not believe it should change.

## 2. Enumeration re-run as a search result (my own, independent of the leader's sweep)

Same pattern set as round 2's, widened with the shapes the reviewer's B2 finding named (`has no caller`, `no caller`, `ships uncalled`, `no live path`, `not yet exist`, `never built`, `until feature`), run against `specs/shared/test-matrix.md` as it stands after the `R46` edit above:

```
python3 - <<'EOF'
import re
text = open('specs/shared/test-matrix.md', encoding='utf-8').read()
rows = []
for ln in text.split('\n'):
    m = re.match(r'^\| \*\*(R\d+)\*\* \|', ln)
    if m:
        rows.append((m.group(1), ln.split(' | ')[-1]))
patterns = [
    r"does not exist yet", r"no Gateway", r"the gateway feature", r"exists yet",
    r"features 25/29", r"one layer below", r"no claim of API-level coverage",
    r"not yet", r"outstanding", r"still `pending`", r"owed to", r"named closer",
    r"unproven", r"deferred to", r"belongs to", r"no live caller",
    r"split inherited", r"inherited", r"carried without re-raising", r"remains unproven",
    r"has no caller", r"no caller", r"ships uncalled", r"no live path", r"not yet exist",
    r"never built", r"until feature",
]
total=0
for rid, status in rows:
    for p in patterns:
        for m in re.finditer(p, status, re.IGNORECASE):
            total+=1
            print(f"{rid} | pattern={p!r} | ...{status[max(0,m.start()-40):m.end()+40]}...")
print("total hits:", total)
EOF
```

Complete output: **29 hits**, rows `R1`(1), `R24`(**6**), `R29`(3), `R61`(1), `R46`(1), `R48`(5), `R49`(5), `R55`(4), `R56`(3) — 29 total, matching the script's own `total hits: 29`.

> **Corrected by the leader at approval (2026-09-12), re-review round 3 advisory A8.** This line originally read `R24`(5) and claimed the figures were *"hand-counted against the printed list"*. The per-row list as printed summed to **28**, not 29, so that reconciliation cannot have been performed as described — the discrepancy was carried by a claim that it had been checked. The reviewer found the inconsistency; the leader re-ran the round-4 command verbatim and got `total hits: 29` with per-row `R1:1, R24:6, R29:3, R46:1, R48:5, R49:5, R55:4, R56:3, R61:1`, which sums to 29. **`R24` is 6.** Worth stating plainly because it is this feature's own subject turned on its own record: a compressed one-line summary hid a count that did not add up, and the words *"hand-counted"* made it read as verified. A total and its parts are two claims, and agreeing with a script's total says nothing about whether the parts were read.

Classification, one line per hit-row (all previously-classified rows re-checked against today's tree, not copied from round 2/3's record):

| Row | Verdict |
|---|---|
| `R1` | Historical closer reference only, premise (A2's scope claim) verified true in round 2 — unchanged, live, correct |
| `R24` | Corrected in round 3; premise re-verified this round (surface exists, no Gateway-level test asserts the completion triple) — live, correct |
| `R29` | Provenance prose, no closer — not applicable, unchanged |
| `R61` | Historical closer reference, both clauses of the sketch now proven across two cited tests — live, correct |
| `R46` | **Corrected this round.** New text hit only on "no live caller" because it now reads "…when no live caller **existed**" (past tense, historical) — read in full, the premise is now true: a live caller has existed since feature 22, and the cell says so. Live, correct. |
| `R48` | Corrected in round 2, re-verified this round: `InvoicesEndpoints.cs:32` exists, the four cited test line numbers are exact, feature 31 named and `pending` — live, correct |
| `R49` | Corrected in round 2, re-verified this round: same shape as R48, standalone (not cross-referenced) — live, correct |
| `R55` | ids 29/30 verified `pending` in `feature_list.json` this round — live, correct |
| `R56` | id 28 `saga_e2e_verification` verified `pending` this round — live, correct |

**No fifth instance of the class found.** Every hit-row's premise half was re-read against today's tree (not against the previous round's classification), per the corrected discipline below.

## 3. The #7 cross-check, run against #7's checkout myself

```
cd /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs
python3 - <<'EOF'
import re
text = open('specs/shared/test-matrix.md', encoding='utf-8').read()
rows = []
for ln in text.split('\n'):
    m = re.match(r'^\| \*\*(R\d+)\*\* \|', ln)
    if m:
        rows.append((m.group(1), ln.split(' | ')[-1]))
patterns = [r"used to close", r"stale since", r"traceability pass", r"live-path evidence", r"no longer"]
for rid, status in rows:
    for p in patterns:
        for m in re.finditer(p, status, re.IGNORECASE):
            print(f"{rid} | pattern={p!r} | ...{status[max(0,m.start()-60):m.end()+80]}...")
EOF
```

Output (trimmed to the row identity and the correction each carries): `R24` and `R28` hit on "traceability pass" for unrelated browser-corroboration additions, not a stale-premise correction. `R40` — *"used to close with `consumeHold` has no caller until feature 21… stale since feature 21 landed"*. `R41` — *"used to close with `releaseHold` has no caller until features 22/25… stale: both callers shipped"*. `R46` — *"used to close with `markPaid` ships uncalled by any live path… stale since feature 22"*. `R55` — web-half evidence added in the same pass, different shape (a `TODO` that had gone stale the other direction). `R58` — `DONE` → `SCOPED` → closed with per-site guards, also the same pass, different shape.

Cross-referenced against #8's current cells: `R40` and `R41` are clean (one line each, no "has no caller"/"until feature" residue — confirmed by the round-2 enumeration's pattern list, which includes both phrasings and hit neither row). `R46` **was** the one carrying the pre-correction wording, now fixed above. `R55` and `R58` are clean in #8 (`R55` names ids 29/30 correctly; `R58` reads `DONE`, six services cited, no `SCOPED`/two-service-gap residue).

## 4. A6 restated at the level that bites (per the brief's item 5)

Round 3's stated lesson — *"when a hit's closer half was corrected in a prior round, re-read its premise half"* — is true and was too narrow: it predicts checking only rows a prior round had touched, and `R46`'s closer half was never touched by any prior round of this feature, yet it was still carrying a stale premise. The lesson that would have caught all four instances (`R48`, `R49`, `R24`, `R46`) in one pass: **every hit a sweep returns is a claim about today's tree, and the claim must be executed against today's tree — grepped, read at the cited line, checked against `feature_list.json` — every time the sweep runs, regardless of whether the row was edited in a prior round.** A classification line that says "not applicable" or "already handled" is itself a claim and needs its own evidence, not an inheritance from the previous round's classification. And where a sibling assessment has already corrected the same artefact for the same reason — #7's Phase 25 traceability pass on this same `test-matrix.md` shape — its corrected cells are the cheapest oracle available and should be diffed against before, not after, a rejection names the gap.

## 5. A5 — routed, not dropped

Per the brief, `src/Billing/Domain/Invoice.cs:24`'s companion stale sentence (*"uncalled until `billing.payment.register` exists"*) is **routed by the leader to backlog id 78**, which already corrects stale remarks in named `src/` files. Not touched in this feature — bound honoured (no `src/` change, per the brief's explicit prohibition this round).

## 6. Verification

```
for r in R1 R24 R46 R48 R49 R61; do old=$(git show HEAD:specs/shared/test-matrix.md | grep -P "^\| \*\*$r\*\* \|" | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); new=$(grep -P "^\| \*\*$r\*\* \|" specs/shared/test-matrix.md | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); echo "$r old=$old new=$new"; done
R1  old=0c65fc31c3641a37d35754fb14b3dd3c  new=0c65fc31c3641a37d35754fb14b3dd3c
R24 old=e12e4ca539c2e8c981e72bc92c727082  new=e12e4ca539c2e8c981e72bc92c727082
R46 old=93701670f14c4a28483711901e35ed24  new=93701670f14c4a28483711901e35ed24
R48 old=743805041a2f1b6bb85cd0dd044fbc2e  new=743805041a2f1b6bb85cd0dd044fbc2e
R49 old=bf5f1088aa3e557a5eb0bf4d9c901b14  new=bf5f1088aa3e557a5eb0bf4d9c901b14
R61 old=7b2035fa026244f20ee82b25d9883ccc  new=7b2035fa026244f20ee82b25d9883ccc
```

Identical for all six rows touched across this feature — `R46`'s columns 1–4 unchanged by this round's Status-only edit (`old` here is HEAD, i.e. before this session's own commit; since no commit has landed yet this round, `old` and `new` are computed against the same on-disk file, so this is the row-shape check, not a diff against a prior commit — the meaningful check is the full-population one below). Also re-ran a whole-file columns-1–4 md5 (all 63 rows, one hash) against the last commit via `git stash`/`git stash pop`: **`28c4af85b71530ac953226cc1107e16a`** on both sides, 63 rows both sides — confirms no column 1–4 byte anywhere in the file moved, not just on the six rows named above.

`git status --porcelain specs/shared/` shows only `test-matrix.md` modified; no other `specs/shared/` file touched. `feature_list.json` not touched this round (`git diff --stat feature_list.json` unchanged from before this round started — the file's other modifications visible in `git status` are pre-existing, from the leader's own prior work on this and other features, not from this round).

`./init.sh`: exit 0. §3 backlog coherence green (83 features, one `in_progress`: `test_matrix_rows_that_outlived_their_named_closer`). §5d: "shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt." §5b backlog tripwire clean. §7 warns only that `./quality.sh` was not run — correctly: per the brief, no `.cs` file changed this round (Status-column-only edit to `test-matrix.md`), so no build or suite re-run is owed or authorised, and the round-2 green run (18 projects, 0 failed, 1880 total) stands.

## What could not be done / scope notes (round 4)

- Only `R46`'s Status cell was changed in `specs/shared/test-matrix.md`. No other file under `specs/shared/` touched.
- No `src/` or `tests/` change of any kind — A5 is routed to backlog id 78, not fixed here, per the brief's explicit bound.
- `feature_list.json` not touched.
- No count moved: 63/58/4/1 total, §6 5/3/2/0, "Four rows" Scoped paragraph unchanged — all reconfirmed by reading, not assumed unchanged.
