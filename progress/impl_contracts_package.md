# impl_contracts_package — feature 8

## What was built

`src/Contracts` now contains hand-written wire types for the shared messaging
contract, replacing the placeholder left by feature 6:

- **`Wire/JsonWire.cs`** — the ONE `JsonSerializerOptions` every service is
  meant to use: `JsonNamingPolicy.CamelCase`, `DefaultIgnoreCondition =
  WhenWritingNull`, no indentation, relaxed Unicode escaping, and the custom
  `InstantJsonConverter` registered. `Options` is the only public member —
  there is deliberately no second factory method that could quietly diverge
  from it.
- **`Wire/InstantJsonConverter.cs`** — formats `DateTimeOffset` as
  `yyyy-MM-ddTHH:mm:ss.fffZ` (three fraction digits, literal `Z`), which is
  the exact shape every golden envelope's `occurredAt` uses. The BCL's
  default round-trip format would instead emit seven fraction digits and a
  `+00:00` offset, which would fail byte-exactness on every golden file.
- **`Envelopes/Envelope.cs`** — `Envelope<TPayload>`, a single generic type
  realising `asyncapi.yaml`'s `components.schemas.Envelope` composed with
  each fact's payload (its `allOf: [Envelope, {eventType: const, payload:
  $ref}]` shape), rather than fourteen hand-duplicated envelope wrappers. The
  seven properties are positional record parameters in the spec's declared
  order (`eventId`, `eventType`, `aggregateId`, `correlationId`,
  `causationId`, `occurredAt`, `payload`), which is what makes
  `System.Text.Json`'s declaration-order serialisation byte-exact.
- **`Facts/*.cs`** — the six wire structures shared by more than one payload:
  `OrderLine`, `ReservationRef`, `Shortage`, `DespatchLine`, `InvoiceLine`,
  `CompensationStep`.
- **`Facts/Payloads/*.cs`** — **fourteen** payload records, one per fact
  schema in `asyncapi.yaml`'s `components.schemas` (`OrderPlacedPayload` …
  `OrderSagaFailedPayload`). See "Fact count: 14, not 13" below for why this
  is fourteen rather than the acceptance list's parenthetical "(13 of
  them)".
- **`Facts/FactCatalog.cs`** — `PayloadTypesByEventType`, a
  `Dictionary<string, Type>` mapping every `eventType` const to its payload
  CLR type. This is the registry the completeness test walks, and the single
  place a fifteenth fact would have to be added.
- Removed `src/Contracts/README_PLACEHOLDER.cs` / `ContractsPlaceholder` —
  confirmed via `grep` that nothing else in the repository referenced it.

`tests/Contracts.UnitTests` (new xUnit project, added to `OrderToCash.sln`
under the `tests` solution folder):

- **`RepositoryPaths.cs`** — locates the repo root by walking up from
  `AppContext.BaseDirectory` until `OrderToCash.sln` is found (duplicated
  from `tests/Architecture.Tests/RepositoryPaths.cs`; not shared via a
  project reference since that would be its own scope creep for four lines
  of logic).
- **`JsonEquivalence.cs`** — a recursive deep-equality assertion over two
  `JsonElement` trees that treats **object key order as immaterial** and
  **array element order as significant** — the exact distinction CLAUDE.md
  draws for payload parity.
- **`GoldenEnvelopeParityTests.cs`** — one `[Fact]` per golden file (12
  tests) plus one for `stock.rejected.v1` (no golden file exists). Each test
  deserialises the golden JSON into `Envelope<TPayload>` using
  `JsonWire.Options`, re-serialises it, and asserts three things against the
  golden bytes: (1) envelope field **set and order** — byte-exact; (2) the
  six scalar envelope fields' **raw JSON text** — byte-exact (this is what
  actually proves the GUID/Instant formatting matches #7's wire, not merely
  "some" ISO string); (3) the `payload` object — semantically equal via
  `JsonEquivalence` (key order unasserted). Doing all three via
  deserialise-then-reserialise (rather than hand-typing a second
  construction of each object from the golden data) also *is* the round-trip
  test (acceptance item 5) — it exercises the real
  deserialise/serialise path rather than a parallel, fallible transcription
  of the oracle.
- **`FactCatalogCompletenessTests.cs`** — two tests, both parsing
  `specs/shared/asyncapi.yaml` directly rather than hardcoding a list:
  - `EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType` —
    regex-extracts every `eventType: \n  const: <fact>` pair (the exact
    two-line shape the spec uses to pin one fact schema's `eventType`) and
    diffs that set against `FactCatalog.PayloadTypesByEventType.Keys`.
  - `EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields` — for each
    catalog entry, locates that payload's schema block in the YAML text by
    its exact 4-space-indented header (e.g. `    OrderPlacedPayload:`),
    extracts its `required:` field-name list, and asserts (via reflection)
    the CLR type has a property for each one.
- **`JsonWireOptionsTests.cs`** — direct unit tests of camelCase, null
  omission (both the omitted and the present case), absence of a `$type`
  discriminator, and that `JsonWire.Options` is a single shared instance.

## Fact count: 14, not 13

The brief's "Build exactly this" section says "(13 of them)", and the
acceptance list in `feature_list.json` doesn't state a count. I read
`specs/shared/asyncapi.yaml`'s `components.schemas` section directly rather
than trusting the parenthetical: it declares **fourteen** `*Event` schemas,
each pinning one `eventType` via `const:` — the twelve with golden files,
plus `stock.rejected.v1` (no golden — see below) and `order.saga_failed.v1`
(the spec's own comment names it "the 14th fact"). `grep -c` on the
`const: <fact>.v<n>` pattern returns 14. Since acceptance item 1 explicitly
requires "a test asserting every spec-declared fact type ... is represented
— parsing the spec, not a hardcoded list", parsing the spec is exactly what
determines the true count, and I built all fourteen so that test is
genuinely green rather than passing over a deliberately incomplete set.

## `stock.rejected.v1` — golden coverage absent

As stated in the brief: no golden file exists for this fact (the rare race
where reservation fails, so #7's retained Kafka topics held no captured
instance). `StockRejectedPayload` is implemented identically to the other
thirteen and wired into `FactCatalog`, and
`StockRejectedV1_HasNoGoldenFile_ButStillProducesTheCorrectEnvelopeShape`
proves the type still produces the correct envelope field order and still
omits an unset optional field — but this test has no golden oracle to check
against, so `stock.rejected.v1`'s **wire-byte parity with real #7 output
remains unproven**. Closing this gap requires a real captured instance,
which was not available at this feature's start.

## Deferred: OpenAPI REST DTOs

None of feature 8's five acceptance items requires a Gateway/REST DTO —
they are about the envelope, the fact payloads, and the shared
`JsonSerializerOptions`. `specs/shared/openapi.yaml` declares roughly 35
schemas (`LoginRequest`, `PlaceOrderRequest`, `OrderDetail`, `TimelineEntry`,
`StockItem`, `Invoice`, `Credit`, `Problem`, …), all of them REST
request/response bodies that have no live caller until the Gateway exists.
Building them now would be uncontrolled scope growth against an unverified
API surface. **Deferred to feature 25 (`gateway_rest_auth`)** and its
sibling gateway features (26, 40, 41), which are the first features with a
live consumer for them and whose acceptance criteria actually name
`openapi.yaml` conformance. `openapi.yaml`'s primitive schemas
(`UniqueId`, `Instant`, `CurrencyCode`, `MinorUnits`, `Money`, `Gln`,
`Quantity`, `PartyCode`, …) are identical to `asyncapi.yaml`'s and need no
separate type in Contracts — they map directly to `Guid`/`string`/`long` on
the payload records already built.

## Design decisions worth recording

- **Primitive types, not `SharedKernel` value objects, on the wire.**
  Payload fields use `Guid`, `string`, `long`, `int`, `DateTimeOffset`
  directly rather than `SharedKernel.Money`/`GLN`/`OrderNumber`/`UniqueId`.
  `Contracts` is permitted to reference `SharedKernel`, but doing so would
  require a `JsonConverter` per value object for no wire-shape benefit —
  the JSON shape of `Money.MinorUnits` is a bare `long` either way. Domain
  services construct/validate `SharedKernel` types from these primitives at
  their own boundary; `Contracts` stays a thin, dependency-light wire layer.
- **One `Envelope<TPayload>` generic type, not fourteen concrete envelope
  types.** `asyncapi.yaml` expresses each fact event as `Envelope` composed
  with a `const eventType` and a `$ref` payload — a closed generic
  instantiation (`Envelope<OrderPlacedPayload>`) is the direct C#
  realisation of that composition, and keeps the seven envelope fields (and
  their order) defined in exactly one place.
- **Deep-equality via `System.Text.Json.JsonElement`, not a hand-rolled
  JSON diff.** `JsonEquivalence` uses `JsonDocument`/`JsonElement` directly
  rather than pulling in a JSON-diff package — no new package dependency for
  a ~90-line recursive comparison the test suite fully exercises itself.
- **No YAML parser added.** The completeness tests parse `asyncapi.yaml`
  with two targeted regexes/text scans rather than adding a YAML package
  (e.g. YamlDotNet) to `Directory.Packages.props`. The two patterns matched
  (`eventType:\n  const: ...` and a schema's `required:` block) are stable,
  narrow, and verified directly against the file's actual indentation
  before being encoded as regex — a full YAML AST would be more general but
  is not needed for what these two tests check.

## Traceability — `specs/shared/test-matrix.md`

**Not touched.** `contracts_package` (feature 8) is scaffolding/plumbing and
owns no `R<n>` row — the matrix groups every row under one of eight
*other* features (`orders_aggregate`, `outbox_and_idempotency`,
`order_saga_orchestrator`, `fulfillment_stock`, `billing_credit`,
`billing_invoicing`, `projector_read_model`, `observability_reliability`).
In particular, R11 ("complete envelope on every fact ... refuses an
envelope with an absent, null or empty field") is `outbox_and_idempotency`'s
row (feature 14, still `pending`) and names a **domain-unit** test that
*refuses* a malformed envelope — that is validation behaviour belonging to
a domain aggregate/event-construction path, not this feature's plain wire
DTOs, which carry no such refusal logic. Leaving R11's cell at `TODO` here
is therefore correct, not an oversight; the file was already modified by
feature 7's prior session turn (R1–R4 rows), and I changed nothing further
in it.

## Arming table (forced-rebuild protocol)

For each row: introduce the violation → `dotnet build --no-incremental` →
run the named test → confirm FAIL (message recorded verbatim) → restore →
`touch` the restored file(s) → `dotnet build --no-incremental` → confirm
GREEN.

| Guard | Violation introduced | Named test | FAIL message (verbatim) | Restored + rebuilt |
|---|---|---|---|---|
| camelCase | `JsonWire.cs`: `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` → `null` | `JsonWireOptionsTests.PropertyNamesAreCamelCaseNotPascalCase`, `.EnvelopeFieldsAreCamelCaseNeverPascalCase` | `Assert.Contains() Failure: Sub-string not found` / `String: "{"OrderReference":"ORD-000001",...` / `Not found: ""orderReference""` (and the analogous `EventId`/`eventId` failure for the envelope test) | Yes — green, 2/2 passed |
| null-omission | `JsonWire.cs`: `DefaultIgnoreCondition = WhenWritingNull` → `Never` | `JsonWireOptionsTests.OptionalNullFieldIsOmittedNotWrittenAsNull` | `Assert.DoesNotContain() Failure: Sub-string found` / `String: ..."totalAmount":14975,"notes":null}"` / `Found: "notes"` | Yes — green, 1/1 passed |
| completeness (fact type) | `FactCatalog.cs`: removed the `["stock.rejected.v1"] = typeof(StockRejectedPayload)` entry | `FactCatalogCompletenessTests.EveryFactEventTypeDeclaredInAsyncApiYamlHasARepresentingPayloadType` | `asyncapi.yaml declares fact type(s) with no representing type in FactCatalog: stock.rejected.v1` | Yes — green, 1/1 passed |
| completeness (required field) | `OrderCompletedPayload.cs`: renamed `TotalAmount` → `TotalAmountRenamedForArmingProbe` (and the one named-argument call site in `JsonWireOptionsTests.cs` updated in lockstep so the assembly still compiles) | `FactCatalogCompletenessTests.EveryPayloadTypeHasAPropertyForEachOfItsSpecRequiredFields` | `OrderCompletedPayload (order.completed.v1) has no property for required field 'totalAmount' (expected 'TotalAmount')` | Yes — both files restored, green, 21/21 passed |

After every restore, `./quality.sh` was re-run in full (format check + build
+ test + coverage) and was green: format clean, build succeeded, 32 + 20 (21
after the required-field test was added) + 12 tests passed across
`SharedKernel.UnitTests`, `Contracts.UnitTests`, `Architecture.Tests`.
`Contracts` line coverage from `coverlet`: 95.8%.

## Files touched

- `src/Contracts/Contracts.csproj` — no change needed (already correctly
  shaped by feature 6; no `PackageReference` required — `System.Text.Json`
  ships in the shared framework).
- `src/Contracts/Wire/JsonWire.cs`, `InstantJsonConverter.cs` — new.
- `src/Contracts/Envelopes/Envelope.cs` — new.
- `src/Contracts/Facts/{OrderLine,ReservationRef,Shortage,DespatchLine,InvoiceLine,CompensationStep,FactCatalog}.cs` — new.
- `src/Contracts/Facts/Payloads/*.cs` (14 files) — new.
- `src/Contracts/README_PLACEHOLDER.cs` — deleted.
- `tests/Contracts.UnitTests/Contracts.UnitTests.csproj` — new.
- `tests/Contracts.UnitTests/{RepositoryPaths,JsonEquivalence,GoldenEnvelopeParityTests,FactCatalogCompletenessTests,JsonWireOptionsTests}.cs` — new.
- `OrderToCash.sln` — added `Contracts.UnitTests` under the `tests` solution folder.
- `feature_list.json` — feature 8 status `in_progress` → `in_review` (only field changed).
- `specs/shared/test-matrix.md` — **not modified by this feature** (see Traceability above).

## What I could not do, and why

- `stock.rejected.v1` has no golden-file wire-parity proof (see above) —
  not fixable without a captured #7 instance.
- OpenAPI REST DTOs are entirely deferred to the gateway features (see
  above) — none of this feature's acceptance criteria required them, and
  building ~35 untested, callerless DTOs against an API surface three
  gateway features away would be scope growth with no test that could prove
  they're right.

## Verification performed

- `./init.sh` — exit 0, before and after.
- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors.
- `./quality.sh` — format clean, build succeeded, all tests passed
  (32 SharedKernel + 21 Contracts + 12 Architecture), coverage reported
  (Contracts 95.8%).
- Arming table above — all four guards proven to fail on introduction and
  proven green after restore + forced rebuild.

## Anything that surprised me

- The BCL's default `DateTimeOffset` JSON round-trip format
  (`+00:00`, 7 fraction digits) does not match #7's wire bytes
  (`Z`, 3 fraction digits) at all — this would have silently broken
  envelope byte-exactness on every single golden file if the custom
  `InstantJsonConverter` hadn't been written and the golden files hadn't
  been read *before* writing the payload types.
- The MySQL-json-column key-order artifact CLAUDE.md documents is real and
  visible directly in the golden files — e.g. `credit_approved_v1.json`'s
  payload keys are ordered by length then alphabetically
  (`currency`, `creditCode`, `heldAmount`, `companyCode`, `retailerCode`,
  `orderReference`, `availableCreditAfter`), which is obviously not
  insertion order for any reasonable constructor. Seeing it directly made
  the "don't sort payload keys to imitate MySQL" instruction concrete rather
  than abstract.
