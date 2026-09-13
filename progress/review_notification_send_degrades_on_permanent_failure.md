# Review — backlog id 73 `notification_send_degrades_on_permanent_failure`

**Verdict: REJECTED** (2 blocking, 5 advisory). Reviewer time: ≈55 min. Transition I would make, for the leader to apply: **id 73 → `in_progress`**. I made no edit to `feature_list.json` (its diff is the leader's/id-62's, 18 insertions / 2 deletions, untouched by me).

The feature is close, and most of it is genuinely well built: the decorator's branching is correct, the three implementer arms are real and reproduce, and the Mailpit `--enable-chaos` fixture is exactly the right instrument for bullet 3. It is rejected on one defect of substance — **the one MailKit signal that carries #7's second permanent case is classified as transient, and the record's reason for not covering it is disproved by a real server** — plus an incomplete bullet-4 enumeration that hid a dropped #7 guard.

I did **not** re-run `./quality.sh`; the last full run (`/tmp/claude-1000/quality_feature73_2.log`, 18 projects, 1872, 0 failed) is the leader's and my findings are not about the full suite. What I ran myself: four `Notifications.UnitTests` runs (104/104 each side of every mutation), three targeted single-test runs, four independent mutations, and two real-SMTP probes through the real classifier.

---

## Blocking defects

### D1 — a real `530 Authentication required` rejection is classified **Transient**, so it retries and dead-letters: the exact failure this feature exists to prevent

**Where:** `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs:61-64`; the claim that excuses it is `progress/impl_notification_send_degrades_on_permanent_failure.md:64` (ledger row 1) and `:128` (bullet-4 row 3), repeated in source at `SendFailureClassifier.cs:51-57` and `SendFailureClassifierRealSmtpTests.cs:17-18`.

The classifier is `error is SmtpCommandException { StatusCode: var s } && (int)s is >= 500 and < 600`. Its own doc-comment claims to implement "RFC 5321 §4.2.1's OWN classification … a `5yz` reply is a Permanent Negative Completion". **A real server replying `530 5.7.0 Authentication required` to `MAIL FROM` does not produce an `SmtpCommandException` at all** — MailKit raises `MailKit.ServiceNotAuthenticatedException`, which is not in that type hierarchy, so the classifier falls through to `Transient`. The fact is then retried `MaxAttempts` times and dead-lettered — the DLQ storm `ad90de6` was written to stop (its own commit body: "728 facts had been dead-lettered … an exhausted Mailtrap free-tier quota").

**Probed, both directions, through the real `SendFailureClassifier` (project reference to the built `OrderToCash.Notifications.dll`, not a re-implementation), driving exactly `MailKitSmtpTransport`'s sequence — `ConnectAsync(…, SecureSocketOptions.None)`, `SendAsync`, `DisconnectAsync`, no `AuthenticateAsync`:**

```
--- AUTH-REQUIRED server, real 530 reply to MAIL FROM (localhost:13530) ---
    capabilities: Size, EnhancedStatusCodes, Authentication | mechanisms: [LOGIN,PLAIN] | IsAuthenticated=False
    EXC TYPE  : MailKit.ServiceNotAuthenticatedException
    BASE CHAIN: ServiceNotAuthenticatedException -> InvalidOperationException -> SystemException -> Exception
    MESSAGE   : 5.7.0 Authentication required
    is SmtpCommandException: False
    >>> REAL SendFailureClassifier.Classify -> Transient

--- CONTROL, real 550 reply to MAIL FROM (localhost:13550) ---
    capabilities: Size, EnhancedStatusCodes, Authentication | mechanisms: [LOGIN,PLAIN] | IsAuthenticated=False
    EXC TYPE  : MailKit.Net.Smtp.SmtpCommandException
    BASE CHAIN: SmtpCommandException -> CommandException -> Exception
    MESSAGE   : 5.1.0 Mailbox unavailable
    is SmtpCommandException: True (StatusCode=MailboxUnavailable=550, ErrorCode=SenderNotAccepted)
    >>> REAL SendFailureClassifier.Classify -> Permanent
```

The 550 control is what calibrates the probe: the same client, the same code path, the same server implementation, one reply code apart — one lands on the guarded branch, the other does not.

**The ledger row's reason is disproved, and disproved in the direction that matters.** The row says the `EAUTH`-with-no-response shape is "structurally unreachable from this transport" *because* `MailKitSmtpTransport` never calls `AuthenticateAsync`. Not authenticating is the **precondition** of this exception, not a defence against it — MailKit's own XML documentation for all four `SmtpClient.Send`/`SendAsync` overloads carries `<exception cref="T:MailKit.ServiceNotAuthenticatedException">Authentication is required before sending a message.` I also ran the opposite direction so the row is not condemned on one reading: against a real Mailpit started with `--smtp-auth-accept-any --smtp-auth-allow-insecure` (AUTH **advertised** but not **required**), the send **succeeded** — `capabilities: Size, EnhancedStatusCodes, Authentication, UTF8`, `RESULT: send SUCCEEDED (no exception)`. So the claim is not wrong everywhere; it is wrong exactly in the auth-**requiring** configuration, which is the one #7's incident ran in (`smtp-notification-sender.ts:46-51` builds its transport with `auth: { user, pass }`) and which `NOTIFICATIONS_SMTP_HOST`/`_PORT` can be pointed at today.

**Why no existing guard sees it, which is the part that makes this the ledger class rather than an ordinary miss.** `SendFailureClassifierTests.cs:27` *does* carry `AuthenticationRequired` (530) as a permanent case — but it constructs `new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, statusCode, …)`, a type MailKit never produces for that reply on this path, so the table proves the **rule** and can never detect the **type mismatch**. And `SendFailureClassifierRealSmtpTests` — the one file whose stated purpose is bullet 3's "every MailKit signal … proven against a REAL Mailpit server's REAL wire responses" — probes 550, 450 and a refused connection only. The signal that carries #7's second permanent case is the one signal never put on a wire.

**Acceptance impact:** bullet 3 ("#8 names the MailKit equivalents it relies on and proves each against a real SMTP failure … never a hand-constructed exception") is not met for that signal, and bullet 4's "not applicable" reason for `send-failure-classifier.spec.ts` case 3 does not hold.

**Not a `specs/shared/` matter.** `git diff --stat -- specs/shared/` is empty, `grep -n "degrad\|notification_send" specs/shared/test-matrix.md` returns nothing, and the shared spec prescribes no SMTP failure taxonomy. No `SA-n` amendment is implicated and none should be proposed; this is implementation-level and the rejection is the whole routing.

### D2 — bullet 4's enumeration misses a #7 spec file that names the ported class, and the guard in it was dropped

**Where:** `progress/impl_notification_send_degrades_on_permanent_failure.md:120-174` (the four enumerated files).

Enumerated by **content**, not by filename, which is what the claim is about:

```
$ find . -name '*.spec.ts' -not -path '*/node_modules/*' -print0 | xargs -0 grep -ln "DegradingNotificationSender"
./apps/notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts
./apps/notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts
./apps/notifications/src/infrastructure/notification/degrading-notification-sender.spec.ts
```

Three files in #7 exercise the class; the record enumerates two of them (plus `send-failure-classifier.spec.ts` and `console-notification-sender.spec.ts`). **`console-notification-sender-log-trace-id.spec.ts` is absent**, and it is not incidental: its third `it` (`:90-138`) is explicitly *"the PRODUCTION degraded path — DegradingNotificationSender falling back to ConsoleNotificationSender after a PERMANENT SMTP failure — still produces a traceable console line"*, asserting `logged.correlationId === 'order-1'`, `logged.traceId === originTraceId` and `fallback.callCount === 1`. Its own header states why it exists: *"On that path the console line is the ONLY record a notification went out."*

#8 has no equivalent assertion: nothing anywhere asserts that the **fallback's own** console line (`ConsoleNotificationSender.cs:19-23`) is emitted or traceable on the degraded path. `NotificationDegradesOnPermanentFailureTests` waits for the **degradation error** line and never looks for the console-adapter line that follows it. This is the phase-13 shape twice over — a guard that existed in a checkout on this machine, in a file named after the thing being ported, dropped in translation and invisible to traceability.

---

## Advisories

- **A1 — the ported `traceId` assertion is materially weaker than #7's, and is classified "Ported" without saying so.** `NotificationDegradesOnPermanentFailureTests.cs:100` asserts `Assert.False(string.IsNullOrEmpty(degraded.Value.TraceId))`. #7's counterpart (`degrading-notification-sender-log-trace-id.spec.ts:56-57`) asserts `logged.traceId === originTraceId` **and** `/^[0-9a-f]{32}$/`, and its test name says "not merely field presence". Presence-only cannot distinguish the real active trace from any non-empty string. The strengthening is available without new machinery: the test already parses every captured line for this `correlationId`, so it can assert the degraded line's `TraceId` equals the `TraceId` on the sibling lines of the same fact (the property `LogCorrelationTests.cs:104-106` checks by distinctness) and matches the 32-hex shape.
- **A2 — the wiring bullet's "a console-only binding stays unwrapped" half has no guard, and no unit test can tell the two bindings apart.** My substitution mutation (M3 below) swapped the two real sibling senders in the DI wiring — `DegradingNotificationSender(consoleFallback, smtpSender, …)`, i.e. the decorator aimed at the wrong target — and `Notifications.UnitTests` stayed **104/104 green**. `find tests -name '*.cs' … | xargs grep -n "GetRequiredService<INotificationSender>\|IsType<ConsoleNotificationSender>\|IsType<DegradingNotificationSender>"` returns **no hits**: nothing asserts what either `SenderKind` branch resolves to. The Smtp side is covered end-to-end by the container test; the Console side is asserted nowhere, so wrapping it (the thing the comment at `NotificationsServiceCollectionExtensions.cs:80-81` forbids) would pass every suite.
- **A3 — `MessageId` on the degraded log line is unguarded.** `DegradingNotificationSender.cs:84` logs it; the unit test asserts `Event`, `To` and `Subject` (`DegradingNotificationSenderTests.cs:95-97`) and the integration test asserts `Event` and `TraceId` only. Corrupting that one argument leaves everything green.
- **A4 — the ledger row's #7 citation points at prose, not code.** Row 1 cites `send-failure-classifier.ts:15-41`; that range is the file's comment block, and the implementation is `:58-72`. I checked the claim itself against the code and it is **accurate** (`responseCode` 5xx → permanent, then `code === 'EAUTH'` → permanent, else transient). Worth correcting anyway, because the history half is the half #9 inherits and has no reason to re-derive.
- **A5 — no effort record exists yet.** `progress/history.md` has no id-73 entry; a feature is not closeable without one. Evidence for the window when it is written: first source file 07:15:19, last 07:27:48, record 07:59:59, logs 07:15–07:59 on 2026-09-12 (`ls --time-style=full-iso`).

---

## Traceability — one row per acceptance bullet

This feature carries **no `R<n>`**, correctly: `git diff --stat -- specs/shared/` is empty and `grep -n "degrad\|notification_send" specs/shared/test-matrix.md` returns nothing, matching #7's own `ad90de6` ("Not a `feature_list.json` entry"). So the unit of traceability is the acceptance bullet, and the unit of coverage is the **test case**, not the class.

| Bullet | Case(s) that exercise it | Arm proving the case can fail | Failure message names the claim? |
|---|---|---|---|
| 1 — transient rethrows unchanged, retry-then-DLQ untouched | `DegradingNotificationSenderTests.RethrowsATransientFailure_UnchangedAndNeverCallsFallback`; `…ComposedWithTheRealFactRetryDispatcher_ATransientSendFailureRetries3xAndDeadLettersOnExhaustion` | implementer arm 1 (transient rethrow deleted) | Yes — `Assert.Same() … Expected: SmtpCommandException: 450 … Actual: null` |
| 1 — decorator wired only when SMTP is configured | `NotificationDegradesOnPermanentFailureTests` (real `SenderKind.Smtp` host) | — | — |
| 1 — **a console-only binding stays unwrapped** | **NO CASE** | reviewer M3 → suite stayed green | **A2** |
| 2 — permanent renders to fallback, returns normally, one structured line with stable `Event` | `…APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason`; `…APermanentFailureIsDistinguishableInLogs_TheLogLineOnlyFiresOnDegradation` | reviewer M1b (deletion) and M2 (corruption) | Yes — `Assert.Single() Failure: The collection was empty`; `Expected: notification.send.degraded / Actual: …v2` |
| 2 — no retry, no `.dlq`, ledger row stays, offset commits | `…ComposedWithTheRealFactRetryDispatcher_APermanentSendFailureNeverDeadLetters_SingleAttemptOnly`; `…ComposedWithTheRealNotificationDispatchService_APermanentSendFailure_…LedgerRowNotDeleted_…`; `…ARedeliveredEventIdAfterAPermanentDegrade_IsAGenuineDuplicate_NeverSentTwice`; integration `APermanentSmtpFailure_DegradesToConsole_KeepsTheLedgerRow_AndNeverDeadLetters_…` | implementer arm 3 (permanent branch rethrows) | Yes — `Expected: 1 / Actual: 3` attempts |
| 2 — line carries `correlationId` **and** `traceId` | integration case above (`correlationId` genuinely bracketed — lines are selected by the published value) | — | `traceId` presence-only → **A1** |
| 3 — MailKit signals each proven against a real SMTP failure | `SendFailureClassifierTests` (14 cases); `SendFailureClassifierRealSmtpTests` 550 / 450 / refused connection (real Mailpit + real refused TCP) | implementer arm 2 (550 → transient) | Yes — `Expected: Permanent / Actual: Transient` |
| 3 — the auth-required signal (#7's second permanent case) | **NO CASE ON A REAL WIRE**; `SendFailureClassifierTests.cs:27` uses a type MailKit never raises there | reviewer 530-vs-550 probe | **D1** |
| 4 — #7's tests enumerated assertion by assertion | record `:120-174` | reviewer re-derivation by content | **D2** — one of three files that name the class is missing |
| 5 — armed in all three families | implementer arms 1–3; reviewer M1b / M2 / M3 | see below | Deletion and corruption: yes. Substitution: **no test fails** → A2 |

`SmtpStatusCode`-equals-the-SMTP-code (bullet 3, verified myself rather than on the record's word): I enumerated the enum out of the installed MailKit 4.17.0 — **31 members**, and **none** whose underlying `int` falls outside a three-digit 2xx–5xx code (`421=ServiceNotAvailable`, `450=MailboxBusy`, `530=AuthenticationRequired`, `535=AuthenticationInvalidCredentials`, `550=MailboxUnavailable`, `552=ExceededStorageAllocation`, …). The claim holds. Note the record says "36 members"; the real count is 31 — immaterial to the claim, but it is a number that did not reconcile.

## My own mutations — one per family, independent of the implementer's three

Protocol each time: `cp` backup → mutate → `dotnet build --no-incremental` → one named test → verbatim failure → restore from **my** backup → `cmp` → `touch` + forced rebuild → confirming green. Nothing else was building (`pgrep -a dotnet | grep -E " (build|test|format)( |$)"` empty before starting).

| # | Family | Mutation | Result |
|---|---|---|---|
| M1 | deletion | removed `await fallback.SendAsync(...)` outright | **did not compile** — `error CS9113: Parameter 'fallback' is unread.` The compiler is itself a guard against that exact deletion; re-run as M1b to give the family a fair probe |
| M1b | deletion | `await fallback.SendAsync(...)` → `_ = fallback;` | `APermanentFailure_ResolvesNormally_RendersToFallback_AndLogsLoudlyWithTheReason` **FAILED**: `Assert.Single() Failure: The collection was empty` |
| M2 | corruption | `"notification.send.degraded"` → `"notification.send.degraded.v2"` | same case **FAILED**: `Assert.Equal() Failure: Values differ / Expected: notification.send.degraded / Actual: notification.send.degraded.v2` |
| M3 | **substitution of a valid sibling** | DI wiring `DegradingNotificationSender(smtpSender, consoleFallback, …)` → `(consoleFallback, smtpSender, …)` — the decorator aimed at the wrong target | `Notifications.UnitTests` **104/104 GREEN** → **A2** |

Restores verified against **two independent references**: `cmp` against my own backups (both files IDENTICAL) and against the implementer's `/tmp/claude-1000/arming_backups/*.bak` (both IDENTICAL). Confirming run after all restores: `Passed! - Failed: 0, Passed: 104, Total: 104`. The working tree is exactly as I found it — same 11 Notifications entries in `git status --porcelain`, same diffstat (`NotificationsServiceCollectionExtensions.cs` +20/-1, `FakeNotificationSender.cs` +11). The two files I mutated now have **newer mtimes** than `quality_feature73_2.log` because of the restore-and-rebuild cycle; their **contents** are byte-identical, which is what `cmp` above establishes — a "no file newer than the log" check will now trip on them and should be read that way.

## Probe 6 — the transient path is untouched, verified structurally

`git status --porcelain -- src/Notifications/Infrastructure/Messaging src/Notifications/Application src/Notifications/Presentation` is **empty**: `FactRetryDispatcher`, `NotificationDispatchService` and `NotificationFactsConsumer` are unmodified by this feature. The decorator sits strictly below them behind the unchanged `INotificationSender` port, and `throw;` preserves the instance (asserted by `Assert.Same`) and the stack. The retry-then-DLQ behaviour is additionally exercised end-to-end by the pre-existing, untouched `NotificationDeadLetterTests.OR1_R16_APoisonFactIsRetriedThenDeadLettered…`.

## CHECKPOINTS walked

**C1 — harness complete:** [x] all files present; [x] agent definitions declare models; [x] `./init.sh` exit 0 (`/tmp/claude-1000/init_feature73.log`: "environment and state are coherent").
**C2 — state coherent:** [x] exactly one `in_progress` (id 73; 80 entries, 54 done, 25 pending); [x] statuses valid; [x] `progress/current.md` describes this session; [ ] **"every `done` feature has passing tests"** — not at issue here, but id 73 must not become `done` on this round.
**C3 — architecture:** [x] no framework reference added to any `Domain/` folder (the feature touches `Infrastructure/` only); [x] no cross-service DB access; [x] no new shared runtime code; [x] no `decimal`, no money arithmetic involved; [x] Kafka-fact / NATS-RPC classification unaffected (SMTP is neither); [x] no stray debug logging — the one new log line is structured and intentional. NetArchTest ran inside the leader's full run (`Architecture.Tests` green in `quality_feature73_2.log`); I did not re-run it, and nothing in this feature moves a namespace.
**C4 — verification real:** [x] `./quality.sh` green — **read off the leader's log, not re-run by me**; [x] integration tests use real containers (real Mailpit, real Kafka, real MS-SQL — `MailpitContainerFixture` is a genuine `ContainerBuilder`, no mocked SMTP); [x] unit tests framework-free; [ ] **coverage gate** — the script prints `[OK]` and per-project line coverage, but I did not independently locate the ≥80/≥60 enforcement line, so that box rests on the script's own word (same disclosure as the id-62 review); [x] no Jest.
**C5 — session close:** [x] no suspicious untracked files (my scratch lives in the session scratchpad, outside the repo); [ ] **`progress/history.md` entry with effort record — MISSING (A5)**; [x] `feature_list.json` reflects true state (id 73 `in_progress`), and I changed nothing in it; [x] no commit by me.
**C6 — SDD:** not applicable, `"sdd": false`, and correctly so — no `specs/<name>/` is owed.
**C7 — reuse fidelity:** [x] `specs/shared/` untouched (`git diff --stat -- specs/shared/` empty); [x] no amendment implicated; [x] `R<n>` ids not claimed (none apply); [ ] **the ported-guard half of C7's spirit fails on D2** — a #7 guard was dropped without being classified; [ ] effort record outstanding (A5).

## What must change before re-review

1. **D1** — classify the auth-required failure as permanent on MailKit's real signal. `MailKit.ServiceNotAuthenticatedException` (base `InvalidOperationException`, **not** `SmtpCommandException`) is the type a real `530` reply produces on this transport's exact call path; decide it deliberately and prove it **on a real wire**, not with a constructed exception — a server that replies `530` to `MAIL FROM` is enough, and `MailpitContainerFixture` plus its chaos API or a small real listener already gives you one. Correct ledger row 1 and bullet-4 row 3: the reason "structurally unreachable because we never call `AuthenticateAsync`" is false, and my negative-direction result (AUTH merely advertised → send succeeds) belongs in the corrected row so the next reader gets both directions.
2. **D2** — add `console-notification-sender-log-trace-id.spec.ts` to the bullet-4 enumeration and classify its three assertions, in particular the production-degraded-path case; port or consciously decline the dropped guard that the **fallback's own** console line is emitted and traceable on the degraded path.
3. **A1** — strengthen the degraded line's `traceId` assertion from presence to identity (equal to the sibling lines' `TraceId` for the same fact) plus the 32-hex shape, matching #7.
4. **A2** — add a case that tells the two `SenderKind` bindings apart: `SenderKind.Console` resolves an unwrapped `ConsoleNotificationSender`, `SenderKind.Smtp` resolves a `DegradingNotificationSender` whose inner is the MailKit sender. It must fail under my M3 swap.
5. **A3/A4** — assert `MessageId` on the log line; repoint the ledger citation at `send-failure-classifier.ts:58-72`.
6. **A5** — the leader writes the `progress/history.md` entry with the effort record (sessions + wall-clock) before this feature can close; also reconcile the "36 members" figure against the enumerated 31.

Re-review needs: the corrected classifier with its real-wire proof, the two new/strengthened cases armed (each seen to fail with a message naming the claim), the corrected ledger row and enumeration, and a green `Notifications.UnitTests` + `Notifications.IntegrationTests` pair. A full `./quality.sh` is not needed unless the fix reaches beyond `src/Notifications/`.

---

# Review round 2 (fix round 1)

**Verdict: APPROVED**, with one item outstanding that is not the implementer's: **A5**, the `progress/history.md` entry with its effort record, which the leader writes and without which the feature is not closeable. Transition I would make, for the leader to apply: **id 73 → `done`** once A5 is written. I made no edit to `feature_list.json`. Reviewer time round 2: ≈40 min.

Both blockers are genuinely closed, and closed at the level they were raised: D1 by a second classifier branch proven on a real wire in **both** directions, D2 by a guard that asserts the fallback's **own** line rather than that some line appeared. Five of my own mutations — one of them an integration run — all failed the right named case and restored clean.

**What I did not re-run:** `./quality.sh`. The leader's post-fix run stands, and I verified its arithmetic myself rather than taking the figure: `grep -oE "Passed: +[0-9]+" … | awk` over `/tmp/claude-1000/quality_feature73_fix1.log` gives **18 projects, sum 1877**, with **18** `^Passed!` lines and **zero** `^Failed!` lines; `Notifications.UnitTests` 107 (line 99) and `Notifications.IntegrationTests` 22 (line 133) match the claimed movers, and `find src tests -name '*.cs' -newer <log>` is empty. What I ran myself: the D1 re-derivation, four unit-test runs, and two runs of the integration case (mutated and restored).

## P1 — D1 re-derived on a real wire, not taken from the record

The record's evidence is a scratch probe it ran itself, so I ran my own, against two Mailpit containers **I** started with the fixture's exact flags, driving `MailKitSmtpTransport`'s exact sequence (`ConnectAsync(…, SecureSocketOptions.None)` → `SendAsync` → `DisconnectAsync`, no `AuthenticateAsync`) and calling the **real** `SendFailureClassifier` out of the current `OrderToCash.Notifications.dll`:

```
--- AUTH-REQUIRED real Mailpit (--smtp-auth-file) (localhost:12625) ---
    capabilities: Size, EnhancedStatusCodes, Authentication, UTF8 | mechanisms: [LOGIN,PLAIN] | IsAuthenticated=False
    EXC TYPE  : MailKit.ServiceNotAuthenticatedException
    BASE CHAIN: ServiceNotAuthenticatedException -> InvalidOperationException -> SystemException -> Exception
    MESSAGE   : 5.7.0 Authentication required
    is SmtpCommandException: False
    >>> REAL SendFailureClassifier.Classify -> Permanent

--- AUTH-ADVERTISED-ONLY real Mailpit (--smtp-auth-accept-any) (localhost:12626) ---
    capabilities: Size, EnhancedStatusCodes, Authentication, UTF8 | mechanisms: [LOGIN,PLAIN] | IsAuthenticated=False
    RESULT: send SUCCEEDED (no exception)
```

This is the same shape I used to raise D1, with the one line that mattered inverted: `Classify -> Permanent` where round 1 read `Classify -> Transient`. The negative control holds in the direction the corrected ledger row now claims — AUTH advertised but not required lets the unauthenticated send through — so the row's "property of the SERVER'S configuration, never of this transport's code" is supported by evidence run both ways, which is what the two-direction rule asks for.

The fix is also the right shape: a **second, independent** branch (`SendFailureClassifier.cs:79-82`), not a widening of the `SmtpCommandException` test, so the 5xx rule and the auth rule can fail independently.

## P2 — D2's ported guard asserts the fallback's OWN line

Read rather than assumed (`NotificationDegradesOnPermanentFailureTests.cs:236-260`): `WaitForConsoleAdapterLogLineAsync` selects on `ConsoleNotificationSender`'s own message prefix `"notification (console adapter):"` **and** the same `correlationId` scope, then returns that line's `MessageId` and `TraceId`, which the test asserts at `:110-111` and `:123-125`. It is the fallback's own record, keyed to this fact — not "a line appeared". The bullet-4 enumeration now carries the missing file with all three assertions classified (`:171-179`), and case 3 is marked Ported against this test.

## My mutations — five, all families, each restored and re-run

Protocol each time: `cp` backup → mutate → `dotnet build --no-incremental` → one named test → verbatim failure → restore from my backup → `cmp` → `touch` + forced rebuild → confirming green. `pgrep -a dotnet | grep -E " (build|test|format)( |$)"` was empty before each; no two builds ever overlapped.

| # | Family | Mutation | Named test | Verbatim failure |
|---|---|---|---|---|
| M4 | substitution (valid sibling **type**) | `error is ServiceNotAuthenticatedException` → `error is System.Security.Authentication.AuthenticationException` — a real, plausible alternative auth type | `SendFailureClassifierTests.Classify_AServiceNotAuthenticatedException_IsPermanent` | `Expected: Permanent / Actual: Transient` |
| M5 | substitution (valid sibling **sender**) | my round-1 M3 again: `DegradingNotificationSender(consoleFallback, smtpSender, …)` | `NotificationSenderBindingTests.SenderKindSmtp_ResolvesADegradingNotificationSenderWhoseInnerIsTheMailKitSender` | `Expected: typeof(…MailKitNotificationSender) / Actual: typeof(…ConsoleNotificationSender)` |
| M6 | substitution (the **other** binding) | wrapped the Console-only binding too — the thing the wiring comment forbids | `NotificationSenderBindingTests.SenderKindConsole_ResolvesAnUnwrappedConsoleNotificationSender` | `Expected: typeof(…ConsoleNotificationSender) / Actual: typeof(…DegradingNotificationSender)` |
| M7 | corruption, **integration** | rendered the fallback OUTSIDE the ambient activity (`Activity.Current = null` around the fallback call) — both lines still emitted, console line loses its trace | `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_DegradesToConsole_…` | `Assert.False() Failure / Expected: False / Actual: True` at `NotificationDegradesOnPermanentFailureTests.cs:line 123` |

M7 is the probe that answers the question round 1 left open — whether A1's identity assertion buys anything over presence. It does: line **123** is `Assert.False(string.IsNullOrEmpty(consoleLine.Value.TraceId))`, the **console-adapter** line's trace guard added this round, while line 121 (the degraded line's, which existed before) **passed**. The mutation was surgical, and only the new guard caught it. Restored, rebuilt and re-run: `Passed! - Failed: 0, Passed: 1, Total: 1` (27 s). M4/M5/M6 restored likewise, and the confirming full unit run is `Passed! - Failed: 0, Passed: 107, Total: 107`.

M6 is mine rather than the implementer's, and it closes the half of A2 I flagged as unguarded: the "a console-only binding stays unwrapped" clause of acceptance bullet 1 now has a case that fails when it is violated.

## Round-1 findings, one by one

| Finding | Status | How I checked it |
|---|---|---|
| **D1** — auth-required rejection classified Transient | **Closed** | my own two-direction real-wire re-derivation (P1) + M4 |
| **D2** — missing spec file, dropped fallback-line guard | **Closed** | enumeration re-derived in the record with my own command and reconciled to 3 hits; guard read at source (P2) + M7 |
| **A1** — traceId presence, not identity | **Closed** | `:121-125` asserts 32-hex on both lines and equality; M7 shows the console-line guard fails |
| **A2** — bindings indistinguishable | **Closed** | `NotificationSenderBindingTests`, both cases; M5 and M6 fail it from both directions |
| **A3** — `MessageId` unguarded | **Closed** | unit `:100` asserts it; integration asserts it on both the degraded and the console line against the real `{eventId}@order-to-cash` |
| **A4** — ledger citation pointed at prose | **Closed** | row now cites `send-failure-classifier.ts:58-64` and `:67-69`; both ranges are the implementation, and I had already read them |
| **A5** — no effort record | **Open — the leader's**, and it gates closing |
| round-1 note: "36 members" | **Corrected** to 31, matching my own enumeration |

## New findings this round

- **A6 (advisory) — an arming failure message that does not name what broke.** M7's message is bare: `Assert.False() Failure / Expected: False / Actual: True`, with only the stack-trace line identifying it; the record's own arm 3 has the identical shape. The repository's rule is that a failure counts when its message names the claim, and `Assert.False(string.IsNullOrEmpty(x))` structurally cannot. `Assert.Matches("^[0-9a-f]{32}$", …)` on the same value one line later does name it (it prints the offending value), so reordering the two lines — or dropping the `IsNullOrEmpty` guard, which `Assert.Matches` subsumes for null/empty — would make the console-line trace guard self-describing at no cost. Not blocking: the assertion works, and the evidence is unambiguous once the line number is read.
- **A7 (advisory, and I judged it rather than waving it through) — `InternalsVisibleTo.cs` plus `internal INotificationSender Inner` is accepted.** It is the established per-service seam, not a new precedent: `src/{Cqrs,Billing,Gateway,Fulfillment}/InternalsVisibleTo.cs` already grant the same access to their own UnitTests assemblies, and the Notifications file grants exactly one assembly. Exactly one member is exposed (`grep "^\s*internal "` across `src/Notifications` returns the single `Inner`). It earns its keep: the property is what lets a test assert **which** real sender a binding resolved, which is the defect M5 reproduces, and asserting the wrapper's type alone would not catch it. It is also not test-only scaffolding — production `SendAsync` reads through it (`DegradingNotificationSender.cs:71`), so there is no unused accessor to rot. No architecture test governs internals visibility. The residual worth stating: the decorator's surface is now wider for one assembly, and if a future test reaches for `Inner` to *drive* behaviour rather than to *identify* a binding, that would be the line worth defending.

## CHECKPOINTS re-walked (deltas only)

**C2:** [x] still exactly one `in_progress` (id 73); my probes changed nothing in the backlog.
**C3:** [x] the new `InternalsVisibleTo.cs` adds no package, no cross-service access and no `Domain/` reference; architecture suite green inside the leader's post-fix full run.
**C4:** [x] `./quality.sh` green on the final tree — the leader's run, whose counts I re-summed myself (18 projects, 1877, 0 failed); [x] integration coverage now includes two more real-container cases, and the new `MailpitAuthContainerFixture` is a genuine Testcontainers fixture injecting its auth file via `WithResourceMapping` (no host-file dependency); [ ] the coverage-gate line remains unlocated by me, unchanged from round 1 and from the id-62 review.
**C5:** [ ] **`progress/history.md` effort record still missing (A5)** — the one box blocking close; [x] `feature_list.json` untouched by me; [x] no commit by me; [x] no stray files in the repository (my scratch, probe project, containers and logs all live in the session scratchpad, and both containers were removed — `docker ps | grep rev73` returns nothing).
**C7:** [x] `specs/shared/` still untouched (`git diff --stat -- specs/shared/` empty); [x] no amendment implicated; [x] the ported-guard gap that failed C7's spirit in round 1 is closed, and the enumeration now matches a content-based search rather than a filename one.

## Tree state at close

`cmp` against my round-2 backups: `SendFailureClassifier.cs`, `NotificationsServiceCollectionExtensions.cs` and `DegradingNotificationSender.cs` all **IDENTICAL**. `git status --porcelain | grep -ic notification` → **15**, the same set the fix round left, and `git diff --stat` over the Notifications source and tests is unchanged (`NotificationsServiceCollectionExtensions.cs` +20/-1, `FakeNotificationSender.cs` +11). Nothing of mine is alive (`pgrep` empty) and no reviewer container remains. As in round 1, the three files I mutated now carry **newer mtimes** than the full-run log purely from the restore-and-rebuild cycle; their contents are byte-identical, which the `cmp` results above establish — a "nothing newer than the log" check should be read with that in mind.
