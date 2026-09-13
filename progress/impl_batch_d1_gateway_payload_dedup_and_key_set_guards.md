# impl — batch D1: backlog id 84 then id 70

One dispatch, two backlog entries, worked strictly in the order the brief set: **id 84** (`gateway_keeps_a_third_copy_of_the_now_canonical_rpc_payloads`) to completion first, then **id 70** (`retyped_key_list_guards_survive_in_three_more_payload_test_files`).

The contract worked from is `feature_list.json`'s own `acceptance` arrays for ids 84 and 70, read verbatim before anything was touched. Where the brief and a bullet appeared to differ, the bullet won; the two discrepancies found are recorded in §6.

`feature_list.json` was NOT touched. `specs/shared/` was NOT touched. No git command that writes the index or working tree was run. No commit, no push.

---

## 1. Baseline, before any change

Measured today, on this working tree, before the first edit. These are the numbers everything below reconciles against — the entries' own figures (`362/362`, `124/124`) predate feature 76 and are **not** what this tree contains, exactly as id 70's bullet 2 warned.

| Suite | Baseline |
|---|---|
| `Contracts.UnitTests` | 24 |
| `Gateway.UnitTests` | 211 |
| `Billing.UnitTests` | 242 |
| `Orders.UnitTests` | **471** (the entry says 362 — stale) |
| `Fulfillment.UnitTests` | **134** (the entry says 124 — stale) |
| `Architecture.Tests` | 36 |
| `Gateway.IntegrationTests`, Docker-free HTTP subset (`InvoicesHttpTests`, `MoneyRepresentationHttpTests`, `OrdersHttpTests`) | 15 |

`./init.sh` exited 0 before starting and after finishing.

---

## 2. Id 84 — the enumeration, as a search result

### 2.1 The population, enumerated by command

The unit the first bullet's claim is about is a **record declaration**, so records are what is enumerated and classified — not files.

```
find src/Gateway src/Contracts -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'record '
```

That returns 149 lines including prose mentions of the word, so the population was extracted mechanically instead: a script matching `^\s*(public|internal)?\s*(sealed\s+)?record\s+(\w+)` and gathering the positional parameter list across line breaks, then joining Gateway records to Contracts records by name and comparing parameter names, types and defaults. (Both scripts are session scratch, not committed — the table below is the output, and it is re-derivable by reading the two sides directly: `GatewayRpcPayloads.cs` is 160 lines and `src/Contracts/Rpc/` is four files totalling ~300.) It reports **140 record declarations** across `src/Gateway` and `src/Contracts`, of which **29 are in `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`** — consistent with the brief's `grep -c record` = 32 (29 declarations plus 3 prose mentions of "record" in that file's header).

**Sibling Gateway payload files.** The first bullet says "every record in `GatewayRpcPayloads.cs` **and every sibling Gateway payload file**". The enumeration covered **all of `src/Gateway`**, not only that directory, precisely so a payload-shaped record living elsewhere would appear rather than be excluded by a filename filter. The other 111 Gateway records are: `Presentation/Dto/*` (HTTP request/response DTOs — a different wire, `openapi.yaml`, never an RPC payload), `Application/Commands|Queries/*` (in-process command/query/result records), `Domain/Projection|Sse|Auth/*` (read-model and domain types), `Infrastructure/Health|Messaging/*` (`HealthResponseDto`, `CheckResultDto`, `RpcErrorPayload`). **`RpcErrorPayload` is the one that needed a decision**: it is RPC-shaped, and it is classified `Gateway-only` — `src/Contracts/Rpc` declares no counterpart, and `asyncapi.yaml`'s `RpcError` is the error envelope rather than a subject's request/reply payload.

### 2.2 The 29 records, one classification line each

Matched by NAME and by PROPERTY SET against `src/Contracts/Rpc` (property names, types, and C# default values compared mechanically by `scratchpad/compare.py`).

| # | Gateway record (line) | Classification | Action |
|---|---|---|---|
| 1 | `OrdersCreateRequestLine:25` | **Gateway-only** — no counterpart in `src/Contracts/Rpc` | kept |
| 2 | `OrdersCreateRequestPayload:27` | **Gateway-only** | kept |
| 3 | `OrdersCreateReplyPayload:36` | **Gateway-only** | kept |
| 4 | `OrdersCancelRequestPayload:48` | **Gateway-only** | kept |
| 5 | `OrdersCancelReplyPayload:50` | **Gateway-only** | kept |
| 6 | `CatalogReferenceListRequestPayload:67` | **Gateway-only** | kept |
| 7 | `ProductPayload:69` | **Gateway-only** | kept |
| 8 | `PartyPayload:71` | **Gateway-only** | kept |
| 9 | `CurrencyViewPayload:73` | **Gateway-only** | kept |
| 10 | `CatalogReferenceListReplyPayload:75` | **Gateway-only** | kept |
| 11 | `StockPageInfo:83` | **identical** to `Contracts/Rpc/StockRpcPayloads.cs:59` | unified |
| 12 | `StockListRequestPayload:85` | **identical property set and types**; Contracts' copy gives the last three parameters C# defaults (`= null`), which is a constructor convenience, not a wire property (`Contracts/Rpc/StockRpcPayloads.cs:62`) | unified |
| 13 | `StockViewPayload:87` | **identical** to `StockRpcPayloads.cs:70` | unified |
| 14 | `StockListReplyPayload:89` | **identical** to `StockRpcPayloads.cs:73` (and both its member types are unified, #11/#13) | unified |
| 15 | `StockReplenishRequestLine:93` | **identical** to `StockRpcPayloads.cs:78` | unified |
| 16 | `StockReplenishRequestPayload:95` | **identical** to `StockRpcPayloads.cs:81` | unified |
| 17 | `StockReplenishReplyPayload:97` | **identical** to `StockRpcPayloads.cs:84` | unified |
| 18 | `CreditPageInfo:101` | **identical** to `Contracts/Rpc/CreditRpcPayloads.cs:55` | unified |
| 19 | `CreditListRequestPayload:103` | **identical property set and types**; defaults differ only (`CreditRpcPayloads.cs:58`) | unified |
| 20 | `CreditViewPayload:105` | **identical** to `CreditRpcPayloads.cs:61` | unified |
| 21 | `CreditListReplyPayload:115` | **identical** to `CreditRpcPayloads.cs:72` | unified |
| 22 | `InvoicePageInfo:119` | **identical** to `Contracts/Rpc/InvoiceRpcPayloads.cs:43` | unified |
| 23 | `InvoiceListRequestPayload:121` | **identical property set and types**; defaults differ only (`InvoiceRpcPayloads.cs:46`) | unified |
| 24 | `InvoiceLinePayload:130` | **no counterpart in `Contracts/Rpc` BY NAME**; property set `(ProductCode, Units, UnitPrice)` is **identical** to `OrderToCash.Contracts.Facts.InvoiceLine` — the type `Contracts/Rpc/InvoiceRpcPayloads.cs:26` itself already reuses for the same `InvoiceLine` schema | unified onto `Contracts.Facts.InvoiceLine` |
| 25 | `InvoiceViewPayload:132` | **field-different** — 13 properties against the Contracts copy's 12. The extra is `Lines`, the optional `lines` that `openapi.yaml`'s `Invoice` schema declares and `asyncapi.yaml`'s `InvoiceView` does not (verified: `asyncapi.yaml:3007-3049` has no `lines`). **Deliberate**, ruled at review D8 of `gateway_rest_auth` | NOT unified — renamed `GatewayInvoiceViewPayload`, reason stated in the file |
| 26 | `InvoiceListReplyPayload:147` | **field-different by transitive reference** — its two property NAMES and TYPE NAMES match `InvoiceRpcPayloads.cs:77` exactly, but `Items`' element type resolves to the divergent view above. Unifying this wrapper alone would silently drop `lines` from the Gateway's deserialisation target | NOT unified — renamed `GatewayInvoiceListReplyPayload`, reason stated in the file |
| 27 | `GatewayRpcMoney:152` | **no counterpart BY NAME**; property set `(Amount, Currency)` **identical** to `Contracts/Rpc/CreditRpcPayloads.cs:23`'s `CreditMoney`, and both are `asyncapi.yaml`'s one `Money` schema | unified onto `CreditMoney` |
| 28 | `PaymentRegisterRequestPayload:154` | **field-different in ONE type NAME only** — `Amount` is `GatewayRpcMoney` here and `CreditMoney` at `InvoiceRpcPayloads.cs:89`; the two are structurally identical and produce the same wire, so this is a duplicate of the same contract, not a divergence | unified (with #27) |
| 29 | `PaymentRegisterReplyPayload:162` | **identical property set and types**; the last parameter's default differs only (`InvoiceRpcPayloads.cs:106`) | unified |

**Counts, reconciling to 29.** Unified and therefore **deleted** from the Gateway file: **17** — 10 exactly identical (#11, #13–#18, #20–#22), 4 identical in property names and types with only the C# default values differing (#12, #19, #23, #29), 2 identical by property set under a different type name (#24 `InvoiceLinePayload`, #27 `GatewayRpcMoney`), and 1 identical but for a member type that is itself one of those two (#28). **Kept**: **10** Gateway-only (#1–#10) plus **2** field-different and documented (#25, #26). 17 + 10 + 2 = 29. Verified after the change by command — `grep -cE '^public sealed record ' src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` returns **12**, and the twelve names it lists are exactly #1–#10 plus the two renamed ones.

The reviewer's three named examples were checked and are **not** the population: `StockListRequestPayload` (#12) and `CreditListReplyPayload` (#21) are identical and were unified; `InvoiceViewPayload` (#25) is the one genuine field difference, and it is the only one in 29.

### 2.3 What was changed

**Deleted from `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`** (17 record declarations), the Gateway now calling the one canonical copy: `StockPageInfo`, `StockListRequestPayload`, `StockViewPayload`, `StockListReplyPayload`, `StockReplenishRequestLine`, `StockReplenishRequestPayload`, `StockReplenishReplyPayload`, `CreditPageInfo`, `CreditListRequestPayload`, `CreditViewPayload`, `CreditListReplyPayload`, `InvoicePageInfo`, `InvoiceListRequestPayload`, `InvoiceLinePayload`, `GatewayRpcMoney`, `PaymentRegisterRequestPayload`, `PaymentRegisterReplyPayload`.

**Renamed** (bullet 5 — the separateness is now stated in the file itself, with its reason, and is visible at every use site): `InvoiceViewPayload` → `GatewayInvoiceViewPayload`, `InvoiceListReplyPayload` → `GatewayInvoiceListReplyPayload`.

The rename is not cosmetic. `src/Gateway` must now import `OrderToCash.Contracts.Rpc`, which declares types of both those names; a file needing both namespaces would not compile (CS0104), so the choice was an alias `using` per file or a name that says what the type is. The `Gateway` prefix follows the precedent the deleted `GatewayRpcMoney` already set, and it makes bullet 5's "so the next sweep does not re-raise it" true at the point of use rather than only in a header comment.

**Files touched for id 84**

- `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` — 17 records deleted, 2 renamed, header rewritten to record which group is kept and why.
- `src/Gateway/Application/Queries/ListStockQuery.cs`, `ListCreditsQuery.cs`, `ListInvoicesQuery.cs` — `using OrderToCash.Contracts.Rpc;`, renamed reply type.
- `src/Gateway/Application/Commands/ReplenishStockCommand.cs`, `RegisterPaymentCommand.cs` — same, plus `GatewayRpcMoney` → `CreditMoney`.
- `src/Gateway/Presentation/Endpoints/StockEndpoints.cs`, `CreditsEndpoints.cs`, `InvoicesEndpoints.cs` — same.
- `tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs`, `ReadOnlyQueryHandlerTests.cs`, `RegisterPaymentCommandHandlerTests.cs`, `tests/Gateway.IntegrationTests/InvoicesHttpTests.cs`, `MoneyRepresentationHttpTests.cs` — renamed types, added `using`.
- `tests/Billing.UnitTests/CreditRpcPayloadTests.cs` — two MISSING theory rows added, see §2.5.

### 2.4 Bullet 3 — the wire cannot move, run and named with counts

Run against the unified records, after the change:

| Test | Count | Result |
|---|---|---|
| `Contracts.UnitTests.GoldenEnvelopeParityTests` (byte-exact envelope parity against the twelve captured #7 envelopes) | 13 | pass |
| `Billing.UnitTests.CreditRpcPayloadTests` + `InvoiceRpcPayloadTests` (the `BC23`/`BI28` parsed-from-the-spec theories) | 41 | pass |
| `Gateway.IntegrationTests` HTTP-level payload tests (`InvoicesHttpTests`, `MoneyRepresentationHttpTests`, `OrdersHttpTests`) | 15 | pass |
| every suite listed in §1 | unchanged, see §5 | pass |

Immediately after the id 84 source change and before any test was added, **all seven suites returned exactly their baseline counts** (24 / 211 / 242 / 471 / 134 / 36 / 15) — the unification moved no test and changed no assertion.

### 2.5 A gap the arming exposed, closed here

`Billing.UnitTests.CreditRpcPayloadTests`'s `BC23` theory had **no row for `CreditView` and none for `PageInfo`** — the two nested records that actually carry the money onto the Gateway's `GET /credits` wire. The theory covered `billing.credit.list`'s envelope and neither of its contents. Found because the first attempt at bullet 4's arming mutated `CreditViewPayload` and the `BC23` theory passed 7/7. Two rows added (`{ "CreditView", typeof(CreditViewPayload) }`, `{ "PageInfo", typeof(CreditPageInfo) }`); both green on the unmutated record.

### 2.6 Bullet 4 — the arming

Mutation: **one unified record given an extra property.** `src/Contracts/Rpc/CreditRpcPayloads.cs`'s `CreditViewPayload` — the record the Gateway now shares with Billing instead of declaring its own copy of — gained `string Amount = "ARMING-ID84-NOT-MINOR-UNITS"`.

Protocol: `cp` backup → mutate → `dotnet build OrderToCash.sln --no-incremental` (Build succeeded) → run each named test → restore from the backup → `cmp` against the backup (byte-identical) → `touch` the restored file → `dotnet build --no-incremental` → confirming green run.

| Named test | Verbatim failure |
|---|---|
| `OrderToCash.Billing.UnitTests.CreditRpcPayloadTests.BC23_EveryCreditRequestAndReplyRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(schemaName: "CreditView", payloadType: typeof(OrderToCash.Contracts.Rpc.CreditViewPayload))` | `record OrderToCash.Contracts.Rpc.CreditViewPayload does not carry exactly the property names asyncapi.yaml's 'CreditView' schema declares. Declared by the schema but MISSING from the record: []. Declared by the record but UNDECLARED by the schema: [amount].` |
| `OrderToCash.Gateway.IntegrationTests.MoneyRepresentationHttpTests.EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode` | `GET /credits $.items[0].amount: R1 requires an integer count of minor units, got "ARMING-ID84-NOT-MINOR-UNITS" (kind String)` |
| `OrderToCash.Gateway.UnitTests.GatewayRpcPayloadTests.GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(schemaName: "CreditView", …)` (extra, not required by the bullet) | `record OrderToCash.Contracts.Rpc.CreditViewPayload does not carry exactly the property names asyncapi.yaml's 'CreditView' schema declares. … UNDECLARED by the schema: [amount].` |

Both required tests name the offending property, `amount`. The second one is an **HTTP-level** failure, over real Kestrel, which is what makes it evidence that the Gateway's live `GET /credits` path now runs through the Contracts record rather than a Gateway copy.

After restore and forced rebuild: `Billing.UnitTests` 262/262, `Gateway.UnitTests` 237/237, Gateway HTTP subset 15/15 — all green.

**One mutation was tried first and rejected**, recorded because it is itself evidence. Renaming `PaymentRegisterReplyPayload.Outcome` broke compilation in 13 places, one of them `src/Gateway/Presentation/Endpoints/InvoicesEndpoints.cs:41` — the Gateway's own endpoint reading `.Outcome` off the **Contracts** record. A compile error is a weaker arming than a failing named test, so the mutation was changed; the compile error is nonetheless a direct demonstration that the unification reaches production code.

---

## 3. Id 70 — the repository-wide enumeration, as a search result

Per bullet 4 and `CLAUDE.md`'s standing rule, the class was enumerated repository-wide **before any instance was fixed**. Four commands, all excluding `bin`/`obj` **by path** (`-not -path`), never by post-filtering output content. Complete outputs are in `scratchpad/enum70_*.txt`; every hit is classified below and no hit is unclassified.

### 3.1 Command A — every key-set assertion site in the solution's tests

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n -E 'AsyncApiSchema\.PropertyNamesOf|\.GetProperties\(\)|EnumerateObject\(\)\.Select' | sort
```

**41 hits.** Classification, one line per hit:

1. `Billing.IntegrationTests/CreditSimulatorTests.cs:174` — outbox-row key comparison between two facts. Not a payload-key theory; not in scope.
2. `Billing.IntegrationTests/CreditSimulatorTests.cs:175` — as above.
3. `Billing.IntegrationTests/InvoiceWireTests.cs:39` — serialised fact key set on a real outbox row. Not a theory; not in scope.
4. `Billing.UnitTests/AsyncApiSchema.PayloadRecords.cs:36` — **the new shared helper added by this feature** (reflection over the record).
5. `Billing.UnitTests/CreditClaimProjectionTests.cs:50` — EF Core `IEntityType.GetProperties()`, a database mapping, not a payload. Not in scope.
6. `Billing.UnitTests/CreditRpcPayloadTests.cs:106` — `G5` scratch-spec arming. Already reflection-based.
7. `Billing.UnitTests/CreditRpcPayloadTests.cs:107` — as above.
8. `Billing.UnitTests/CreditRpcPayloadTests.cs:64` — serialised-key `[Fact]` for one reply outcome. Complementary, not a contract theory.
9. `Billing.UnitTests/CreditRpcPayloadTests.cs:76` — as above.
10. `Billing.UnitTests/InvoiceRpcPayloadTests.cs:51` — serialised-key `[Fact]`. Complementary.
11. `Billing.UnitTests/InvoiceRpcPayloadTests.cs:57` — as above.
12. `Billing.UnitTests/InvoiceRpcPayloadTests.cs:71` — `G5` scratch-spec arming. Already reflection-based.
13. `Billing.UnitTests/InvoiceRpcPayloadTests.cs:72` — as above.
14. `Contracts.UnitTests/FactCatalogCompletenessTests.cs:74` — reflection over FACT payload records against the catalog. Already reflection-based; different artefact (facts, not RPC).
15. `Contracts.UnitTests/GoldenEnvelopeParityTests.cs:172` — envelope FIELD ORDER against the captured #7 bytes. Not a key-set theory.
16. `Fulfillment.UnitTests/AsyncApiSchema.PayloadRecords.cs:36` — **the new shared helper**.
17. `Fulfillment.UnitTests/StockClaimProjectionTests.cs:50` — EF Core mapping. Not in scope.
18. `Fulfillment.UnitTests/StockRpcPayloadTests.cs:165` — **DEFECT INSTANCE 3**: the `BC23` theory comparing the spec against a hand-typed `InlineData` list. Fixed.
19. `Fulfillment.UnitTests/StockRpcPayloadTests.cs:192` — `G5` arming, previously against a hand-retyped literal. Fixed (now reads the record).
20. `Fulfillment.UnitTests/StockRpcPayloadTests.cs:216` — serialised-key `AssertKeys` helper. Complementary; kept.
21. `Gateway.UnitTests/AsyncApiSchema.PayloadRecords.cs:36` — **the new shared helper**.
22. `Gateway.UnitTests/GatewayRpcPayloadTests.cs:131` — `G5` arming. Already reflection-based.
23. `Gateway.UnitTests/GatewayRpcPayloadTests.cs:132` — as above.
24. `Gateway.UnitTests/GatewayRpcPayloadTests.cs:97` — the documented `InvoiceView`-plus-`lines` subset case. Already reflection-based.
25. `Gateway.UnitTests/GatewayRpcPayloadTests.cs:98` — as above.
26. `Orders.IntegrationTests/OutboxWireParityTests.cs:180` — envelope field order on a real outbox row. Not a key-set theory.
27. `Orders.UnitTests/AsyncApiSchema.PayloadRecords.cs:36` — **the new shared helper**.
28. `Orders.UnitTests/CatalogReferenceListPayloadTests.cs:103` — **DEFECT INSTANCE 2**: `BC23` theory against a hand-typed list. Fixed.
29. `Orders.UnitTests/CatalogReferenceListPayloadTests.cs:124` — `G5` arming against a hand-retyped literal. Fixed.
30. `Orders.UnitTests/CatalogReferenceListPayloadTests.cs:146` — serialised-key helper. Complementary; kept.
31. `Orders.UnitTests/OrdersCancelPayloadTests.cs:113` — serialised-key helper. Complementary; kept.
32. `Orders.UnitTests/OrdersCancelPayloadTests.cs:68` — **DEFECT INSTANCE 1**: `BC23` theory against a hand-typed list. Fixed.
33. `Orders.UnitTests/OrdersCancelPayloadTests.cs:91` — `G5` arming against a hand-retyped literal. Fixed.
34. `Orders.UnitTests/OutboxClaimProjectionTests.cs:30` — EF Core mapping. Not in scope.
35. `Orders.UnitTests/SagaCommandPayloadTests.cs:188` — a doc-comment `<see cref>`. Prose, no assertion.
36. `Orders.UnitTests/SagaCommandPayloadTests.cs:226` — `BC23` theory, **already reflection-based** (fixed by backlog id 64). Schema-name theory added here.
37. `Orders.UnitTests/SagaCommandPayloadTests.cs:227` — as above.
38. `Orders.UnitTests/SagaCommandPayloadTests.cs:257` — `G5` arming. Already reflection-based.
39. `Orders.UnitTests/SagaCommandPayloadTests.cs:258` — as above.
40. `Orders.UnitTests/SagaCommandPayloadTests.cs:294` — serialised-key helper. Complementary; kept.
41. `Projector.UnitTests/FactProjectionTests.cs:89` — reflection over `ProjectionDelta`, a read-model type, not an RPC payload. Not in scope.

### 3.2 Command B1 — every hand-typed key list living in an `InlineData` row

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n -E '^\s*\[InlineData\(.*new\[\] \{ "' | sort
```

**19 hits, in exactly three files, and no others** — which is the finding: the class is closed at three instances, matching the entry, and nothing else in the solution carries the shape.

- `Fulfillment.UnitTests/StockRpcPayloadTests.cs:151-162` — **12 rows**, all retired. The brief's fact is confirmed and the acceptance bullet's `:151-156` is a **partial** range: `:162` is the `DespatchCreateReplyPayload` row, the one id 70's own bullet 2 arms against.
- `Orders.UnitTests/CatalogReferenceListPayloadTests.cs:96-100` — **5 rows**, all retired.
- `Orders.UnitTests/OrdersCancelPayloadTests.cs:64-65` — **2 rows**, all retired.

### 3.3 Command B2 — every `TheoryData<string, Type>` table (the reflection-based shape)

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'TheoryData<string, Type>' | sort
```

**4 hits before the change**, all already reflection-based and all now additionally carrying the schema-name theory:

- `Billing.UnitTests/CreditRpcPayloadTests.cs:16` — 7 rows before, **9 after** (§2.5).
- `Billing.UnitTests/InvoiceRpcPayloadTests.cs:17` — 9 rows.
- `Gateway.UnitTests/GatewayRpcPayloadTests.cs:34` — 26 rows.
- `Orders.UnitTests/SagaCommandPayloadTests.cs:191` — 13 rows.

After the change there are **7**: the three converted files now declare one too.

### 3.4 Command B3 — the `handRetyped` locals inside the `G5` armings

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'handRetyped' | sort
```

**12 hits, 3 of them literal key lists** — `StockRpcPayloadTests.cs:193`, `CatalogReferenceListPayloadTests.cs:125`, `OrdersCancelPayloadTests.cs:92`, exactly the additional sites the brief flagged (the bullet's `:66` and `:101` are stale; the current lines are `:92` and `:125`). All three now read the record via `AsyncApiSchema.PropertyNamesOfRecord`, and each gained a positive `DoesNotContain`/`Contains` pair naming the renamed key, so the `G5` arming asserts the direction of the difference rather than only its existence. The other 9 hits are the theory parameter name and `Assert.NotEqual` call sites in the same three methods, all rewritten.

### 3.5 What was changed for id 70

**New, one per test project that owns a payload theory** (the same per-project duplication `AsyncApiSchema.cs` itself already uses, since these are four separate assemblies), each a `partial` extension of the existing `AsyncApiSchema` class:

- `tests/Billing.UnitTests/AsyncApiSchema.PayloadRecords.cs`
- `tests/Gateway.UnitTests/AsyncApiSchema.PayloadRecords.cs`
- `tests/Orders.UnitTests/AsyncApiSchema.PayloadRecords.cs`
- `tests/Fulfillment.UnitTests/AsyncApiSchema.PayloadRecords.cs`

Each declares three members:

- `PropertyNamesOfRecord(Type)` — the record's own property names, camelCased, by reflection.
- `AssertRecordCarriesExactlyTheSchemasProperties(schemaName, payloadType)` — set EQUALITY between the spec-parsed set and the record's, failing with a message that **names both differences**. xUnit's own `Assert.Equal` on two `HashSet<string>`s truncates the sets with an ellipsis and therefore cannot name the offending property; that was measured, not assumed — the first arming attempt produced `Expected: ["creditCode", …] Actual: ["creditCode", …]` and named nothing.
- `AssertTheRowsSchemaNameNamesTheRecordItClaims(schemaName, payloadType)` — bullet 3's guard, deriving the agreement from the two names (the record's simple name ends with the schema name, optionally plus a `Payload` suffix) rather than from a second hand-typed mapping, which would reintroduce exactly what this entry retires.

**Converted from hand-typed lists to record reflection** (bullet 1), each gaining a `TheoryData<string, Type>` table naming the record unambiguously (bullet 2):

- `tests/Orders.UnitTests/OrdersCancelPayloadTests.cs` → `BC23_EveryOrdersCancelRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped` + `BC23_EveryRowsSchemaNameNamesTheRecordThatRowClaims`. **Which definition**: `OrderToCash.Orders.Presentation.Rpc` — Orders answers `orders.cancel`, so its Presentation copy shapes the wire. The Gateway's caller-side `OrdersCancelReplyPayload` is a genuinely second definition (id 84 kept it: `Contracts/Rpc` has no counterpart for this subject) and is covered by its own row in `GatewayRpcPayloadTests`.
- `tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs` → same two theories, same namespace, same two-definitions note.
- `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs` → same two theories. **Which definition**: `OrderToCash.Contracts.Rpc`, and for these twelve records there is exactly **one** definition — feature 76 unified them. Confirmed by command, not assumed: `DespatchCreateReplyPayload` has a single declaration, `src/Contracts/Rpc/DespatchRpcPayloads.cs:18`.

**Additionally given the schema-name theory** (closing the class rather than the three instances it was noticed in, per `CLAUDE.md`): `Billing.UnitTests/CreditRpcPayloadTests.cs`, `Billing.UnitTests/InvoiceRpcPayloadTests.cs`, `Gateway.UnitTests/GatewayRpcPayloadTests.cs`, `Orders.UnitTests/SagaCommandPayloadTests.cs`. All four also now route their key-set assertion through the naming helper.

### 3.6 The armings

Protocol for each: `cp` backup → mutate → `dotnet build OrderToCash.sln --no-incremental` → run the named test(s) → record verbatim → restore from the backup → `cmp` (byte-identical every time) → `touch` → forced rebuild → confirming green run. No `git checkout`, no `git stash`, no git command that writes the tree.

**Arming A — bullet 2's first measured mutation.** `src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs`'s `OrdersCancelReplyPayload` given `string? UndeclaredArmingProperty = null`. **This names which of the two definitions was mutated: Orders' Presentation copy, not the Gateway's.**

> `OrderToCash.Orders.UnitTests.OrdersCancelPayloadTests.BC23_EveryOrdersCancelRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped(schemaName: "OrdersCancelReplyPayload", payloadType: typeof(OrderToCash.Orders.Presentation.Rpc.OrdersCancelReplyPayload)) [FAIL]`
> `record OrderToCash.Orders.Presentation.Rpc.OrdersCancelReplyPayload does not carry exactly the property names asyncapi.yaml's 'OrdersCancelReplyPayload' schema declares. Declared by the schema but MISSING from the record: []. Declared by the record but UNDECLARED by the schema: [undeclaredArmingProperty].`

Whole-project result under the mutation: **`Failed: 1, Passed: 490, Total: 491`**. That single number is the entry's claim re-measured on today's ground: the **471 pre-existing tests all passed**, so before this change `Orders.UnitTests` would have been 471/471 green with an undeclared property on a reply record. The entry said 362/362; the defect is the same, the figure has moved.

**Arming B — bullet 2's second measured mutation.** `src/Contracts/Rpc/DespatchRpcPayloads.cs`'s `DespatchCreateReplyPayload` given `string? UndeclaredArmingProperty = null`. **Which definition: the only one — there is no Gateway or Fulfillment copy left after feature 76.**

> `OrderToCash.Fulfillment.UnitTests.StockRpcPayloadTests.BC23_EveryStockAndDespatchRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ReadFromTheRecordNeverRetyped(schemaName: "DespatchCreateReplyPayload", payloadType: typeof(OrderToCash.Contracts.Rpc.DespatchCreateReplyPayload)) [FAIL]`
> `record OrderToCash.Contracts.Rpc.DespatchCreateReplyPayload does not carry exactly the property names asyncapi.yaml's 'DespatchCreateReplyPayload' schema declares. Declared by the schema but MISSING from the record: []. Declared by the record but UNDECLARED by the schema: [undeclaredArmingProperty].`

Whole-project result: **`Failed: 1, Passed: 145, Total: 146`** — the 134 pre-existing tests all passed, re-measuring the entry's 124/124. Run against `Orders.UnitTests` under the same mutation, the caller side catches it too: `SagaCommandPayloadTests.BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(schemaName: "DespatchCreateReplyPayload", …) [FAIL]`, `Failed: 1, Passed: 490`.

**Arming C — bullet 3, the schema NAME substituted for a real sibling.** In `StockRpcPayloadTests`'s table, `{ "StockCheckRequestPayload", typeof(StockCheckRequestPayload) }` → `{ "StockReplenishRequestPayload", typeof(StockCheckRequestPayload) }`. Those two schemas both declare exactly `{ companyCode, lines }`, which is why this case exists.

- Key-set theory under the substitution: **`Passed: 12, Total: 12`** — it cannot see the swap, which is the measured justification for bullet 3 rather than an argument for it.
- Schema-name theory: **FAIL**, naming both halves —
  > `this theory row claims asyncapi.yaml schema 'StockReplenishRequestPayload' for record OrderToCash.Contracts.Rpc.StockCheckRequestPayload, but 'StockCheckRequestPayload' does not name that schema (expected the record's name to end with 'StockReplenishRequestPayload', optionally prefixed with a service or subject qualifier). A row whose schema name is substituted for a real sibling's is invisible to the key-set assertion whenever the two schemas declare the same keys.`

**Arming D — the same claim probed the other way round, and the MISSING half.** A theory row has two halves, so substituting the *record* is as much a defect as substituting the *name*: `{ "StockReserveRequestPayload", typeof(StockReserveRequestPayload) }` → `{ "StockReserveRequestPayload", typeof(StockCheckRequestPayload) }`. Both theories fail:

> `record OrderToCash.Contracts.Rpc.StockCheckRequestPayload does not carry exactly the property names asyncapi.yaml's 'StockReserveRequestPayload' schema declares. Declared by the schema but MISSING from the record: [orderReference, retailerCode]. Declared by the record but UNDECLARED by the schema: [].`

> `this theory row claims asyncapi.yaml schema 'StockReserveRequestPayload' for record OrderToCash.Contracts.Rpc.StockCheckRequestPayload, but 'StockCheckRequestPayload' does not name that schema …`

`Failed: 2, Passed: 22, Total: 24`. This is the only arming that exercises the **MISSING from the record** branch of the message — i.e. defeat-list attack #7, a declared element dropped entirely — so that branch has now been seen to fail rather than merely being present in the source.

---

## 4. Defeat list — run against my own guards, before submitting

The two guards under attack are `AssertRecordCarriesExactlyTheSchemasProperties` (G1) and `AssertTheRowsSchemaNameNamesTheRecordItClaims` (G2).

| # | Attack | Ran? | Result / why it cannot bite |
|---|---|---|---|
| 1 | Delete the behaviour | **ran** | Arming D removes two declared properties from what the row asserts about (by pointing it at a smaller sibling record) and G1 fails naming both. The class-level version — deleting the theory — is visible as a suite-count drop, reconciled in §5. |
| 2 | Corrupt a payload field the test supplied | **ran** | Armings A and B corrupt the record's property SET, which is the field G1's claim is about, and G1 fails naming the offending key. Neither side of G1's comparison is supplied by the test: `expected` is parsed from `specs/shared/asyncapi.yaml` on disk, `actual` is read off the compiled type. |
| 3 | Substitute a valid sibling identifier | **ran, both directions** | Arming C substitutes the schema NAME for a real sibling; arming D substitutes the record TYPE for a real sibling. Both fail. The false-negative this family carries — a substitution that fails for the wrong reason — cannot occur silently here: an unknown schema name throws `could not locate the '<name>:' schema block in specs/shared/asyncapi.yaml`, which names what was wrong, so a swap that fails for "no such schema" is distinguishable from one that fails for "wrong schema". |
| 4 | Shadow the pattern from a comment or string literal | **skipped — cannot bite** | Neither guard reads source text. G1 reflects over a compiled `Type` and parses the spec YAML; G2 compares two strings, one of which is `Type.Name` from metadata. Text that merely *looks* like a record declaration produces no type. |
| 5 | Hide the real thing in a dead region (`#if false`) | **skipped — cannot bite** | Same reason: the guards see the compiled assembly, so they see exactly what the compiler emitted, not what the file appears to contain. A record inside `#if false` does not exist as a type and its row would fail to compile. |
| 6 | Hide it in a raw or verbatim string | **skipped — cannot bite** | Same reason as #4/#5. |
| 7 | Drop an OPTIONAL element entirely | **ran** | The assertion is set EQUALITY, never a subset or a presence check, and arming D exercised the `MISSING from the record` branch explicitly. The one deliberate subset assertion in the repository — `GatewayRpcPayloadTests.GatewayInvoiceViewPayload_CarriesEveryAsyncApiPropertyPlusTheDocumentedOptionalLinesExtra` — pins the extra to exactly `{ "lines" }` with a second assertion, so it is not a presence-only check either. |
| 8 | Compare a literal to a literal | **ran** | This is the defect id 70 exists to close, and it is closed by construction: neither side of G1 is a literal in the test. Proven by armings A and B, where mutating **only the record** — with no test file touched — flips the result from green to red, which a literal-to-literal comparison could not do. |
| 9 | Satisfy the closer half and leave the premise half stale | **ran, and it bit** | Every completeness claim in this record is a command plus its full output plus one classification line per hit (§2.1, §3.1–§3.4). The premise sweep is `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \| xargs -0 grep -in 'hand-retype\|handRetyped\|hand retypes'`, enumerated on the **retired** wording rather than the new one — 15 hits. See §4.1: the first draft of this row asserted the sweep returned nothing, which was false, and running it found **two genuinely stale sentences** that the three file-level rewrites had missed. |
| 10 | Let a build-output copy join the population | **ran** | Every enumerating command excludes `bin`/`obj` at the `find` predicate (`-not -path '*/bin/*' -not -path '*/obj/*'`), never by piping into `grep -v`, which would filter on the matched line's content. |

### 4.1 The retired-wording sweep, and the two stale sentences it caught

`CLAUDE.md`'s rule is to enumerate on the wording of the claim being **retired**, not the one being written. Doing so returned 15 hits, classified:

- `Billing|Gateway|Orders|Fulfillment.UnitTests/AsyncApiSchema.PayloadRecords.cs:12` (×4) — the new helper's own header, narrating what id 64 and id 70 retired. Accurate history.
- `Orders.UnitTests/SagaCommandPayloadTests.cs:210`, `:251` — narrating what id 64 retired in that file. Accurate history.
- `Orders.UnitTests/OrdersCancelPayloadTests.cs:18`, `:85`; `CatalogReferenceListPayloadTests.cs:19`, `:119`; `Fulfillment.UnitTests/StockRpcPayloadTests.cs:23`, `:182` (×6) — the rewritten comments of the three converted files, narrating the conversion. Accurate history.
- **`Fulfillment.UnitTests/StockRpcPayloadTests.cs:219` — STALE.** The `G5` doc comment still said "this file's hand-retyped list for that schema no longer agrees with the scratch copy" after that list had been replaced by a reflection read. **Fixed.**
- **`Gateway.UnitTests/GatewayRpcPayloadTests.cs:15` — STALE.** Said the reflection shape was "chosen over `StockRpcPayloadTests`'s hand-retyped-list shape", which stopped being true the moment id 70 converted that file. **Fixed.**
- `Orders.UnitTests/OrdersCreateErrorMapperTests.cs:113` — a near neighbour, correctly outside the class: it is about the twelve-value `RpcError.code` **enum**, not a payload key set, it already reads the production copy by reflection, and its failure messages already name which of the three copies drifted. No change.

The two stale sentences are worth recording rather than quietly fixing: both were in files this feature had already edited, and both survived a rewrite of the surrounding comment. A per-file rewrite is not a sweep.

**One shape not on the list, added here because it defeated the first attempt at this work.** *A guard that fails but cannot say what broke.* xUnit's `Assert.Equal` over two `HashSet<string>`s truncates both sets with `···`, so the original theory would have failed under the arming while naming nothing — and id 84's bullet 4 requires the message to name the offending property. The instrument change (a hand-written set comparison with an explicit missing/undeclared split) was made for that reason, and per `CLAUDE.md`'s instrument-change rule its new premises were enumerated and armed: it assumes (a) `GetProperties()` returns exactly the record's public positional properties — `EqualityContract` is protected and therefore excluded, confirmed by 78 green rows across seven theories; and (b) an unknown schema throws rather than returning empty, which is what stops a substitution from passing vacuously.

---

## 5. Suite counts, before and after, reconciled

| Suite | Before | After | Δ | Reconciliation |
|---|---|---|---|---|
| `Contracts.UnitTests` | 24 | 24 | 0 | untouched |
| `Gateway.UnitTests` | 211 | 237 | **+26** | one new theory, `GatewayPayload_EveryRowsSchemaNameNamesTheRecordThatRowClaims`, over the existing 26-row table |
| `Billing.UnitTests` | 242 | 262 | **+20** | +2 rows added to `CreditRpcPayloadTests`' key-set theory (`CreditView`, `PageInfo` — §2.5); +9 and +9 rows for the new schema-name theories in `CreditRpcPayloadTests` (9 rows) and `InvoiceRpcPayloadTests` (9 rows) |
| `Orders.UnitTests` | 471 | 491 | **+20** | new schema-name theories: `OrdersCancelPayloadTests` 2 rows, `CatalogReferenceListPayloadTests` 5 rows, `SagaCommandPayloadTests` 13 rows. The converted key-set theories keep their row counts (2, 5), so the conversion itself adds nothing |
| `Fulfillment.UnitTests` | 134 | 146 | **+12** | new schema-name theory over `StockRpcPayloadTests`' 12 rows; the converted key-set theory keeps its 12 |
| `Architecture.Tests` | 36 | 36 | 0 | untouched |
| `Gateway.IntegrationTests`, HTTP subset | 15 | 15 | 0 | renames only |

**Total new tests: 78** (26 + 20 + 20 + 12). No test was deleted; the three conversions replaced 19 `InlineData` rows with 19 `TheoryData` rows one-for-one.

### The final confirming run

One forced rebuild (`dotnet build OrderToCash.sln --no-incremental`, exit 0), then every suite with `--no-build`:

| Suite | Result |
|---|---|
| `Contracts.UnitTests` | 24 / 24 pass |
| `Gateway.UnitTests` | 237 / 237 pass |
| `Billing.UnitTests` | 262 / 262 pass |
| `Orders.UnitTests` | 491 / 491 pass |
| `Fulfillment.UnitTests` | 146 / 146 pass |
| `Notifications.UnitTests` | 111 / 111 pass |
| `Projector.UnitTests` | 120 / 120 pass |
| `Cqrs.UnitTests` | 23 / 23 pass |
| `SharedKernel.UnitTests` | 50 / 50 pass |
| `Seed.UnitTests` | 44 / 44 pass |
| `Architecture.Tests` | 36 / 36 pass |
| **`Gateway.IntegrationTests`, FULL (Docker: real MS-SQL, Kafka, NATS, MongoDB; boots the real Fulfillment, Orders and Projector hosts)** | **61 / 61 pass**, 6 m 8 s |

**1605 tests, 0 failures, 0 skipped**, counted by adding the twelve figures above, each read off the run in this session: 24 + 237 + 262 + 491 + 146 + 111 + 120 + 23 + 50 + 44 + 36 + 61 = 1605.

`dotnet format OrderToCash.sln --verify-no-changes` is clean. Two arming-residue sweeps return no hits:

```
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'ARMING-ID84\|UndeclaredArmingProperty'      # no output
```

`./init.sh` exits 0.

---

## 6. Discrepancies between the brief and the acceptance bullets, and things I could not do

1. **Id 70 bullet 1's line range for `StockRpcPayloadTests` is partial, not merely stale.** The bullet says the `InlineData` rows are "at `:151-156`". They are at **`:151-162`**, twelve rows, and `:162` is the `DespatchCreateReplyPayload` row that bullet 2 then arms against. The brief flagged this and it is confirmed by command (§3.2). The bullet's other two line corrections (`:92`, `:125`) are correct as given.
2. **The brief's ordering hazard did not materialise, for a reason worth recording.** The brief warned that id 70's arming might target an `OrdersCancelReplyPayload` definition that id 84 deletes. Id 84 **kept** both definitions: the Gateway's copy has no counterpart in `src/Contracts/Rpc`, so bullet 2 of id 84 required keeping it. Doing 84 first was still right — the Gateway's `InvoiceListReplyPayload`/`InvoiceViewPayload` renames would have invalidated an arming written against the old names — but the specific hazard named was not the live one.
3. **Both entries' `feature_list.json` notes still open with the `RE-OPENED 2026-09-13 by maintainer ruling` marker and retain the superseded `ACCEPTED, NOT FIXED` prose.** Anything read elsewhere implying these entries are closed is that stale text. `feature_list.json` was not edited by me; id 84's status was already `in_progress` when I started and id 70's was `pending`. The coordinator owns both transitions.
4. **Scope I took beyond the literal three files, and why.** Id 70 bullet 4 requires a repository-wide enumeration of the class, and `CLAUDE.md` requires the class to be closed rather than the instances it was noticed in. The enumeration found four further payload theories (§3.3) that were already reflection-based for the key set but had **no** guard on the schema name a row claims — the defect bullet 3 names. All four received the same schema-name theory. `Billing.UnitTests/CreditRpcPayloadTests`'s two missing rows (§2.5) were added for the same reason: id 84's own arming could not otherwise fail a `BC23`-named test, because the record it mutates had no row.
5. **Not done: a full `./quality.sh`.** `dotnet format --verify-no-changes` is clean and the whole solution builds `--no-incremental` with zero errors; the suites listed in §5 were run. The container-backed integration suites for Billing, Fulfillment, Orders, Notifications, Projector and Seed were **not** re-run — nothing in this change touches their subjects, and `Gateway.IntegrationTests` (which boots the real Fulfillment, Orders and Projector hosts against real MS-SQL, Kafka, NATS and MongoDB) was run in full as the representative container suite. If the reviewer wants the rest, they are a `./quality.sh` away and nothing here predicts a movement in them.
6. **Not done, and deliberately: the Orders `orders.create`/`orders.cancel`/`catalog.reference.list` payloads were NOT moved into `src/Contracts/Rpc`.** They are genuinely duplicated between `src/Orders/Presentation/Rpc/` and the Gateway, and unifying them is the same argument feature 76 made. It is out of id 84's scope — its first bullet matches against `src/Contracts/Rpc`, which declares no counterpart for these subjects — and it would move Presentation types of another service into the shared contract, which is a design decision for a spec'd feature rather than a backlog fix. Both definitions of each are now guarded by a reflection-based theory (Orders' by this feature, the Gateway's by `GatewayRpcPayloadTests`), so the duplication can no longer drift silently. **Recommend the coordinator file this as its own backlog entry rather than leave it in this record** — a sentence in an implementer's report names nobody.
