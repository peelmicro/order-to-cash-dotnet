# impl: application_layer_depends_on_infrastructure_unguarded (backlog id 76)

## Status header

- **Where we are:** feature complete. `./quality.sh` green (1878/1878, see
  reconciliation below), `./init.sh` exits 0, the new architecture guard is
  armed in two services and its failure message was read verbatim before
  being restored.
- **Needs approval:** the reviewer's normal round. One design decision
  beyond the brief's literal ask is called out below (RPC payload
  unification) because it changes more files than a pure "move" would have
  — recommended, not assumed.
- **Recommendation:** accept. The population is closed exactly (26 real
  files, all now behind either `src/Contracts` or a port), the new
  NetArchTest rule is armed in Billing and Fulfillment and correctly stays
  silent on the three doc-comment-only hits, and a real, non-obvious
  limitation of the guard mechanism itself is disclosed rather than papered
  over (see "What the guard cannot see").

---

## 1. Enumeration — a search result, not a reading

Command (mirrors the leader's own, run fresh in this session):

```
find src -type d -name Application -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 -I{} find {} -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -lE "OrderToCash\.[A-Za-z]*\.Infrastructure" 2>/dev/null | sort
```

**Output: 27 files.** Billing 10, Fulfillment 10, Notifications 1, Orders 6
(`CancelOrderCommandHandler.cs`, `OperatorCancelRequestedEnvelope.cs`,
`IHealthCheck.cs`, `IIdempotentSagaRunner.cs`, `ISagaCommands.cs`,
`SagaCommandRequestFactory.cs`). This reconciles exactly with the leader's
brief: the brief's own list, counted item by item, is also 6 Orders files,
not 5 — its "(5)" label was a typo for the population it correctly listed.
10 + 10 + 6 + 1 = 27, matching both readings.

### Classification, one line per hit

Per-file `grep -n "OrderToCash\.[A-Za-z]*\.Infrastructure"` and manual read
of each hit line, excluding lines under `///` doc comments from the
"real dependency" count (a doc comment compiles to nothing NetArchTest, or
any IL-based tool, can see — `GenerateDocumentationFile` is off in this
repo per backlog id 78's own finding):

| File | Hit shape | Classification |
|---|---|---|
| `Billing/Application/Commands/HoldCreditCommand.cs` | `using` | real |
| `Billing/Application/Commands/ReleaseCreditCommand.cs` | `using` | real |
| `Billing/Application/CreditHoldService.cs` | `using` | real |
| `Billing/Application/CreditReleaseService.cs` | `using` | real |
| `Billing/Application/InvoiceIssueService.cs` | `using` | real |
| `Billing/Application/PaymentRegisterService.cs` | `using` | real |
| `Billing/Application/Ports/ICreditReadPort.cs` | `using` | real |
| `Billing/Application/Ports/IInvoiceReadPort.cs` | `using` | real |
| `Billing/Application/Queries/ListCreditQuery.cs` | `using` | real |
| `Billing/Application/Queries/ListInvoicesQuery.cs` | `using` | real |
| `Fulfillment/Application/Commands/CreateDespatchCommandHandler.cs` | `using` | real |
| `Fulfillment/Application/Commands/ReleaseStockCommandHandler.cs` | `using` | real |
| `Fulfillment/Application/Commands/ReplenishStockCommandHandler.cs` | `using` | real |
| `Fulfillment/Application/Commands/ReserveStockCommandHandler.cs` | `using` | real |
| `Fulfillment/Application/DespatchCreationService.cs` | `using` | real |
| `Fulfillment/Application/Ports/IStockReadPort.cs` | `using` | real |
| `Fulfillment/Application/Queries/CheckStockQuery.cs` | `using` | real |
| `Fulfillment/Application/Queries/ListStockQuery.cs` | `using` | real |
| `Fulfillment/Application/StockReplenishService.cs` | `using` | real |
| `Fulfillment/Application/StockReservationService.cs` | `using` | real |
| `Notifications/Application/Ports/INotificationIdempotency.cs` | two `<c>`-tag prose mentions, no cref, no using | **doc-comment-only, not real** |
| `Orders/Application/Commands/CancelOrderCommandHandler.cs` | `using` (payload type) **and** a direct `RpcJson.Serialize` call | real, two-part fix |
| `Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs` | `using`, used ONLY for a direct `RpcJson.Serialize` call (no payload type referenced at all) | real |
| `Orders/Application/Ports/IHealthCheck.cs` | one `<see cref="OrderToCash.Orders.Infrastructure.Health.HealthCheckAggregator"/>` | **doc-comment-only (cref), not real — the exact case the acceptance bullet names for exclusion** |
| `Orders/Application/Ports/IIdempotentSagaRunner.cs` | two `<c>`-tag prose mentions, no cref, no using | **doc-comment-only, not real** |
| `Orders/Application/Ports/ISagaCommands.cs` | `using` | real |
| `Orders/Application/Sagas/SagaCommandRequestFactory.cs` | `using` (payload types) **and** `RpcJson.Serialize` calls | real, two-part fix |

**24 of the 27 are real; 3 are doc-comment-only** (`IHealthCheck.cs`,
`IIdempotentSagaRunner.cs`, `INotificationIdempotency.cs`) and were left
untouched — there is nothing there for NetArchTest, or any IL-based check,
to see, and the acceptance bullet's own wording ("excluding cref-only doc
comments") anticipates exactly this.

### A second sweep the text-search population definition could not see

The acceptance bullet defines the population as "using directives AND
fully-qualified names" found by text search. That definition has a real
blind spot: C# resolves a **partially-qualified** name via the enclosing
namespace chain without any `using` and without the leading `OrderToCash.`
the grep pattern requires. Two Billing files did exactly this —
`ICommand<Infrastructure.Messaging.Rpc.InvoiceIssueReplyPayload>` compiles
because `OrderToCash.Billing.Application.Commands`'s enclosing namespace
`OrderToCash.Billing` also contains `Infrastructure.Messaging.Rpc`, and the
compiler climbs to find it. Found by building after the first pass (CS0234
on `IssueInvoiceCommand.cs`/`RegisterPaymentCommand.cs` once the old
namespace's payload types were deleted) and confirmed with:

```
find src -type d -name Application -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 -I{} find {} -name '*.cs' -print0 \
  | xargs -0 grep -lE "\bInfrastructure\.[A-Za-z]" 2>/dev/null
```

— 20 files matched; reading every one (excluding `///` lines) found real
code in exactly **two**, both in `src/Billing/Application/Commands/`:
`IssueInvoiceCommand.cs` (3 hits, all `ICommand<...>` / return-type
positions) and `RegisterPaymentCommand.cs` (3 hits, same shape). The other
18 were doc-comment prose (Notifications/Orders/Projector ports files
already classified above, plus Orders' `IRpcRequestSerializer.cs` and
`SagaCommandRequestFactory.cs`, which after this feature's own fix mention
"Infrastructure" only in a doc comment explaining why they no longer
import it).

**Reconciled population: 24 (text-search) + 2 (partial-qualification) = 26
real files fixed.** This is a finding for the record, not a criticism of
the brief's population — the brief's own definition named its method
("using directives AND fully-qualified names") honestly, and the method
has a gap that only a build catches. Both files are fixed the same way as
their siblings (payload type moved to `Contracts.Rpc`, `using` added).

## 2. What moved, and why the move became a unification

Bullet 2 says the RPC payload records move into `src/Contracts` (#7's own
placement — one shared contracts package, not one copy per service). Before
this feature, three of the six payload files were near-duplicates:

- `Billing/Infrastructure/.../CreditRpcPayloads.cs` and
  `Orders/Infrastructure/.../SagaCommandPayloads.cs`'s credit section
  declared the SAME six types twice — `CreditHoldRequestPayload`,
  `CreditHoldReplyPayload`, `CreditReleaseRequestPayload`,
  `CreditReleaseReplyPayload`, and a nested Money record (`CreditMoney` on
  Billing's side, `SagaMoney` on Orders' — identical shape,
  `(long Amount, string Currency)`, different name).
- Same duplication for `InvoiceIssueRequestPayload`/`InvoiceIssueReplyPayload`
  (Billing vs. Orders' saga copy).
- Same duplication for the five Fulfillment `StockReserve*`/`StockRelease*`/
  `DespatchCreate*`/`StockCheck*` types (Fulfillment's own copy vs. Orders'
  saga-side and stock-check copies).

Each file's own header comment explained this was **deliberate**: "RPC
payloads live in the service that speaks them" — the opposite of #7's
model, and the reason the RPC-payload half of this population exists at
all (per the entry's own notes). Moving Billing's copy to `Contracts` while
leaving Orders' separate copy in place would have solved the architecture
violation but kept the duplication (and the two copies could drift again).
Since #7 kept exactly one canonical type per RPC subject, shared by caller
and callee, unifying is what "follows #7's own placement" means literally,
not just relocates the symptom. So:

- `src/Contracts/Rpc/CreditRpcPayloads.cs` — canonical `CreditMoney`,
  `CreditHoldRequestPayload`, `CreditHoldReplyPayload`,
  `CreditReleaseRequestPayload`, `CreditReleaseReplyPayload`, plus
  Billing-only `CreditPageInfo`/`CreditListRequestPayload`/
  `CreditViewPayload`/`CreditListReplyPayload`.
- `src/Contracts/Rpc/InvoiceRpcPayloads.cs` — canonical
  `InvoiceIssueRequestPayload`/`InvoiceIssueReplyPayload`, plus
  Billing-only invoice-list and payment-register types.
- `src/Contracts/Rpc/StockRpcPayloads.cs` — canonical `StockCheck*`,
  `StockReserve*`, `StockRelease*`, plus Fulfillment-only stock-list/
  replenish types.
- `src/Contracts/Rpc/DespatchRpcPayloads.cs` — canonical
  `DespatchCreateRequestPayload`/`DespatchCreateReplyPayload`.

`Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs` and
`StockCheckPayloads.cs` are **deleted**, not retained as thin re-exports —
every field-for-field comparison (done by reading, not assumed) showed the
two copies of each type were byte-identical in shape, so there was nothing
to preserve by keeping a second declaration. `SagaMoney` is retired; every
call site now uses `CreditMoney` (grepped clean, `grep -rn "SagaMoney" src
tests` returns only the explanatory comment in the new Contracts file).

**Gateway is explicitly out of scope for this unification** — its own
`Application/Rpc/GatewayRpcPayloads.cs` keeps ITS OWN copy by the same
established rule, and its own header comment already explains why (never a
reference to another service's Infrastructure payload file, cross-checked
by property name against each). Gateway's Application layer has zero real
Application→Infrastructure hits (confirmed: all `Infrastructure` mentions
in `src/Gateway/Application/**` are `<c>` prose in doc comments, none are
`using` or fully-qualified/partially-qualified code references) — it was
never part of this population and this feature does not touch it.

### What did NOT move — resolved by a port instead

Three Orders files called `RpcJson.Serialize`/`Deserialize` directly — not
a wire-payload TYPE reference, but a call to an Infrastructure
serialization UTILITY. Per bullet 2's own second half ("anything else
crosses through a port"):

- **New port:** `src/Orders/Application/Ports/IRpcRequestSerializer.cs` —
  `byte[] Serialize<T>(T value)`.
- **New implementation:** `src/Orders/Infrastructure/Messaging/Rpc/RpcJsonRequestSerializer.cs`
  — a one-line wrapper composing the EXISTING, UNMODIFIED `RpcJson.Serialize`
  verbatim.
- `SagaCommandRequestFactory` converted from a `static class` with `static`
  methods to `public sealed class SagaCommandRequestFactory(IRpcRequestSerializer serializer)`
  — `BuildJson`/`BuildStockReleaseJson` are now instance methods;
  `StockReleaseReasonFor` and the private payload builders stay `static`
  (pure functions, no serialization).
- `CancelOrderCommandHandler` gained a constructor parameter
  `IRpcRequestSerializer serializer` and its direct `RpcJson.Serialize(payload)`
  call became `serializer.Serialize(payload)`.
- `OperatorCancelRequestedEnvelope.Build` gained a fifth parameter
  `IRpcRequestSerializer serializer`, replacing its own direct
  `RpcJson.Serialize(envelope)` call.
- `SagaFactHandler` gained a constructor parameter
  `SagaCommandRequestFactory requestFactory` and its three
  `SagaCommandRequestFactory.BuildJson`/`BuildStockReleaseJson` static
  calls became `requestFactory.BuildJson`/`requestFactory.BuildStockReleaseJson`.
- DI: `src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs`
  registers `services.AddScoped<IRpcRequestSerializer, RpcJsonRequestSerializer>();`
  and `services.AddScoped<Application.Sagas.SagaCommandRequestFactory>();`
  — `CancelOrderCommandHandler` is discovered by the CQRS assembly scan
  (CLAUDE.md), so only its new port dependency needed a registration line.

`RpcErrorPayload.cs`, `RpcJson.cs` (Billing/Fulfillment/Orders) and
`RpcSubjects.cs` (Orders) stay in each service's own
`Infrastructure/Messaging/Rpc` — confirmed by reading every Application
file in the population that no Application code references them directly
except through the new port.

## 3. Byte-identity — asserted, not assumed

`grep -rln "using OrderToCash\.\(Billing\|Fulfillment\|Orders\)\.Infrastructure\.Messaging\.Rpc;"`
before the move found **93 additional files** (Presentation/Infrastructure
production code plus tests) that also reference the moved types alongside
`RpcErrorPayload`/`RpcJson`/`RpcSubjects`. Each was classified (does it
still need the old `using` for a staying type, or only the moved types)
and updated mechanically — the old `using` was kept wherever
`RpcErrorPayload`/`RpcJson`/`RpcSubjects` is still referenced (58 files:
responders, validators, error mappers, most integration/unit tests) and
replaced outright where only payload types were used (35 files:
Presentation validators with no error-mapper code, EF Core read
repositories, all remaining Application files, and several payload-only
unit tests).

The existing schema-parity guards (feature ids 51/70's
"parsed-from-the-spec, never retyped" family) and the golden-envelope
parity tests were **run, not assumed**, against the moved types:

- `Billing.UnitTests` — 238/238, including `CreditRpcPayloadTests.BC23_*`
  and `InvoiceRpcPayloadTests`' equivalent, both reading the MOVED
  `CreditHoldReplyPayload`/`InvoiceIssueReplyPayload` etc. by reflection
  off `typeof(...)` and comparing against `asyncapi.yaml`.
- `Fulfillment.UnitTests` — 130/130, including `StockRpcPayloadTests`.
- `Orders.UnitTests` — 459/459, including `SagaCommandPayloadTests`'
  `BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped`
  theory (13 cases) and its `G5` arming case (both unaffected by the
  namespace move — they reflect off the type object and parse the spec
  text, neither of which is namespace-sensitive).
- `Contracts.UnitTests` — 24/24, including the golden-envelope parity
  tests under `GoldenEnvelopes/` (Kafka fact envelopes, not RPC, but
  proves `JsonWire.Options` — the shared serializer the moved types also
  go through — was untouched).

`JsonWire.Options` (`src/Contracts/Wire/JsonWire.cs`) was read: camelCase
naming policy + nulls-omitted, no `[JsonPropertyName]` attributes anywhere
on the moved records, no type-name-keyed lookup, no polymorphic
discriminator — nothing in the wire path depends on a type's CLR
namespace. Moving `CreditHoldReplyPayload` from
`OrderToCash.Billing.Infrastructure.Messaging.Rpc` to
`OrderToCash.Contracts.Rpc` cannot change one byte of what it serialises
to, and the round-trip tests (`RpcJsonPayloadTests`' `RoundTrip<T>` helper,
used throughout `SagaCommandPayloadTests`) prove the actual bytes, not just
the property set.

## 4. The new architecture rule

`tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs` —
one `[Fact]`, `ApplicationMustNotDependOnInfrastructure`:

```csharp
Types.InAssemblies(_serviceAssemblies)
    .That().ResideInNamespaceMatching(ApplicationNamespacePattern)   // (^|\.)Application(\.|$)
    .ShouldNot().HaveDependencyOnAny(_infrastructureNamespaceRoots)
    .GetResult();
```

`_serviceAssemblies` is a **literal** array of six `typeof(...).Assembly`
expressions (Gateway, Orders, Fulfillment, Billing, Notifications,
Projector) — the same six `DomainAssemblies.All` names, minus `Seed`.
`_infrastructureNamespaceRoots` is a literal six-element array of each
service's own `OrderToCash.<Service>.Infrastructure` root — not a bare
`"Infrastructure"` substring, so the check cannot be satisfied by
accident against an unrelated namespace that happens to contain the word.

**Seed is deliberately excluded from the population**, per bullet 5's
"literal list … never discovered by a predicate" and CLAUDE.md's own
repeated "six services" framing (Seed is a seeding utility, not one of the
six Clean-Architecture services CLAUDE.md enumerates). Swept separately as
a completeness check, not as part of the guarded population:

```
find src/Seed/Application -name '*.cs' | xargs grep -lE "OrderToCash\.Seed\.Infrastructure"
```

— zero hits. Seed's Application layer is already clean; excluding it from
the literal list costs nothing today.

## 5. Arming — verbatim, and what the guard cannot see

Protocol: `cp` a backup of each mutated file → mutate → forced rebuild →
run the ONE named test → record the verbatim failure → restore from the
backup → `cmp` → forced rebuild → confirm green. Never `git checkout --`
(both files are tracked, but the restore is from the backup regardless, so
the check is a genuine `cmp`, not a no-op).

### Attempt 1 (recorded honestly — it revealed a real property of the check)

First mutation: inside `CreditHoldService.HoldAsync`'s own
`unitOfWork.ExecuteAsync(async ct => { ... })` lambda, added
`RpcErrorPayload? armingProbe = null;` after adding
`using OrderToCash.Billing.Infrastructure.Messaging.Rpc;`. Rebuilt
(`dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental`,
succeeded), ran `ApplicationInfrastructureLayeringTests` alone — **passed
(1/1), not failed.** Not usable as an arming result: `= null` emits no
`newobj`/`call` IL instruction referencing the type at all (just `ldnull`),
so there is nothing for a Cecil-based dependency scan to see. Changed the
mutation to a real construction, `var armingProbe = new RpcErrorPayload("PROBE", "arming probe")`,
inside the SAME lambda — **still passed (1/1).** Confirmed by three further
probes on the SAME lambda-nested location, all invisible: a `newobj` alone,
a `newobj` + `RpcJson.Serialize` call chained through it, all discarded via
`_ = probe;`. A probe placed as the OUTER type's own METHOD signature
(`public RpcErrorPayload ArmingProbe() => new(...)`) or as a plain call
inside a method whose body is NOT itself a further lambda
(`public string DiagnosticProbe() => Encoding.UTF8.GetString(RpcJson.Serialize(new RpcErrorPayload(...)))`,
declared directly on `CreditHoldService`) was detected both times —
including once as an `async` METHOD (not lambda) whose signature exposed
nothing but whose call to `RpcJson.Serialize` inside its own body was still
caught, proving the scan DOES walk instruction-level `call`/`newobj`
operands of the outer type's own declared methods.

**MECHANISM CORRECTED — review round 1, advisory A1.** This section first
explained the blind spot as *"a lambda compiles to a SEPARATE,
compiler-generated nested type … and NetArchTest does not recurse into
it"*. **That mechanism is disproved.** The reviewer's own probes P6 and P8
placed references inside **synchronous** lambdas in the same
`unitOfWork.ExecuteAsync(...)` position and both were **caught**, and its
P7 missed a case that did not involve `ExecuteAsync` at all. So "a nested
closure is invisible" is not the boundary; every probe that survived this
scan sat inside an **`async` lambda** specifically — a state machine the
compiler emits as its own type, where a plain lambda is often inlined into
the declaring type or emitted in a form the scan still walks. The tested
boundary, stated as what was actually observed rather than as a theory:

| Shape | Result |
|---|---|
| reference inside an `async` lambda (`ExecuteAsync(async ct => …)`) | **missed** |
| reference inside a **sync** lambda in the same position | caught |
| `async` METHOD on the outer type, reference only in its body | caught |
| method signature, interface member, generic type argument | caught |

The **conclusion** of this section is unchanged and still holds — and the
bound is independently verified by the reviewer: none of the 26 real
violations was shaped as an async-lambda-only reference (Billing's and
Fulfillment's exposed their payload type in a public signature; the three
Orders files used `async` methods on the outer type, a caught shape). The
correction is recorded rather than quietly edited because `CLAUDE.md`'s
ledger rule is explicit that a confidently wrong mechanism is worse than an
absent one: it is the half #9 inherits and has no reason to re-derive. The
residual gap is filed as its own backlog entry.

**GAP CLOSED — backlog id 83
(`architecture_rule_cannot_see_references_inside_async_lambdas`, phase 14).**
Both halves of the finding above turned out to need correcting a second
time, not just the first. Re-probing the boundary directly with Mono.Cecil
(the same library `NetArchTest.Rules` 1.3.2 wraps) against a minimal
reproduction shows the mechanism is **nesting depth**, not "async lambda"
as such: `ResideInNamespaceMatching` only ever matches types whose OWN
`Namespace` is non-empty, and every compiler-generated closure/state-machine
type has `Namespace == ""`; the scan nonetheless reaches ONE level of such
types below a matched type, never two. A sync lambda that only touches
`this`/primary-constructor fields, or an `async` METHOD, needs no
intermediate closure class and sits at depth one — caught. An `async`
LAMBDA that captures a local variable or method parameter (this
repository's dominant `unitOfWork.ExecuteAsync(async ct => { … command
… })` shape) needs a `<>c__DisplayClassN_M` at depth one and its own state
machine at depth two — invisible; a NON-capturing async lambda is cached in
the compiler's shared `<>c` type at depth one with its state machine at
depth two — also invisible, for a different reason but the same measured
depth. `progress/impl_architecture_rule_cannot_see_references_inside_async_lambdas.md`
§2 has the full probe table (including the earlier P4/P7-style capture
that this repository's own review round 1 found missed).

Closed with a Roslyn `SemanticModel` scan over the real six services'
source (`tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`'s
`FindClosureConfinedInfrastructureReferences`, folded into the SAME
`ApplicationMustNotDependOnInfrastructure` fact rather than a second test) —
a lambda body is not a separate type at the syntax level, so a symbol
reference inside one is exactly as visible as one in the outer method's own
body, at any nesting depth. Armed by re-introducing the EXACT missed shape
(a real `new RpcErrorPayload(...)` inside `unitOfWork.ExecuteAsync(async ct
=> …)`, capturing the command parameter) in Billing's
`CreditReleaseService.ReleaseAsync` and Fulfillment's
`StockReservationService.ReserveAsync` simultaneously: the Cecil rule alone
reported `IsSuccessful=True` (confirmed by a temporary instrumentation read
before the fix, then removed) while the combined test failed naming both
`OrderToCash.Billing.Application.CreditReleaseService` and
`OrderToCash.Fulfillment.Application.StockReservationService`. Restored,
`cmp`-clean, forced rebuild, green — full detail, defeat-list probes and the
measured cost (35 → 36 tests, ≈4s → ≈7s) are in id 83's own record.

**Count of real violations in the newly visible population at the moment of
closing: zero**, read off id 83's own bullet-1 enumeration of the 12
`unitOfWork.ExecuteAsync(async ct => …)`-and-sibling-shape sites (11
files, across Billing, Fulfillment, Orders **and Projector** — id 83's
round-1 review found a twelfth site, `Projector/Application/
ProjectionApplyService.cs`'s `async (document, ct) =>`, missed by round
1's `grep -c "async ct =>"` predicate, which was narrower than the claim
it was measuring; id 83's own record now carries the corrected
enumeration) — none constructs an Infrastructure type or calls an
Infrastructure method from inside the lambda today. This is the same bound
this section already claimed by inference (none of the 26 real violations
fixed above was async-lambda-confined); id 83 is what turned that bound
from an inference into a direct, armed measurement of the now-closed gap.

## Ported-idiom ledger (added at review round 1, advisory A3)

The remedy in bullet 2 is explicitly a port of #7's **placement**, so the
row is owed here (`sdd: false` puts the ledger in this file).

| # | #7 relied on | In #8 that property is supplied by |
|---|---|---|
| 1 | #7's RPC payload types were **generated** from `asyncapi.yaml` into its shared contracts package (`order-to-cash-nestjs/packages/contracts/src/generated/asyncapi.types.ts`, present at #7 HEAD) — a payload record therefore **could not drift** from the spec, because regeneration would overwrite it | Hand-written records under `src/Contracts/Rpc` (#8 generates nothing). The anti-drift property is supplied by the **BC23 "parsed-from-the-spec, never retyped" tests**, which reflect off the type object and parse `asyncapi.yaml` itself: `CreditRpcPayloadTests.BC23_…`, `InvoiceRpcPayloadTests.BC23_…`, `StockRpcPayloadTests.BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi`, `SagaCommandPayloadTests.BC23_…` — all four run and green in §3's evidence, and namespace-insensitive, which is why the move could not weaken them |

The property is genuinely supplied and its guards exist, are named and were
run; what was missing was the row stating it. Recorded so #9 inherits the
question — *what stopped these types drifting over there, and what stops it
here?* — rather than the answer alone.

**This is a real, disclosable limitation, not a defect in this feature's
guard.** Every one of the 26 real violations this feature fixed was shaped
as either (a) a payload type in an interface/command/method signature, or
(b) a direct `RpcJson.Serialize`/`.Deserialize` call inside a method body
that is NOT itself wrapped in a further lambda passed to another method —
both shapes the rule demonstrably catches (see the official arming below).
The blind spot is narrower: an Infrastructure reference confined ENTIRELY
inside a lambda argument's own body, with no trace in the outer type's own
members. Recorded here rather than engineered around, because closing it
would mean recursively walking NetArchTest's underlying Cecil graph for
compiler-generated nested types — out of this feature's scope, and not
something any of the 26 real violations needed.

### Official arming — two services, the confirmed-detectable shape

Backups: `cp src/Billing/Application/CreditHoldService.cs
/tmp/.../CreditHoldService.cs.bak` and the Fulfillment equivalent.

Mutation (both services, applied together, one build, one test run):

```csharp
// Billing/Application/CreditHoldService.cs
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
...
public sealed class CreditHoldService(...)
{
    // ARMING PROBE (feature 76) — temporary, restored before submission.
    public RpcErrorPayload ArmingProbe() => new("PROBE", "arming probe — id 76");
    ...
```

```csharp
// Fulfillment/Application/StockReservationService.cs
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
...
public sealed class StockReservationService(...)
{
    // ARMING PROBE (feature 76) — temporary, restored before submission.
    public RpcErrorPayload ArmingProbe() => new("PROBE", "arming probe — id 76");
    ...
```

`dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental`
→ succeeded (the mutation is legal C#, only an architecture-convention
violation). `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build --filter "FullyQualifiedName~ApplicationInfrastructureLayeringTests"`:

```
[xUnit.net 00:00:00.57]     OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [FAIL]
  Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [273 ms]
  Error Message:
   Application types must not depend on any service's Infrastructure namespace — Infrastructure implements the ports Application declares, never the reverse (CLAUDE.md). RPC/Kafka wire payload records belong in src/Contracts; anything else crosses through a port. Offending types: OrderToCash.Fulfillment.Application.StockReservationService, OrderToCash.Billing.Application.CreditHoldService

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Both offending types named, exactly, in the message — the failing
assertion's own message names the claim, per this session's own backlog
id 82 rule.

Restore: `cp` back from the two `.bak` files, `cmp` both (clean, byte
identical), `touch` both (forced rebuild timestamp), rebuilt
`--no-incremental`, re-ran the SAME filtered test:

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

Then the full `Architecture.Tests` project, unfiltered:

```
Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26, Duration: 3 s
```

25 pre-existing architecture tests + this feature's 1 new one = 26.

## 6. Full-suite build/test/format evidence

- `dotnet build OrderToCash.sln --no-incremental` — succeeded, 0 errors,
  twice (once after the payload move + port introduction, once again
  after the final restore).
- `dotnet format OrderToCash.sln --verify-no-changes` — exit 0, clean
  (after one `dotnet format OrderToCash.sln` run to auto-sort the ~130
  `using` lines this feature's mechanical edits added/reordered; the two
  private static fields in the new architecture-test file needed a
  `_camelCase` rename to satisfy IDE1006, caught by the SAME verify pass —
  fixed, re-verified clean).
- `./quality.sh` — full run, log at
  `/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/quality_feature76.log`.
  Format clean, build succeeded, **all 18 projects passed**, coverage
  reports produced for all 18.
- `./init.sh` — exit 0, log at
  `/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/init1.log`,
  zero `[FAIL]` lines.

### Reconciled counts

Per-project totals from the `quality.sh` run, summed by hand:

```
50 (SharedKernel.UnitTests) + 23 (Cqrs.UnitTests) + 24 (Contracts.UnitTests)
+ 107 (Notifications.UnitTests) + 130 (Fulfillment.UnitTests)
+ 211 (Gateway.UnitTests) + 238 (Billing.UnitTests) + 459 (Orders.UnitTests)
+ 44 (Seed.UnitTests) + 26 (Architecture.Tests)
+ 6 (Seed.IntegrationTests) + 59 (Projector.IntegrationTests)
+ 22 (Notifications.IntegrationTests) + 64 (Fulfillment.IntegrationTests)
+ 90 (Billing.IntegrationTests) + 120 (Projector.UnitTests)
+ 59 (Gateway.IntegrationTests) + 146 (Orders.IntegrationTests)
= 1878
```

18 projects, all `Failed: 0`. **1878 = 1877 (the brief's stated baseline,
`/tmp/claude-1000/.../quality_feature73_fix1.log`) + 1** — the one new case
this feature adds, `ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure`
(`Architecture.Tests` went from 25 to 26). No other project's count moved:
`Orders.UnitTests` stayed at 459 (my edits there only changed constructor
call sites of EXISTING tests, added no new `[Fact]`/`[Theory]` case), and
the two intermittents the brief named (`Projector.IntegrationTests`'
`PR38_…ReadFromTheBroker`, `Gateway.IntegrationTests`'
`OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`) did not fire this
run — both projects are fully green (59/59 and 59/59) in the single run
above, so no isolated re-run was needed.

## 7. Scope note — files touched beyond the brief's named folders

The brief's scope paragraph named `Application/` and `Infrastructure/`
folders. Satisfying bullet 3 (existing schema-parity/golden tests still
pass) required the build to succeed, which required updating every
`Presentation/`-layer and test file that also referenced a moved type —
93 additional files (listed in §3), none of them an architecture-rule
violation (Presentation→Infrastructure and Infrastructure→Infrastructure
are both legal under CLAUDE.md's Clean Architecture section), all of them
mechanical `using`-directive updates with no logic change. This is called
out per this repository's own standing rule that a scope bound must be
read against what the approved acceptance criteria actually require, not
assumed from a folder list — bullet 3's requirement could not be met
otherwise.

## 8. What was NOT done, and why

- **Gateway's own RPC payload copy was left untouched.** It was never part
  of the population (zero real Application→Infrastructure references,
  confirmed above) and CLAUDE.md/the existing file comments record its
  separateness as deliberate.
- **The NetArchTest nested-lambda blind spot (§5) was not engineered
  around.** No real violation in this repository is shaped that way today,
  and closing it would require walking Cecil's compiler-generated nested
  types generically — a larger change than this feature's population
  warrants. Flagged here for whoever next hardens this guard family.
- **`feature_list.json` was not touched** — the leader owns the status
  transition, per the brief.

## Files touched

**Created:** `src/Contracts/Rpc/{CreditRpcPayloads,InvoiceRpcPayloads,StockRpcPayloads,DespatchRpcPayloads}.cs`,
`src/Orders/Application/Ports/IRpcRequestSerializer.cs`,
`src/Orders/Infrastructure/Messaging/Rpc/RpcJsonRequestSerializer.cs`,
`tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`.

**Deleted:** `src/Billing/Infrastructure/Messaging/Rpc/{CreditRpcPayloads,InvoiceRpcPayloads}.cs`,
`src/Fulfillment/Infrastructure/Messaging/Rpc/{StockRpcPayloads,DespatchRpcPayloads}.cs`,
`src/Orders/Infrastructure/Messaging/Rpc/{SagaCommandPayloads,StockCheckPayloads}.cs`.

**Hand-edited (design changes):** `src/Orders/Application/Ports/ISagaCommands.cs`,
`src/Orders/Application/Sagas/{SagaCommandRequestFactory,SagaFactHandler}.cs`,
`src/Orders/Application/Commands/{CancelOrderCommandHandler,OperatorCancelRequestedEnvelope}.cs`,
`src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs`,
`src/Billing/Application/Commands/{IssueInvoiceCommand,RegisterPaymentCommand}.cs`.

**Mechanically edited (93 files, `using`-directive swap/add only):** every
Billing/Fulfillment Application/Presentation/Infrastructure file and test
that referenced the moved payload types — full list reproducible via the
enumeration commands in §1/§3.

**Test call-site fixes:** `tests/Orders.UnitTests/{SagaCommandPayloadTests,SagaFactHandlerTests,SagaFactCommandHandlerTests,CancelOrderCommandHandlerTests,NatsSagaCommandsAdapterTests}.cs`,
`tests/Orders.IntegrationTests/SagaCommandRetryTests.cs`.

This record's own path:
`progress/impl_application_layer_depends_on_infrastructure_unguarded.md`.
