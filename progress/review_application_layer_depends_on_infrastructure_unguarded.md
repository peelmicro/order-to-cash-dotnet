# review: application_layer_depends_on_infrastructure_unguarded (backlog id 76) — round 1

## Status header

- **Verdict: APPROVED.** 0 blocking defects, 4 advisory, 1 routing item the leader must file.
- **Transition I would make (I made no edit — the leader owns `feature_list.json`):** id 76 `in_progress` → `done`.
- **Effort record:** appended to `progress/history.md` as part of this approval (a feature without one is not closeable).
- **What I did not redo:** the leader's full `./quality.sh` (1878, 18 projects, 0 failed) was not re-run — my findings are not about the full suite. I re-ran only `Architecture.Tests` (26/26), four times, around eight independent mutations of my own.

## Traceability — one row per acceptance bullet

| Bullet | What it demands | Evidence I verified myself | Verdict |
|---|---|---|---|
| B1 | the class enumerated FIRST as a search result, one classification line each, cref-only doc comments excluded | Re-ran the record's §1 sweep today: **3 files** match `OrderToCash.<Service>.Infrastructure` under `src/*/Application/`, and all three are the doc-comment-only hits the bullet excludes (`Notifications/Application/Ports/INotificationIdempotency.cs`, `Orders/Application/Ports/{IHealthCheck,IIdempotentSagaRunner}.cs`). Ran the partial-qualification sweep (`\bInfrastructure\.[A-Za-z]`, `///` lines removed) — **empty**. 27 − 3 = 24, +2 partial-qualification = **26 real, all closed** | **MET** |
| B2 | payload records move to `src/Contracts`; anything that is not a wire payload crosses a port | `src/Contracts/Rpc/{Credit,Invoice,Stock,Despatch}RpcPayloads.cs`, 39 records. I diffed every merged record against **both** HEAD copies field-by-field: all identical; the only rename is `SagaMoney`→`CreditMoney`, whose property names (`Amount`,`Currency`) are identical, so the wire is unchanged. `IRpcRequestSerializer` is declared in `src/Orders/Application/Ports/` and implemented in `Infrastructure/Messaging/Rpc/RpcJsonRequestSerializer.cs` as `RpcJson.Serialize(value)` verbatim — a genuine Application-declared port, not a relocated Infrastructure type. DI: `IRpcRequestSerializer`, `SagaCommandRequestFactory`, `SagaFactHandler` all `AddScoped` — no captive dependency | **MET** |
| B3 | the id 51/70 schema-parity guards still pass against the moved records, wire stays byte-identical, asserted not assumed | The moved types are what those guards now reflect on: `CreditRpcPayloadTests` (7 `typeof` cases), `InvoiceRpcPayloadTests` (9), `StockRpcPayloadTests` BC23 (12 `InlineData` + round-trips), `SagaCommandPayloadTests` BC23 (13 `typeof`) — all resolving to `OrderToCash.Contracts.Rpc` and all green in the leader's run. Namespace-sensitivity checked independently: **no** `JsonPropertyName` on any moved record, and a repo-wide sweep for `JsonSerializerContext\|TypeInfoResolver\|JsonDerivedType\|JsonPolymorphic\|AssemblyQualifiedName` returns only three `GetType().FullName` hits, all exception text in fact mappers. `JsonWire.Options` keys off property names via `CamelCase`, never the CLR namespace | **MET** |
| B4 | a NetArchTest rule, armed in two services, failing with the offending type named | `ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure`. Implementer's arm names both types. My own eight probes below name every type I broke — the id 82 requirement is satisfied by this assertion's message | **MET** |
| B5 | the rule's population is a literal list, never a predicate a violating assembly could escape | Six `typeof(...).Assembly` literals, six literal `OrderToCash.<Service>.Infrastructure` roots; `Seed` excluded deliberately and swept clean. Proven live, not read: probes **P1 (Projector)** and **P3 (Gateway)** — two services the implementer never probed, one of which had zero violations — both failed the rule | **MET** |

## Probes — my own, all eight

Protocol on every one: `cp` backup → mutate → `dotnet build --no-incremental` → one named test → verbatim output → restore from backup → `cmp` → `touch` → forced rebuild → confirming green. Never `git checkout --`. One build at a time; `pgrep -a dotnet | grep -E " (build|test|format)( |$)"` returned nothing before each.

**Run 1 — four mutations, one build, one run.** Verbatim:

```
  Error Message:
   Application types must not depend on any service's Infrastructure namespace — Infrastructure implements the ports Application declares, never the reverse (CLAUDE.md). RPC/Kafka wire payload records belong in src/Contracts; anything else crosses through a port. Offending types: OrderToCash.Gateway.Application.Queries.ReviewProbeP3, OrderToCash.Notifications.Application.ReviewProbeP2, OrderToCash.Projector.Application.Ports.IReviewProbeP1
```

| Probe | Family / unit | Shape, and where | Result |
|---|---|---|---|
| **P1** | substitution — *service* | `interface IReviewProbeP1` with a method returning `Projector.Infrastructure.Health.CheckResultDto`, in `Projector/Application/Ports/IHealthCheck.cs`. An **interface**, the shape half the real population had (`ISagaCommands`, `ICreditReadPort`, `IStockReadPort`), never probed by the implementer | **CAUGHT** |
| **P2** | corruption — *invocation path* | body-only `call` to `Notifications.Infrastructure.Observability.TraceContext`, no Infrastructure type in the signature, in `Notifications/Application/NotificationDispatchService.cs` | **CAUGHT** |
| **P3** | substitution — *service* | `IReadOnlyList<Gateway.Infrastructure.Messaging.Rpc.RpcErrorPayload>` as a **generic type argument** in a return type, in `Gateway/Application/Queries/GetOrderQuery.cs` — the service whose Application layer had zero violations | **CAUGHT** |
| **P4** | the disclosed blind spot | `newobj RpcErrorPayload` **inside the `unitOfWork.ExecuteAsync(async ct => …)` lambda** of `Billing/Application/CreditReleaseService.cs` — a different service and a different expression from the implementer's | **MISSED** (absent from the failing list; build succeeded with the `newobj` present) |

**Run 2 — the record's bound, which turns on a distinction §5 does not draw.** The three Orders violations put their `RpcJson.Serialize` call inside **`async` private methods on the outer type** (`CancelOrderCommandHandler.BeginCreditReleaseCompensationAsync:182`, `BeginStockReleaseCompensationAsync:214` at HEAD), and an `async` method's body also compiles into a compiler-generated nested state machine. If that shape were invisible, a member of this feature's own population would have escaped its own guard.

| Probe | Shape | Result |
|---|---|---|
| **P5** | `async` method on the outer type, `await Task.Yield()`, body calls `Orders.Infrastructure…RpcJson.Serialize`, nothing in the signature — the real `CancelOrderCommandHandler` shape | **CAUGHT** |
| **P6** | non-capturing **sync** lambda (`Select(_ => new RpcErrorPayload(…))`) inside an Application method | **CAUGHT** |

**Run 3 — isolating the boundary.**

| Probe | Shape | Result |
|---|---|---|
| **P7** | capturing **async** lambda held in a local `Func<Task<string>>` and invoked locally — no `unitOfWork`, no port call | **MISSED** |
| **P8** | capturing **sync** lambda | **CAUGHT** |

**Tested boundary:** a reference confined to an **async lambda** is invisible to this rule (P4, P7); a reference in an **async method**, a **sync lambda** (capturing or not), an interface member, a generic type argument or a plain body call is caught (P1, P2, P3, P5, P6, P8).

**Restore evidence.** All five mutated files `cmp`-identical to their backups and sha256-identical to their pre-probe values (`517406…`, `c290e2…`, `b6eedd…`, `8f0680…`, `1f0133…`); `grep` for `ReviewProbeP[0-9]\|reviewProbeP[0-9]` across `src` and `tests` returns nothing; forced rebuild then `Architecture.Tests` **26/26 green**; `git status --short | wc -l` is **146**, exactly as I found it. `feature_list.json` untouched by me.

## The 93-file scope widening (§7)

Enumerated rather than sampled: for every file in `git diff --name-only -- src tests`, I counted changed lines that are **not** `using` directives and not blank. **All six changed `Presentation/` files score 0** — `Billing/Presentation/{BillingRpcResponder,Rpc/CreditRequestValidator,Rpc/InvoiceRequestValidator,Rpc/PaymentRegisterRequestValidator}.cs`, `Fulfillment/Presentation/{StockRpcResponder,Rpc/StockRequestValidator}.cs` — i.e. `using`-only, no logic change. No Presentation→Infrastructure reference was "fixed": 17 directories outside `Application/` still import a service's `Infrastructure.Messaging.Rpc` for the types that correctly stayed there (`RpcJson`, `RpcErrorPayload`, `RpcSubjects`), and **zero** files under any `Application/` do. Every other non-`using` diff in the tree belongs to the two other uncommitted features (ids 62, 73), not to this one.

## Counts

1878 = 50+23+24+107+130+211+238+459+44+26+6+59+22+64+90+120+59+146, summed from the 18 `Passed!` lines of `quality_feature76.log` (10:29:51). The stated baseline log exists at `/tmp/claude-1000/quality_feature73_fix1.log` and sums to **1877**, with `Architecture.Tests` at **25** against **26** now. 1877 + 1 reconciles exactly, and `Architecture.Tests` is the only project that moved. Nothing in `src/` or `tests/` was newer than the quality log when I started.

## Findings

**No blocking defects.**

- **A1 (advisory) — §5 states a mechanism that is disproved, while its conclusion holds.** `progress/impl_application_layer_depends_on_infrastructure_unguarded.md:322-337` explains the blind spot as *"a lambda compiles to a SEPARATE, compiler-generated nested type … and NetArchTest does not recurse into it"*. P6 and P8 are lambdas in the same position and both were **caught**; P7 shows the missed case does not need `unitOfWork.ExecuteAsync` either. The real boundary is the **async** lambda specifically. This matters because the conclusion is right and will therefore be inherited unread: CLAUDE.md's ledger rule is explicit that a confidently wrong mechanism is worse than an absent one, since #9 has no reason to re-derive it. Remedy: restate §5 as the tested boundary (one paragraph, no code change).
- **A2 (advisory, and the routing item) — the blind spot covers the dominant shape of Billing and Fulfillment Application code.** Every transactional service there is `unitOfWork.ExecuteAsync(async ct => { … })`, so the guard is blind precisely where that business logic lives: a future `RpcJson.Serialize` added inside one of those lambdas ships unguarded on a green suite. Nothing in `specs/shared/` causes this, so it is **not** an SA-n — it is a guard-hardening entry in the id 72/82 family. **I am not filing it (I must not write `feature_list.json`); the leader should**, with this text: *"`ApplicationInfrastructureLayeringTests` cannot see an Infrastructure reference confined to an async lambda; verified by probe (async lambda missed, async method / sync lambda / interface member / generic argument caught). Close it by walking Cecil's compiler-generated nested types, and arm it with the async-lambda shape."* The bound the record claims — that none of the 26 was shaped this way — **is verified**: the Billing/Fulfillment services and every port and query exposed their payload type in a public signature at HEAD, and the three Orders files used async methods on the outer type, which P5 proves is caught.
- **A3 (advisory) — no ported-idiom ledger row, on a feature whose remedy is explicitly a port of #7's placement.** Bullet 2 ports #7's one-shared-contracts-package placement, and `sdd: false` means the ledger belongs in `progress/impl_<feature>.md`. The row is owed and is one line: *"#7's payload types were **generated** from `asyncapi.yaml` (`order-to-cash-nestjs/packages/contracts/src/generated/asyncapi.types.ts`, present at #7 HEAD), so a payload record could not drift from the spec; in #8 they are hand-written under `src/Contracts/Rpc`, and that property is supplied by the BC23 'parsed-from-the-spec, never retyped' tests."* Advisory rather than blocking because the property really is supplied and its guards exist, are named in §3 and were run — `CreditRpcPayloadTests.BC23_…`, `InvoiceRpcPayloadTests.BC23_…`, `StockRpcPayloadTests.BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi`, `SagaCommandPayloadTests.BC23_…`. This is documentation debt, not a hole.
- **A4 (advisory) — a third payload copy survives in `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`.** Correctly out of this feature's population (it is an Application-layer type, so it violates nothing) and correctly out of scope. But the unification's own argument — one canonical type per RPC subject, so two copies cannot drift — now applies to `StockListRequestPayload`, `CreditListReplyPayload`, `InvoiceViewPayload` and their siblings, which exist in both `Contracts.Rpc` and Gateway's copy. Worth a backlog entry at the leader's discretion; not owed here.
- **N1 (note, not a defect)** — bullet 2 cites #7 at `bf45af0` while the checkout is at `63f130e` today. I verified the cited file exists and declares the payload interfaces regardless.

## What I checked that the leader had already checked

Confirmed by my own commands rather than re-run wholesale: the enumeration reconciles (3 residual hits, all doc-comment), both populations are literal, no arming probe survives anywhere in `src/` or `tests/`, and the count arithmetic 1877 + 1 = 1878 holds against both logs.

## Time

Review round 1, ≈35 min: reading, four build/test cycles on `Architecture.Tests`, eight mutation probes across five services, the HEAD-vs-now record comparison, and this file.
