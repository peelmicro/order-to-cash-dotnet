# backlog id 73 — `notification_send_degrades_on_permanent_failure`

Ports #7's operational fix (`ad90de6`, `progress/impl_notification_degradation.md`
in the `order-to-cash-nestjs` checkout) into #8: a **PERMANENT** SMTP send
failure renders through the console fallback and acknowledges the fact; a
**TRANSIENT** one rethrows the original exception unchanged, leaving
`FactRetryDispatcher`'s retry-then-dead-letter path exactly as it is today.

This feature carries no `R<n>` — like #7's own fix, it was never written
down as a spec requirement (`specs/shared/test-matrix.md` is unchanged).

## Design as built

- `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs:1-65`
  — pure classifier, `Classify(Exception) : SendFailureClassification`
  (`Transient` | `Permanent`). Reads `MailKit.Net.Smtp.SmtpCommandException.StatusCode`
  (a `SmtpStatusCode` enum whose underlying `int` **is** the real
  three-digit SMTP reply code — confirmed by enumerating every member of the
  enum, not assumed: `MailboxUnavailable = 550`, `MailboxBusy = 450`,
  `AuthenticationRequired = 530`, `ServiceNotAvailable = 421`, …). `>= 500
  and < 600` → `Permanent` (RFC 5321 §4.2.1's Permanent Negative Completion);
  everything else — a 4xx `SmtpCommandException`, a `SocketException`, an
  `SmtpProtocolException`, anything unrecognised — → `Transient`.
- `src/Notifications/Infrastructure/Notification/DegradingNotificationSender.cs:1-89`
  — the `INotificationSender` decorator. On failure: `Transient` → `throw;`
  (the original exception, unchanged, stack trace preserved). `Permanent` →
  `logger.LogError(...)` with a stable `Event="notification.send.degraded"`
  structured field (`DegradingNotificationSender.cs:77-84`), then
  `fallback.SendAsync(message, ...)`, then returns normally (no throw).
- `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs:74-92`
  — the `Smtp` case now builds `new DegradingNotificationSender(mailKitSender,
  consoleFallback, logger)` instead of registering `MailKitNotificationSender`
  bare. The `Console` case (line 94-96) is untouched — nothing to degrade
  from, matching #7's `app.module.ts` rule.

### correlationId / traceId — no hand-rolled propagation needed

#7's decorator manually attaches `correlationId` (from the message) and
`traceId` (`activeTraceId()`) to its own `console.error(JSON.stringify(...))`
call, because Nest/nodemailer's logging has no ambient scope. #8's `ILogger`
pipeline already supplies both for free, for **every** log line emitted
while a fact is being handled:

- `correlationId` — `NotificationFactsConsumer.cs:156`'s
  `logger.BeginScope(new Dictionary<string, object> { ["correlationId"] =
  envelope.CorrelationId })`, which wraps the whole `FactRetryDispatcher.DispatchAsync`
  call, which wraps `NotificationDispatchService.DispatchAsync`, which
  wraps `DegradingNotificationSender.SendAsync`.
- `traceId` — `NotificationsHost.CreateBuilder`
  (`src/Notifications/NotificationsHost.cs:48`)'s
  `ActivityTrackingOptions.TraceId`, combined with
  `NotificationFactsConsumer.cs:150-152`'s `"consume {eventType}"` Activity.

So `DegradingNotificationSender` itself adds neither field — it only needed
to log through the injected `ILogger<DegradingNotificationSender>`, and the
ambient scope/Activity machinery (already proven for every other log line by
`LogCorrelationTests`) does the rest. Proven for THIS log line by the new
`NotificationDegradesOnPermanentFailureTests` (below).

## Ported-idiom ledger

**Corrected after review round 1's D1 — see "Fix round 1" below for the full evidence.** The original row 1 claimed #7's second signal had no #8 equivalent because `MailKitSmtpTransport` never authenticates; that reasoning is disproved (not authenticating is the PRECONDITION of the real exception, not a defence against it). Row kept below in its corrected form; the disproved original wording is not restated here — CLAUDE.md's own ledger-correction rule is to fix the row, not to preserve a wrong one alongside the right one.

| # | #7 relied on (file:line, `order-to-cash-nestjs` checkout) | In #8 that property is supplied by |
|---|---|---|
| 1 | nodemailer's own error shape — `err.responseCode` (the numeric SMTP reply code the library itself parses from the server's response, `send-failure-classifier.ts:58-64`) and `err.code === 'EAUTH'` when no response was ever read at all (`send-failure-classifier.ts:67-69`) — citation corrected per A4 to the implementation, not the file's comment block (`:15-41`) | TWO real MailKit signals, both required: (a) `MailKit.Net.Smtp.SmtpCommandException.StatusCode` (`SmtpStatusCode`, confirmed by enumeration of all 31 members to equal the literal 3-digit SMTP code — `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs:30-40`); (b) `MailKit.ServiceNotAuthenticatedException` (`SendFailureClassifier.cs:41-59`) — MailKit's OWN type for "authentication is required before sending", proven live against a REAL auth-requiring Mailpit (`SendFailureClassifierRealSmtpTests.ARealMailpitAuthRequiredRejection_530_...`) with a negative control against a REAL auth-advertised-only Mailpit where the same unauthenticated send SUCCEEDS (`...AuthAdvertisedOnly...`) — proving the exception is a property of the SERVER'S configuration, never of this transport's own code not calling `AuthenticateAsync`. Guard: `SendFailureClassifierTests` (15 cases, counted directly from the file: two `[Theory]` methods with 5 and 4 `[InlineData]` rows plus 6 `[Fact]` methods, table-driven, including the corruption arm) plus `SendFailureClassifierRealSmtpTests` (5 `[Fact]` cases, against real Mailpit servers) — all armed below. |

Only one row: this is the single MailKit-vs-nodemailer substitution the
feature required. Everything else (the decorator's transient/permanent
branching, the DI wiring, the console fallback) transliterates directly —
#8 already had `ConsoleNotificationSender`/`MailKitNotificationSender`
behind the same `INotificationSender` port (feature `notifications_service`),
so no second row applies.

## Bullet 3 — proof against a REAL SMTP failure, never a hand-constructed exception

Mailpit (`axllent/mailpit:v1.27.5`, the same pinned tag
`docker-compose.infra.yml` uses) ships an `--enable-chaos` mode whose HTTP
API (`PUT /api/v1/chaos`) makes the **real** SMTP server reject `RCPT TO`
with a caller-chosen, genuine three-digit reply code. New fixture
`tests/Notifications.IntegrationTests/MailpitContainerFixture.cs` drives
this via Testcontainers' generic `ContainerBuilder` (the same pattern
`KafkaContainerFixture` already uses for an image with no dedicated
Testcontainers module).

Verified directly (scratch probe, `dotnet run` against a real container,
before any test was written):

```
$ curl -s -X PUT http://localhost:12026/api/v1/chaos -d '{"Recipient":{"ErrorCode":550,"Probability":100},...}'
$ dotnet run   # real MailKit.Net.Smtp.SmtpClient, real TCP, real Mailpit
EXC TYPE: MailKit.Net.Smtp.SmtpCommandException
StatusCode(enum)=MailboxUnavailable StatusCode(int)=550 ErrorCode=RecipientNotAccepted
```

```
$ curl -s -X PUT http://localhost:12026/api/v1/chaos -d '{"Recipient":{"ErrorCode":450,"Probability":100},...}'
StatusCode(enum)=MailboxBusy StatusCode(int)=450 ErrorCode=RecipientNotAccepted
```

```
$ # connect to an unbound port — no chaos, no listener at all
EXC TYPE: System.Net.Sockets.SocketException
MESSAGE: Connection refused
```

Made permanent as `SendFailureClassifierRealSmtpTests` (Notifications.IntegrationTests):

- `ARealMailpitRecipientRejection_550_RaisesSmtpCommandException_AndClassifiesAsPermanent`
- `ARealMailpitRecipientRejection_450_RaisesSmtpCommandException_AndClassifiesAsTransient`
- `ARealRefusedConnection_RaisesASocketException_AndClassifiesAsTransient`

All 3 pass (`/tmp/claude-1000/test_notif_int_classifier_1.log`: `Passed! - 3, Total: 3`).

Additionally, `SmtpStatusCode`'s full enumeration was read directly
(reflection over the installed `MailKit 4.17.0` assembly, not the docs) to
confirm every member's underlying `int` equals its literal SMTP code —
`MailboxUnavailable=550`, `MailboxBusy=450`, `AuthenticationRequired=530`,
`ServiceNotAvailable=421`, `ExceededStorageAllocation=552`, etc. — before
writing the pure-unit boundary table.

## Bullet 4 — #7's tests enumerated, assertion by assertion

### `send-failure-classifier.spec.ts` (10 `it`s)

| # | #7 assertion | Classification | Where in #8 |
|---|---|---|---|
| 1 | quota-exhausted rejection (535, EAUTH) → permanent | **Ported** (adapted to the real MailKit signal) | `SendFailureClassifierTests.Classify_AnySmtpCommandExceptionWithA5xxStatusCode_IsPermanent` (`AuthenticationInvalidCredentials`=535 case) + `SendFailureClassifierRealSmtpTests` (550, real server) |
| 2 | invalid credentials (535, EAUTH) → permanent | **Not ported** — duplicate of #1's rule; the classifier reads only the numeric status, never message text, so a second 535+EAUTH case proves nothing #1 doesn't; #8's table instead covers the boundary with distinct codes (535, 550, 530, 552, 599) | — |
| 3 | EAUTH with no SMTP response at all → permanent | **Ported, corrected after review round 1's D1** — the original "Not applicable" reasoning ("`MailKitSmtpTransport` never calls `AuthenticateAsync`, so this cannot occur") was disproved: not authenticating is the PRECONDITION of a real `530`, not a defence against it. A real server configured to REQUIRE authentication raises `MailKit.ServiceNotAuthenticatedException` on exactly this transport's unauthenticated `SendAsync` call | `SendFailureClassifierTests.Classify_AServiceNotAuthenticatedException_IsPermanent` (pure, real type via its public ctor) + `SendFailureClassifierRealSmtpTests.ARealMailpitAuthRequiredRejection_530_...` (real Mailpit, `--smtp-auth-file`) + its negative control `...AuthAdvertisedOnly...` (real Mailpit, `--smtp-auth-accept-any`, unauthenticated send SUCCEEDS) |
| 4 | malformed/rejected recipient (550, EENVELOPE) → permanent | **Ported** | `Classify_AnySmtpCommandExceptionWithA5xxStatusCode_IsPermanent` (`MailboxUnavailable`=550) + `SendFailureClassifierRealSmtpTests.ARealMailpitRecipientRejection_550_...` |
| 5 | SMTP 4xx (450) → transient | **Ported** | `Classify_AnySmtpCommandExceptionWithA4xxStatusCode_IsTransient` (`MailboxBusy`=450) + `SendFailureClassifierRealSmtpTests.ARealMailpitRecipientRejection_450_...` |
| 6 | socket error (ESOCKET) → transient | **Ported** (MailKit-equivalent: a real `SocketException`) | `Classify_ASocketException_IsTransient` + `SendFailureClassifierRealSmtpTests.ARealRefusedConnection_...` (real refused TCP connection) |
| 7 | connection timeout (ETIMEDOUT) → transient | **Not ported** — redundant with #6 under the single "anything that is not `SmtpCommandException` defaults transient" rule; MailKit carries no per-code-name transient table to enumerate separately | — |
| 8 | connection refused (ECONNECTION) → transient | **Ported** — same real-socket proof as #6 | `SendFailureClassifierRealSmtpTests.ARealRefusedConnection_...` |
| 9 | unrecognised error (no code, no responseCode) → transient | **Ported** | `Classify_AnUnrecognisedException_DefaultsToTransient_NeverPermanent` |
| 10 | non-Error thrown value → transient | **Not applicable** — the CLR/CLS require every thrown object to derive from `Exception`; `SendFailureClassifier.Classify(Exception)` cannot even be called with a non-`Exception` value | `Classify_TakesExceptionNotObject_TheNonErrorThrownValueCaseHasNoEquivalent` (documents the non-applicability by reflecting the parameter type) |

Also added, beyond #7's table: `SmtpProtocolException` → transient
(`Classify_ASmtpProtocolException_IsTransient` — a MailKit exception type
with no nodemailer analogue at all, named in the brief as a signal to
verify) and a dedicated corruption-target test,
`Classify_ARealMailtrapStyleQuotaExhaustedRejection_550_IsPermanent_NeverTransient`
(the arm-family-2 target, below).

### `degrading-notification-sender.spec.ts` (8 `it`s, 3 layers — #7's own header)

| # | #7 assertion | Classification | Where in #8 |
|---|---|---|---|
| 1 | delegates to inner, never touches fallback on success | **Ported** | `DelegatesToInner_AndNeverTouchesFallbackOnSuccess` |
| 2 | rethrows a transient failure UNCHANGED, never calls fallback | **Ported** (arm family 1 target) | `RethrowsATransientFailure_UnchangedAndNeverCallsFallback` |
| 3 | permanent failure resolves normally, renders to fallback, logs loudly with reason | **Ported** (arm family 2 target) | `APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason` |
| 4 | permanent failure's log carries the message's OWN correlationId | **Ported, relocated** — #8's `NotificationMessage` carries no `correlationId` field (unlike #7's); correlationId is threaded ambiently (see "Design as built" above), observable only with a real host/logger pipeline | `NotificationDegradesOnPermanentFailureTests` (Notifications.IntegrationTests) |
| 5 | permanent failure distinguishable in logs — line only fires on degradation | **Ported** | `APermanentFailureIsDistinguishableInLogs_TheLogLineOnlyFiresOnDegradation` |
| 6 (layer 2) | transient failure retries 3x with backoff, dead-letters on exhaustion — unchanged | **Ported**, against #8's real `FactRetryDispatcher` | `ComposedWithTheRealFactRetryDispatcher_ATransientSendFailureRetries3xAndDeadLettersOnExhaustion` |
| 7 (layer 3) | permanent failure: dispatch resolves, ledger row NOT compensated, fallback receives message | **Ported**, against #8's real `NotificationDispatchService` | `ComposedWithTheRealNotificationDispatchService_APermanentSendFailure_DispatchResolves_LedgerRowNotDeleted_FallbackReceivesTheMessage` |
| 8 (layer 3) | redelivered eventId after permanent degrade is a genuine duplicate, never sent twice | **Ported** | `ComposedWithTheRealNotificationDispatchService_ARedeliveredEventIdAfterAPermanentDegrade_IsAGenuineDuplicate_NeverSentTwice` |

Added beyond #7's table (required by acceptance bullet 5's third arm
family, which #7's own layer-2 tests never covered — #7 only proved the
transient retry path, never "a permanent failure must NOT dead-letter"):
`ComposedWithTheRealFactRetryDispatcher_APermanentSendFailureNeverDeadLetters_SingleAttemptOnly`.

### `degrading-notification-sender-log-trace-id.spec.ts` (2 `it`s, R58 closeout in #7)

| # | #7 assertion | Classification | Where in #8 |
|---|---|---|---|
| 1 | degraded-send log carries the REAL active span's traceId and the message's own correlationId, equal to the real originating values | **Ported, relocated to integration** — #8 has no hand-rolled `console.error(JSON.stringify(...))` branch to unit-test in isolation; the property is a consequence of the framework's ambient scope/Activity pipeline, provable only with a real host | `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_DegradesToConsole_KeepsTheLedgerRow_AndNeverDeadLetters_WithCorrelationIdAndTraceIdOnTheLogLine` |
| 2 | omits traceId entirely (never the literal string "undefined") when no span is active | **Not applicable** — #8 never manually decides whether to include `traceId`; `ActivityTrackingOptions` + the JSON console formatter's own `Scopes` rendering does, identically for every log line this service emits, not only this one. There is no bespoke branch in `DegradingNotificationSender` to test | — |

### `console-notification-sender-log-trace-id.spec.ts` (3 `it`s) — MISSING from round 1's enumeration, added per review round 1's D2

Found by content search (`find . -name '*.spec.ts' -not -path '*/node_modules/*' -print0 | xargs -0 grep -ln "DegradingNotificationSender"` — the reviewer's exact command, re-run here and reconciled: 3 hits, this file, `degrading-notification-sender-log-trace-id.spec.ts` and `degrading-notification-sender.spec.ts`, all three now enumerated across this document).

| # | #7 assertion | Classification | Where in #8 |
|---|---|---|---|
| 1 | direct `ConsoleNotificationSender.send` logs the message's own correlationId and the REAL active span's traceId | **Not applicable, distinctly from case 3 below** — #8's `ConsoleNotificationSender.SendAsync` logs through the SAME ambient `ILogger` pipeline as every other log line in this service (no message-carried `correlationId` field exists on `NotificationMessage`); a dedicated "direct send, no degradation" proof would exercise the identical mechanism `LogCorrelationTests` and the new `NotificationDegradesOnPermanentFailureTests` already exercise for other call sites — not duplicated here to avoid re-proving one mechanism a third time | (mechanism proven by `LogCorrelationTests` and case 3 below) |
| 2 | omits correlationId/traceId entirely (never literal "undefined") when neither is available | **Not applicable** — same reasoning as `degrading-notification-sender-log-trace-id.spec.ts` case 2: #8 never manually decides whether to interpolate these fields; the JSON console formatter's own `Scopes` rendering does, structurally incapable of emitting the literal string `"undefined"` the way a hand-rolled JS template literal can | — |
| 3 | **the PRODUCTION degraded path** — `DegradingNotificationSender` falling back to `ConsoleNotificationSender` after a PERMANENT SMTP failure — still produces a traceable console line ("the console line is the ONLY record a notification went out") | **Ported — this is the guard D2 found dropped.** Round 1 shipped with NOTHING asserting the fallback's own console-adapter line was ever emitted or traceable on the degraded path; `NotificationDegradesOnPermanentFailureTests` only waited for the ERROR (degradation) line | `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_DegradesToConsole_KeepsTheLedgerRow_AndNeverDeadLetters_WithCorrelationIdAndTraceIdOnTheLogLine`, strengthened (see "Fix round 1" below) to also wait for and assert the console-adapter line, its `MessageId`, and its `TraceId` IDENTITY with the degraded line's own |

### `console-notification-sender.spec.ts` (2 `it`s)

| # | #7 assertion | Classification | Reason |
|---|---|---|---|
| 1 | records every sent message, exposes `callCount` | **Not ported** | #8's `ConsoleNotificationSender` (feature `notifications_service`, pre-existing) does not track a call list — any test in this feature needing "was send invoked" proof uses `FakeNotificationSender`/`FakeNotificationSender` from `TestSupport`, the repository's existing generic convention, applied uniformly rather than per-adapter |
| 2 | never sends real mail — no network client at all | **Not ported** | Pre-existing `ConsoleNotificationSenderTests.SendAsync_CompletesWithoutThrowing_ForAnyWellFormedMessage` already covers no-throw completion; `ConsoleNotificationSender.cs` structurally has no transport field to fail on a second call — the JS test's "call twice" is a workaround for TypeScript's lack of that structural guarantee, which C#'s type shows by inspection |

## Arming table (bullet 5 — all three families)

Protocol per CLAUDE.md: `cp` a backup → mutate → `dotnet build --no-incremental`
→ run ONE named test → record the verbatim failure → restore from backup →
`cmp` (identical) → forced rebuild (`touch`) → confirm green.

| # | Mutation | File | Named test | Verbatim failure | Restore verified |
|---|---|---|---|---|---|
| 1 | Deleted the transient-rethrow branch (`if (...Transient) { throw; }` removed — every failure degrades silently) | `DegradingNotificationSender.cs` | `DegradingNotificationSenderTests.RethrowsATransientFailure_UnchangedAndNeverCallsFallback` | `Assert.Same() Failure: Values are not the same instance` / `Expected: MailKit.Net.Smtp.SmtpCommandException: 450 mailbox temporarily unavailable` / `Actual: null` | `cmp` identical; rebuilt; re-ran → `Passed! - 1, Total: 1` |
| 2 | Corrupted exactly one permanent status (550, `MailboxUnavailable`) to transient, ahead of the general rule | `SendFailureClassifier.cs` | `SendFailureClassifierTests.Classify_ARealMailtrapStyleQuotaExhaustedRejection_550_IsPermanent_NeverTransient` | `Assert.Equal() Failure: Values differ` / `Expected: Permanent` / `Actual: Transient` | `cmp` identical; rebuilt; re-ran full `SendFailureClassifierTests` → `Passed! - 14, Total: 14` |
| 3 | Replaced the permanent branch's normal return with a rethrow (`throw;` added after `fallback.SendAsync`) | `DegradingNotificationSender.cs` | `DegradingNotificationSenderTests.ComposedWithTheRealFactRetryDispatcher_APermanentSendFailureNeverDeadLetters_SingleAttemptOnly` | `Assert.Equal() Failure: Values differ` / `Expected: 1` / `Actual: 3` (attempt count — the permanent failure was retried and would have dead-lettered) | `cmp` identical; rebuilt; re-ran full `Notifications.UnitTests` → `Passed! - 104, Total: 104` |

Backups kept at `/tmp/claude-1000/arming_backups/*.bak` for the duration of
the session (scratch, not committed).

## Files touched

- `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs` (new)
- `src/Notifications/Infrastructure/Notification/DegradingNotificationSender.cs` (new)
- `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs` (edited — `Smtp` case wraps the sender)
- `tests/Notifications.UnitTests/SendFailureClassifierTests.cs` (new, 14 cases)
- `tests/Notifications.UnitTests/DegradingNotificationSenderTests.cs` (new, 8 cases)
- `tests/Notifications.UnitTests/TestSupport/RecordingLogger.cs` (new — a genuine `ILogger<T>` recorder, no mocking framework)
- `tests/Notifications.UnitTests/TestSupport/FakeNotificationSender.cs` (edited — added `SendCallCount`, needed to count attempts against a sender that throws on every call)
- `tests/Notifications.IntegrationTests/MailpitContainerFixture.cs` (new — real Mailpit + chaos API fixture, `NotificationsWithMailpitCollection`)
- `tests/Notifications.IntegrationTests/SendFailureClassifierRealSmtpTests.cs` (new, 3 cases — bullet 3's real-server proof)
- `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs` (new, 1 case — full end-to-end proof)
- `progress/impl_notification_send_degrades_on_permanent_failure.md` (this file)

Not touched: `feature_list.json` (status transition left to the leader, per
the brief), `specs/shared/**`, any other service, `progress/current.md`,
`progress/history.md`, `README.md`, `docs/`, and the working tree's other
in-flight feature (id 62, `src/Orders/**` and its tests).

## Verification

- `dotnet build src/Notifications/Notifications.csproj --no-incremental` — clean, 0 warnings, 0 errors.
- `dotnet test tests/Notifications.UnitTests` — **104/104** passing (was 82 before this feature; +22: 14 classifier + 8 decorator).
- `dotnet test tests/Notifications.IntegrationTests` — **20/20** passing (was 16 before this feature; +4: 3 real-SMTP classifier + 1 end-to-end degrade).
- All three arming mutations confirmed to fail their named test with a message naming what was broken, then confirmed to restore to green (table above).
- Full `./quality.sh`:
  - **Round 1** (`/tmp/claude-1000/quality_feature73_1.log`): format clean, build clean (0/0), test summed to **1872** across 18 projects (reconciling exactly against the expected baseline: last full run 1844 + 2 id-62 `[Theory]` cases = 1846, + this feature's 26 new cases (22 unit + 4 integration) = **1872**, matched exactly). One failure: `Gateway.IntegrationTests.NatsRpcClientIntegrationTests.OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` — an RPC timeout (`RpcTimeoutError ... timed out after 2000ms`) under the full solution's concurrent Testcontainers load. Unrelated to this feature (Gateway/NATS code was never touched). Re-ran in isolation immediately after (`dotnet test tests/Gateway.IntegrationTests --filter ...OR4_TwoConcurrentCalls...`): **1/1 passing** — confirms a timing flake under contention, not a regression.
  - **Round 2** (`/tmp/claude-1000/quality_feature73_2.log`): launched to get a genuinely green top-to-bottom record including the coverage gate (round 1's script stopped before the coverage step on the one flake). **Format clean, build clean, all 18 projects green** — `Gateway.IntegrationTests` itself now **59/59** (confirming round 1's failure was the timing flake, not a regression), `Notifications.UnitTests` **104/104**, `Notifications.IntegrationTests` **20/20**. Summed total across all 18 projects: **1872** — identical to round 1's sum, reconciling exactly against the expected baseline (1844 + 2 id-62 `[Theory]` cases = 1846, + this feature's 26 new cases = **1872**). `dotnet test: all tests passed`; coverage summary printed for all 18 projects; `quality.sh finished` (`[OK]`).
  - `./init.sh` (`/tmp/claude-1000/init_feature73.log`): **exit 0** — `environment and state are coherent`. The only `[WARN]`s are the expected ones mid-session ("44 uncommitted change(s)", "run `./quality.sh` before closing a feature") and are pre-existing harness behaviour, not failures.
- `git status --porcelain` confirms this feature's own changes are confined to the declared scope: `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs` (edited), `src/Notifications/Infrastructure/Notification/{SendFailureClassifier,DegradingNotificationSender}.cs` (new), five new/edited files under `tests/Notifications.{Unit,Integration}Tests/`, and this record. Every other uncommitted change in the tree (`feature_list.json`, `progress/current.md`, `progress/history.md`, `progress/impl_operator_cancel_races_saga_forward_progress.md`, `progress/review_operator_cancel_races_saga_forward_progress.md`, `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`, `tests/Orders.IntegrationTests/RecordingFulfillmentStandIn.cs`) predates this session (backlog id 62's approved, uncommitted work, named in the brief) and was not touched.

## R<n> / test-matrix

None apply. Like #7's own `ad90de6`, this was never written down as a spec
requirement — `specs/shared/requirements.md` and `specs/shared/test-matrix.md`
are untouched, confirmed by `grep -n "degrad\|notification_send" specs/shared/test-matrix.md`
returning no hits before this session and none added.

## What was not done, and why

- No metrics counter (`otc_notifications_degraded_total` or similar) —
  #7's own record proposed exactly this and deliberately did not implement
  it, for the same reason it still does not apply here: the structured
  `Event="notification.send.degraded"` field exists precisely so a
  log-based metric/alert can be wired later without touching this code.
- No change to `NotificationsSmtpOptions`/env-var surface — "SMTP
  configured" is still decided once, explicitly, by
  `NotificationsOptions.SenderKind` (never inferred from credential
  presence), exactly as it already was before this feature; nothing about
  that decision needed to change for the wrapping to apply.
- Did not attempt to fold `console-notification-sender.spec.ts`'s call-recorder
  pattern into `ConsoleNotificationSender` itself — #8's existing
  `FakeNotificationSender` convention already gives every test that needs
  it, and duplicating the mechanism per-adapter would only add a second way
  to assert the same thing (classified not-ported above, with the reason).

## Surprises

- MailKit's `SmtpStatusCode` enum is not a guess or an approximation of the
  SMTP reply code — its underlying `int` for every member equals the real
  three-digit code exactly (confirmed by enumerating all **31** members via
  reflection before writing a single test — corrected here per review round
  1's count check; the figure originally written was 36, which did not
  reconcile against the actual enumeration). This made the classifier's
  FIRST signal simpler than #7's own reading of nodemailer's `responseCode`:
  one boundary (`>= 500`), no message-text special case. Round 1 found the
  classifier was missing #7's SECOND signal entirely (D1, corrected below).
- Mailpit's `--enable-chaos` mode (a feature of the exact image this
  repository already pins, `axllent/mailpit:v1.27.5`) turned out to be a
  direct, zero-extra-dependency way to make a REAL SMTP server return a
  caller-chosen reply code — `PUT /api/v1/chaos` with
  `{"Recipient":{"ErrorCode":550,"Probability":100}}` — which is exactly
  what bullet 3 asked for and removed any need to hand-roll a fake SMTP
  listener.
- **Disproved, corrected in "Fix round 1" below.** This bullet originally
  claimed `MailKitSmtpTransport` never calling `AuthenticateAsync` "quietly
  eliminates" #7's second classifier branch. Review round 1's D1 disproved
  that in the direction that matters: not authenticating is the
  PRECONDITION of a real server's `530` reply, not a defence against it — a
  real Mailpit configured to REQUIRE authentication raises
  `MailKit.ServiceNotAuthenticatedException` on exactly this transport's
  own unauthenticated `SendAsync` call, proven live. The genuine surprise,
  restated correctly: the signal is real and reachable, and the classifier
  was missing it entirely until this round.

## Fix round 1

Rejected at review round 1 (`progress/review_notification_send_degrades_on_permanent_failure.md`): 2 blocking (D1, D2), 5 advisory (A1-A5, A5 the leader's). This section records what changed, the arming evidence, and the reconciled counts. Only `src/Notifications/**` and `tests/Notifications.{Unit,Integration}Tests/**` were touched — no other service, no `specs/shared/**`, no `feature_list.json`, no `progress/history.md` or `progress/current.md`.

### D1 — the auth-required signal, corrected

**Defect:** a real `530 Authentication required` reply raises `MailKit.ServiceNotAuthenticatedException` (base chain `InvalidOperationException → SystemException`), which `SendFailureClassifier.Classify` did not check for — it fell to the default and returned `Transient`, so the exact DLQ-storm failure this feature exists to prevent would recur for any auth-requiring SMTP host.

**Fix:** `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs:72-85` now checks `error is ServiceNotAuthenticatedException` as a second, independent Permanent branch, alongside the existing `SmtpCommandException`-5xx check. `using MailKit;` added for the type.

**Real-wire evidence (both directions, against the SAME pinned `axllent/mailpit:v1.27.5` image, no custom socket server needed):**

```
--- Mailpit started with --smtp-auth-file (AUTH REQUIRED), unauthenticated send ---
capabilities: Size, EnhancedStatusCodes, Authentication, UTF8
AuthenticationMechanisms: PLAIN,LOGIN
IsAuthenticated: False
EXC TYPE: MailKit.ServiceNotAuthenticatedException
BASE CHAIN: ServiceNotAuthenticatedException -> InvalidOperationException -> SystemException -> Exception -> Object
MESSAGE: 5.7.0 Authentication required
is SmtpCommandException: False

--- Mailpit started with --smtp-auth-accept-any, NO auth file (AUTH advertised only), unauthenticated send ---
capabilities: Size, EnhancedStatusCodes, Authentication, UTF8
AuthenticationMechanisms: LOGIN,PLAIN
IsAuthenticated: False
SENT OK (unexpected)
```

Made permanent as two new integration tests in `SendFailureClassifierRealSmtpTests.cs`, driving `MailpitAuthContainerFixture.cs` (new — two real Mailpit containers, one per configuration, using `WithResourceMapping(byte[], string)` to inject the auth-file bytes with no host file dependency):

- `ARealMailpitAuthRequiredRejection_530_RaisesServiceNotAuthenticatedException_AndClassifiesAsPermanent`
- `ARealMailpitAuthAdvertisedButNotRequired_TheUnauthenticatedSendSucceeds` (the negative control review round 1 asked to see in the corrected row)

Plus a fast pure-unit case using the real type's own public constructor (never a hand-rolled substitute): `SendFailureClassifierTests.Classify_AServiceNotAuthenticatedException_IsPermanent`.

The ledger row and bullet-4 row 3 (`send-failure-classifier.spec.ts` case 3) are corrected IN PLACE above, not merely noted here — a wrong "history half" left standing is exactly the failure CLAUDE.md's ledger rule exists to prevent.

### D2 — the missing spec file and its dropped guard

**Defect:** bullet 4's enumeration named two of the three #7 spec files that exercise `DegradingNotificationSender` by content (`grep -ln "DegradingNotificationSender"` across `*.spec.ts`). `console-notification-sender-log-trace-id.spec.ts` was missing, and its third assertion — that the FALLBACK's own console-adapter line is emitted and traceable on the PRODUCTION degraded path — had no #8 equivalent: `NotificationDegradesOnPermanentFailureTests` only ever waited for the degradation ERROR line, never the console INFO line that follows it.

**Fix:** the file is now enumerated above (new sub-section, all 3 assertions classified). Its dropped guard (case 3) is ported: `NotificationDegradesOnPermanentFailureTests.cs` gained `WaitForConsoleAdapterLogLineAsync` (matches `ConsoleNotificationSender.cs`'s own `"notification (console adapter): ..."` message prefix, scoped by the same `correlationId`), and the test now asserts this line is present and carries the expected `MessageId`.

### A1 — traceId strengthened from presence to identity

`Assert.False(string.IsNullOrEmpty(...))` replaced/extended with: both the degraded-error line's and the console-adapter line's `TraceId` must (a) match `^[0-9a-f]{32}$` and (b) be EQUAL to each other — the same fact, two log statements, one real trace, matching #7's own `degrading-notification-sender-log-trace-id.spec.ts:56-57` assertion shape ("not merely field presence").

### A2 — the two `SenderKind` bindings can now be told apart

New file `tests/Notifications.UnitTests/NotificationSenderBindingTests.cs` (2 cases): builds the REAL host composition (`NotificationsHost.CreateBuilder`, the same DI-shape-only probe `NotificationsDispatcherRegistrationTests` already uses) and resolves the real `INotificationSender` singleton for each `SenderKind`. `SenderKind.Console` must resolve an unwrapped `ConsoleNotificationSender` (`Assert.IsType`, exact type). `SenderKind.Smtp` must resolve a `DegradingNotificationSender` whose OWN `Inner` is a `MailKitNotificationSender` — the property the reviewer's M3 swap gets backwards.

This needed a small production addition: `DegradingNotificationSender.cs` gained an `internal INotificationSender Inner { get; }` property (the primary-constructor `inner` parameter is now read through it, to avoid CS9124's "captured and also used to initialize a member" error), and a new `src/Notifications/InternalsVisibleTo.cs` grants `OrderToCash.Notifications.UnitTests` access to it — the SAME established per-service convention already used by `Cqrs`, `Billing`, `Gateway` and `Fulfillment` (`src/<Service>/InternalsVisibleTo.cs`).

### A3 — MessageId guarded on the degraded log line

`DegradingNotificationSenderTests.APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason` now builds its message with an explicit `MessageId` and asserts `entry.State["MessageId"]` equals it. The end-to-end integration test asserts the same on both the degraded-error line and the console-adapter line, against the real `{eventId}@order-to-cash` value.

### A4 — ledger citation repointed

Ledger row 1's #7 citation now reads `send-failure-classifier.ts:58-64` (the `responseCode` check) and `:67-69` (the `EAUTH` check) — the implementation, not the file's comment block (`:15-41`).

### Arming table (round 1 re-arm, all five)

Protocol identical to the original round: `cp` backup → mutate → `dotnet build --no-incremental` → run ONE named test → record the verbatim failure → restore from backup → `cmp` (identical) → forced rebuild (`touch`) → confirm green. No two builds ever ran concurrently (`pgrep -a dotnet | grep -E " (build|test|format)( |$)"` checked empty before every mutation).

| # | Fix | Mutation | File | Named test | Verbatim failure | Restore verified |
|---|---|---|---|---|---|---|
| 1 | D1 | Removed the `ServiceNotAuthenticatedException` branch | `SendFailureClassifier.cs` | `SendFailureClassifierTests.Classify_AServiceNotAuthenticatedException_IsPermanent` | `Assert.Equal() Failure: Values differ / Expected: Permanent / Actual: Transient` | `cmp` identical; rebuilt; `Notifications.UnitTests` → `Passed! - 107, Total: 107` |
| 1b | D1 (real wire) | SAME mutation, same build | `SendFailureClassifier.cs` | `SendFailureClassifierRealSmtpTests.ARealMailpitAuthRequiredRejection_530_...` | `Assert.Equal() Failure: Values differ / Expected: Permanent / Actual: Transient` | `cmp` identical; rebuilt; `SendFailureClassifierRealSmtpTests` → `Passed! - 5, Total: 5` |
| 2 | D2 | Suppressed the console-adapter log line's emission (`_ = logger;`, no `LogInformation` call) | `ConsoleNotificationSender.cs` | `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_DegradesToConsole_...` | `Assert.NotNull() Failure: Value of type 'Nullable<ValueTuple<string, string>>' does not have a value` (the `consoleLine` the fallback should have produced) | `cmp` identical; rebuilt; same test → `Passed! - 1, Total: 1` |
| 3 | A1 | `ActivityTrackingOptions.TraceId \| SpanId` → `ActivityTrackingOptions.None` (CLAUDE.md's own documented probe for this exact property) | `NotificationsHost.cs` | same integration test as above | `Assert.False() Failure / Expected: False / Actual: True` (`TraceId` vanished — `string.IsNullOrEmpty` became true) | `cmp` identical; rebuilt; same test → `Passed! - 1, Total: 1` |
| 4 | A2 | Swapped the decorator's arguments: `DegradingNotificationSender(consoleFallback, smtpSender, ...)` | `NotificationsServiceCollectionExtensions.cs` | `NotificationSenderBindingTests.SenderKindSmtp_ResolvesADegradingNotificationSenderWhoseInnerIsTheMailKitSender` | `Assert.IsType() Failure: Value is not the exact type / Expected: typeof(MailKitNotificationSender) / Actual: typeof(ConsoleNotificationSender)` | `cmp` identical; rebuilt; `NotificationSenderBindingTests` → `Passed! - 2, Total: 2` |
| 5 | A3 | `message.MessageId` argument replaced with the literal `"corrupted-message-id"` | `DegradingNotificationSender.cs` | `DegradingNotificationSenderTests.APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason` | `Assert.Equal() Failure: Values differ / Expected: event-73-degraded@order-to-cash / Actual: corrupted-message-id` | `cmp` identical; rebuilt; `Notifications.UnitTests` → `Passed! - 107, Total: 107` |

Every mutation targeted exactly the file the fix touched; every restore was confirmed both by `cmp` against the pre-mutation backup (`/tmp/claude-1000/arming_backups/*.round2.bak`) and by re-reading the changed lines (shown verbatim in the tool transcript this round).

### Reconciled counts

- `dotnet format --verify-no-changes` (`/tmp/claude-1000/fix1_format2.log`): clean, exit 0.
- `dotnet build OrderToCash.sln --no-incremental` (`/tmp/claude-1000/fix1_solution_build.log`): succeeded, 0 warnings, 0 errors. Counted from the `.sln` itself (`grep "^Project(" OrderToCash.sln`, 30 lines, minus the 2 solution folders `src`/`tests`): **28 buildable projects** (10 source incl. `Cqrs`, 18 test) — a full-solution build was run (not merely the two Notifications projects) because this round added a new assembly-level attribute (`InternalsVisibleTo.cs`); nothing outside `src/Notifications/**`/`tests/Notifications.*Tests/**` was edited, so a full `./quality.sh` was not re-run, per this round's own instruction.
- `dotnet test tests/Notifications.UnitTests` (`/tmp/claude-1000/fix1_unit_full_final.log`): **107/107** — reconciles exactly against round 1's 104 plus this round's 3 new cases (`Classify_AServiceNotAuthenticatedException_IsPermanent`, `NotificationSenderBindingTests` ×2).
- `dotnet test tests/Notifications.IntegrationTests` (`/tmp/claude-1000/fix1_int_final_full.log`): **22/22** — reconciles exactly against round 1's 20 plus this round's 2 new cases (`ARealMailpitAuthRequiredRejection_530_...`, `ARealMailpitAuthAdvertisedButNotRequired_...`). The existing `APermanentSmtpFailure_DegradesToConsole_...` test was STRENGTHENED (D2, A1, A3) rather than duplicated, so it does not add to this count.
- Total Notifications-project count this round: 107 + 22 = **129**, up from round 1's 124 (104 + 20) by exactly the 5 new cases named above — reconciled by name, not merely by arithmetic.

### Files touched this round (in addition to round 1's list)

- `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs` (edited — D1 fix)
- `src/Notifications/Infrastructure/Notification/DegradingNotificationSender.cs` (edited — A2's `Inner` accessor, A3's log-line MessageId unchanged in production, only the test strengthened)
- `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs` (unchanged in its final state — only mutated transiently for A2's arm, then restored)
- `src/Notifications/InternalsVisibleTo.cs` (new — A2)
- `tests/Notifications.UnitTests/SendFailureClassifierTests.cs` (edited — D1, +1 case: `Classify_AServiceNotAuthenticatedException_IsPermanent`, 14 → 15 methods, counted directly from the file)
- `tests/Notifications.UnitTests/DegradingNotificationSenderTests.cs` (edited — A3)
- `tests/Notifications.UnitTests/NotificationSenderBindingTests.cs` (new — A2, 2 cases)
- `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs` (new — D1)
- `tests/Notifications.IntegrationTests/MailpitContainerFixture.cs` (edited — added `MailpitAuthContainerFixture` to `NotificationsWithMailpitCollection`)
- `tests/Notifications.IntegrationTests/SendFailureClassifierRealSmtpTests.cs` (edited — D1, +2 cases)
- `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs` (edited — D2, A1, A3, strengthened in place)
- This file (ledger row 1, bullet-4 rows, Surprises corrected in place; this section appended)

Not touched: `feature_list.json`, `progress/history.md`, `progress/current.md`, `progress/review_notification_send_degrades_on_permanent_failure.md`, id 62's files, any service other than Notifications.
