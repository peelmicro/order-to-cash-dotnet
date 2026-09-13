# review — batch D1: backlog ids 84 and 70

**Verdict: APPROVED (id 84) — APPROVED (id 70).**

Scoped review under the phase-14 budget ruling. The leader had already verified first-hand the unit counts (237/262/491/146, exit 0), the 32→22 `record` line drop in `GatewayRpcPayloads.cs`, the two deliberate renames and their in-file reasons, that `specs/shared/` is clean and that `feature_list.json` was untouched by the implementer. Those were **not** re-run. What follows is what this review did itself.

---

## 1. Armings re-armed from scratch (4, against the brief's minimum of 3)

Protocol each time: `cp` backup → mutate → `dotnet build <project> --no-incremental` (0 errors) → run the ONE named test → restore from the backup → `cmp` (byte-identical) → re-read the changed line → `touch` → forced rebuild → confirming green. No `git checkout`, no `git stash`, no git command that writes the index or working tree. `pgrep -fl "dotnet (build|test|format)"` was empty before each build and no two runs overlapped.

| # | Entry | Mutation | Named test | Result |
|---|---|---|---|---|
| 1 | **84** | `src/Contracts/Rpc/CreditRpcPayloads.cs` `CreditViewPayload` + `string Amount = "ARMING-ID84-NOT-MINOR-UNITS"` | `Billing.UnitTests.CreditRpcPayloadTests.BC23_…ParsedFromTheSpecNeverRetyped(schemaName: "CreditView", …)` | **FAIL**, `Failed: 1, Passed: 17, Total: 18` — *"…UNDECLARED by the schema: [amount]."* |
| 2 | **84** | same mutation | `Gateway.IntegrationTests.MoneyRepresentationHttpTests.EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode` | **FAIL** — *"GET /credits $.items[0].amount: R1 requires an integer count of minor units, got \"ARMING-ID84-NOT-MINOR-UNITS\" (kind String)"* |
| 3 | **70** (arming A) | `src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs` `OrdersCancelReplyPayload` + `string? UndeclaredArmingProperty = null` | `Orders.UnitTests.OrdersCancelPayloadTests.BC23_…ReadFromTheRecordNeverRetyped(schemaName: "OrdersCancelReplyPayload", payloadType: typeof(OrderToCash.Orders.Presentation.Rpc.OrdersCancelReplyPayload))` | **FAIL**, `Failed: 1, Passed: 490, Total: 491` — *"…UNDECLARED by the schema: [undeclaredArmingProperty]."* |
| 4 | **70** (arming C) | `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs` row `{ "StockCheckRequestPayload", typeof(StockCheckRequestPayload) }` → `{ "StockReplenishRequestPayload", typeof(StockCheckRequestPayload) }` | key-set theory **PASSES 12/12** (the blindness), `BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims` **FAILS 1/12** naming both halves | as recorded |

Every message reproduced the record's transcript verbatim. Counts matched the record exactly (18, 491, 12).

**The two judgements the brief asked for, answered.**

**(a) Id 70's failures are caused by the added/undeclared property being detected, not by an incidental error.** Arming 3's message is the `UNDECLARED by the schema: [undeclaredArmingProperty]` branch of `AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties` (`tests/Orders.UnitTests/AsyncApiSchema.PayloadRecords.cs:59`), the record's own name camelCased, with `MISSING: []` — the exact signature of defeat-list attack #7 inverted (an **added** element). The other 490 tests passed, so this theory is the sole detector: before id 70 the suite was green on this mutation, which is the entry's whole claim re-measured on today's ground.

**(b) The substitution family's false negative is handled.** In arming 4 the substituted sibling `StockReplenishRequestPayload` is a **real schema that resolves** — proven because the key-set theory passed 12/12 under the substitution (it parsed the sibling's `{ companyCode, lines }` and found them equal). So the schema-name theory failed for the **name** reason and not for an *absence / no such schema* reason, which is what would have made the arming worthless. `AsyncApiSchema.PropertyNamesOf` throws by name on an unknown schema, so the two failures are distinguishable in any case.

**A fifth probe, independent and not in the implementer's record.** The one hazard the brief flagged is that a record with **two** definitions may be guarded on only one side. Mutating the **Gateway's own** `ProductPayload` (`src/Gateway/Application/Rpc/GatewayRpcPayloads.cs:105`, a Gateway-only record id 84 kept) failed `Gateway.UnitTests.GatewayRpcPayloadTests.GatewayPayload_…(schemaName: "Product", payloadType: typeof(OrderToCash.Gateway.Application.Rpc.ProductPayload))` — `Failed: 1, Passed: 236, Total: 237`. Both sides of the surviving duplication are therefore genuinely guarded, which is the claim §3.5 of the record makes and does not itself arm.

Residue sweep after all restores, excluded by path at the `find` predicate, complete output empty:

```
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'ARMING-ID84\|UndeclaredArmingProperty'
```

Confirming greens on forced `--no-incremental` rebuilds: Billing **262/262**, Orders **491/491**, Fulfillment **146/146**, Gateway.UnitTests **237/237**, Contracts **24/24**, Architecture.Tests **36/36**, Gateway HTTP subset (`InvoicesHttpTests` + `MoneyRepresentationHttpTests` + `OrdersHttpTests`) **15/15**. 0 failed, 0 skipped everywhere.

---

## 2. Id 84's population, counted independently before accepting the classification

The population was re-derived from the **committed** file (`git show HEAD:src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`, a read-only git command): **29** `public sealed record` declarations, the same 29 names at the same 29 line numbers the record's table lists. Current file: **12**. 29 − 17 = 12. ✔

The classification was then re-derived mechanically rather than read: a parser extracting each record's positional parameter list (name **and** type) from the HEAD Gateway file and from `src/Contracts/Rpc/*.cs` + `src/Contracts/Facts/*.cs`, compared property-for-property.

- **17 unified** — 14 name-matched with **identical property names AND types** (`StockPageInfo`, `StockListRequestPayload`, `StockViewPayload`, `StockListReplyPayload`, `StockReplenishRequestLine/RequestPayload/ReplyPayload`, `CreditPageInfo`, `CreditListRequestPayload`, `CreditViewPayload`, `CreditListReplyPayload`, `InvoicePageInfo`, `InvoiceListRequestPayload`, `PaymentRegisterReplyPayload`); 2 identical by property set under another name (`InvoiceLinePayload`→`Contracts.Facts.InvoiceLine`, `GatewayRpcMoney`→`CreditMoney`); 1 differing in exactly one **type name** (`PaymentRegisterRequestPayload`: `GatewayRpcMoney Amount` vs `CreditMoney Amount`, the two being `(long Amount, string Currency)` both). The record's own line-by-line account of each is accurate, including its "defaults differ only" notes.
- **10 Gateway-only** — all ten confirmed absent from `src/Contracts/Rpc` by name.
- **2 field-different, kept and renamed** — `InvoiceViewPayload` 13 properties vs Contracts' 12, the extra being exactly `Lines`; `InvoiceListReplyPayload` identical property-for-property but transitively divergent through `Items`. Both renamed with the reason in-file (`:119-151`).

17 + 10 + 2 = **29**. ✔

**The three names in bullet 1 were treated as examples, not as the population.** Confirmed: `StockListRequestPayload` (identical → unified), `CreditListReplyPayload` (identical → unified) and `InvoiceViewPayload` (the single genuine field difference → kept) each appear as one row among 29, and the enumeration's own sentence says so. The anti-sample-bias instruction was honoured.

**Sibling Gateway payload files.** Independently checked by name intersection across **all** of `src/Gateway` (67 record declarations, 26 files) against `src/Contracts`: `comm -12` returns **empty** — no Gateway record anywhere now shares a name with a Contracts record, which is both the CS0104 story and the evidence that nothing payload-shaped was missed outside the one file.

---

## 3. Id 70's enumeration — commands re-run, exclusion verified

All four commands exclude `bin`/`obj` at the **`find` predicate** (`-not -path '*/bin/*' -not -path '*/obj/*'`), never by piping into `grep -v`, so none of them can drop a hit whose matched *content* mentions a build directory. Verified by reading the commands and re-running them.

| Command | Record | This review | Judgement |
|---|---|---|---|
| A — key-set assertion sites | 41 | **39** | benign drift, reconciled: the record's hits 36/37 (`SagaCommandPayloadTests.cs:226,227`, two `PropertyNamesOf` calls) were subsequently routed through `AssertRecordCarriesExactlyTheSchemasProperties` by the feature's own class-closure work. Every one of the 39 current hits maps to a classified line; **no unclassified hit exists** |
| B1 — `[InlineData(… new[] { "` | 19, in exactly 3 files | **0 now**; at HEAD 12 + 5 + 2 = **19** | the class is closed and the retirement is complete |
| B2 — `TheoryData<string, Type>` | 4 before → 7 after | **7** | matches |
| B3 — `handRetyped` | 12 | **0 now**; at HEAD 4 + 4 + 4 = **12** | matches |

**One population check the record did not make, run here.** Every `[MemberData(nameof(RequestAndReplySchemas))]` site in the solution: **14** = 7 tables × 2 theories, i.e. every payload-key theory in the repository now carries **both** the key-set and the schema-name assertion. No table carries one without the other, and no payload theory uses a different data source.

---

## 4. The schema-name guard (bullet 3)

Probed by substitution — arming 4 above — and it fails with a message naming both halves (the claimed schema **and** the record that cannot be it) plus the reason the key-set assertion cannot see it. The pair chosen is the right one: `StockCheckRequestPayload` and `StockReplenishRequestPayload` both declare exactly `{ companyCode, lines }`, so this transposition is invisible to every other assertion in the suite. Verified that the substitution resolves a real schema, so the failure is not the absence false negative.

**Advisory, not a defect.** `AssertTheRowsSchemaNameNamesTheRecordItClaims` derives agreement by suffix, permitting a qualifier prefix — deliberately, so `PageInfo`→`StockPageInfo` and `InvoiceListReplyPayload`→`GatewayInvoiceListReplyPayload` pass. A consequence is that the Gateway table's three `{ "PageInfo", typeof(…PageInfo) }` rows are mutually transposable undetected. That is not a defect: `PageInfo` is one schema and the three records are `(int Page, int PageSize, int Total)` identically, so a transposition changes no claim. Recorded so the next sweep does not re-raise it.

---

## 5. The two discrepancies the implementer reported — both confirmed, neither changes the verdict

**(a) `:151-156` is actually `:151-162`.** Confirmed from HEAD: twelve `InlineData` key-list rows at 151–162, `:162` being `DespatchCreateReplyPayload` — the row bullet 2 then arms against. The acceptance bullet's range was partial rather than merely stale, and the implementer retired all twelve. The correction is right and the work is a superset of what the bullet described.

**(b) Both `OrdersCancelReplyPayload` definitions were kept, so the stated ordering hazard never materialised.** Confirmed: `src/Contracts/Rpc` declares no counterpart for `orders.cancel`, so id 84's bullet 2 required keeping the Gateway's copy; the Gateway file still declares it at `:86` and Orders' Presentation copy stands. Doing 84 first was still correct (the two invoice renames would have invalidated an arming written first). The residual risk of the surviving duplication — that a mutation reaches only one definition — is what probe 5 above was run to close, and both sides are guarded.

---

## 6. `CHECKPOINTS.md` — the applicable boxes

- **C2** — [x] at most one `in_progress` (id 84; set `done` by this review, leaving none) · [x] every status in `rules.valid_status` · [x] every `done` feature has passing tests · [x] `feature_list.json` reflects the true state · [x] both entries' `RE-OPENED 2026-09-13` markers and their superseded `ACCEPTED, NOT FIXED` history left intact
- **C3** — [x] `Architecture.Tests` **36/36** run, not eyeballed (id 84 makes `src/Gateway/Application` reference `OrderToCash.Contracts.Rpc`, which is one of the three permitted shared projects; no `Domain/` namespace gained a reference) · [x] no shared runtime code beyond `SharedKernel`/`Contracts`/`Cqrs` · [x] money stays `long` minor units — `CreditMoney(long Amount, string Currency)` and `MoneyRepresentationHttpTests` green
- **C4** — [x] no Jest; xUnit throughout · [x] `Gateway.IntegrationTests` HTTP subset runs the real Kestrel pipeline · [ ] full `./quality.sh` **not run by this review** and not by the implementer (§6.5 of its record): `dotnet format --verify-no-changes` was clean there and the container suites for Billing, Fulfillment, Orders, Notifications, Projector and Seed were not re-run. Accepted for a change that adds only test theories and deletes duplicate type declarations, with `Gateway.IntegrationTests` (full, 61/61, real MS-SQL/Kafka/NATS/MongoDB) run by the implementer as the representative container suite
- **C5** — [x] effort record appended to `progress/history.md` · [x] no suspicious untracked files (the four new `AsyncApiSchema.PayloadRecords.cs` are the feature's own) · [x] no commit, no push
- **C6** — n/a, both entries are `sdd: false`
- **C7** — [x] `specs/shared/` untouched (verified by the leader; `git status` shows no `specs/shared/` entry) · [x] no `R<n>` claim moved · [x] effort records honest, including that this batch was two entries in one dispatch

---

## 7. Acceptance-bullet traceability

| Bullet | Evidence |
|---|---|
| 84.1 enumerate first, bin/obj by path, one classification line each | 29 records re-counted from HEAD and re-classified mechanically by this review; matches row for row (§2) |
| 84.2 identical unified, different not unified silently | 17 unified, 2 field-different kept with the reason in-file at `GatewayRpcPayloads.cs:23-50`, `:119-151` |
| 84.3 wire cannot move, run and named with counts | `Contracts.UnitTests` 24/24 (golden-envelope parity), `Billing.UnitTests` 262/262 (BC23), Gateway HTTP subset 15/15 — re-run here |
| 84.4 armed, both named tests fail naming the property | armings 1 and 2, both naming `amount`; restored, forced rebuild, both green |
| 84.5 deliberate separateness stated in the file, entry names the records | `GatewayInvoiceViewPayload`, `GatewayInvoiceListReplyPayload`, named in the file header and in §2.3 of the record |
| 70.1 three files derive the key set from the record | B1 = 0 `InlineData` key lists remain; all three call `AsyncApiSchema.AssertRecordCarriesExactlyTheSchemasProperties` |
| 70.2 armed, naming WHICH definition is mutated | arming 3 re-run here (Orders' `Presentation.Rpc` copy, named in the test parameters); arming B (`Contracts.Rpc.DespatchCreateReplyPayload`, single definition) accepted on the record's transcript, with probe 5 closing the two-definitions hazard independently |
| 70.3 schema name guarded by substitution | arming 4 re-run here, false negative excluded |
| 70.4 repository-wide enumeration as a search result | four commands re-run; path-excluding; 39/0/7/0 today, reconciled against 41/19/4/12; no unclassified hit; plus the 14-site `MemberData` population check |

---

## 8. Defects

**None blocking.** Two items are routed rather than narrated.

**R1 — the remaining `orders.create` / `orders.cancel` / `catalog.reference.list` duplication needs a numbered entry, and its #7 half is not what the record assumes.** The implementer correctly declined to move Orders' `Presentation/Rpc` records into `src/Contracts` (out of id 84's scope, another feature's design) and correctly asked for it to be filed rather than left in its own report. **It must be filed.** What the record does not say, and what makes the entry worth more than tidiness: **#7 has no duplication of these types at all, by construction.** Its payload types are **generated** from `asyncapi.yaml` into `packages/contracts/src/generated/` and imported by every app — one definition of `StockListRequestPayload`, used at `apps/fulfillment/src/application/ports/stock-read.port.ts:3` and `apps/gateway/src/application/queries/list-stock.query.ts:9` — with drift guarded by `packages/contracts/scripts/check.mjs` and its `check.spec.ts` (which corrupts a **copy** of the committed generated directory and asserts the check names the file and prints a diff). So the property *"one canonical payload type per schema, in agreement with the spec by construction"* was supplied in #7 by **codegen plus a drift check**; in #8 it is supplied by hand-transcribed records, and that is precisely why ids 84 and 70 exist. That sentence is a **ported-idiom ledger row** and the backlog entry should carry it, because #9 will face the same choice and should face it with the citation rather than re-derive it.

Neither entry owed a ledger under the port-bound rule (both are #8-native remediation, not ports), so its absence is not a defect here — but the row above is real, was found by looking, and is the kind of cargo the ledger exists to carry. **The leader files it; this review does not write backlog entries.**

**R2 — `./quality.sh` has not been run against this change by anyone.** Not blocking (see C4), but it is the one box on the checklist this batch leaves unticked, and it should be run before the phase's commit rather than as part of a later feature.

---

## 9. Effort record

Appended to `progress/history.md` as one entry per backlog id, with the shared dispatch noted.
