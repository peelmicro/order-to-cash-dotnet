# Id 111 `outbox_relay_poison_payload_retry_loop_unverified` — disposition record (phase 25, LIGHT, leader-direct, read-only investigation)

## What was checked

Read `../order-to-cash-nestjs/apps/orders/src/infrastructure/outbox/outbox-relay.service.ts` (the caller of `runOnce()` that id 52's audit did not reach within its time-box) and `outbox-relay.ts`'s publish-failure catch block, then compared directly against `src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs`.

## Finding: not parity — an asymmetry, and it runs in #8's favour

- **#7's poll loop has no catch around `runOnce()`, only a `finally`** (`outbox-relay.service.ts:63-72`, `runCycle()`): `try { await this.relay.runOnce(); } finally { ...reschedule... }`. `runCycle()` itself is invoked from a `setTimeout` callback without being awaited or `.catch()`'d (`scheduleNext`, line 59: `this.inFlight = this.runCycle();`). Any exception `runOnce()` throws that isn't already handled inside `outbox-relay.ts`'s own publish-failure catch — including a payload-decode failure in the `claimed.map(...)` step, which sits **outside** that inner try block (`outbox-relay.ts:145-157` vs. the try at `:158`) — becomes an **unhandled promise rejection**. No `unhandledRejection` handler exists anywhere in `apps/orders/src/main.ts` or any other `apps/*/src/main.ts` (`grep -rn "uncaughtException\|unhandledRejection"` — zero hits). Node's default behaviour for an unhandled rejection is to terminate the process. So #7's actual exposure to a corrupted `outbox.payload` row is not "retries forever" — it is **the whole Orders service process crashes on the first occurrence**, and would crash again immediately after any process-manager restart, since the poison row is still there and still first in line.
- **#8's poll loop (`OutboxRelayBackgroundService.cs:38-47`) has a real `catch (Exception ex) when (ex is not OperationCanceledException)`** around the entire `RunOnceAsync` call, logging and continuing to the next tick. #8 does not crash on a `runOnce` failure of any kind, including a payload decode failure. #8's actual exposure is the one id 52's audit already named: the same poison row would be reclaimed and retried every poll interval, indefinitely — paced, not tight-looping, and never taking the process down.

**Neither repository implements a poison-row circuit-breaker or dead-letter path for a corrupted relational `outbox` row** (distinct from the Kafka consumer DLQ, which is a different mechanism for a different queue). That gap is real and shared. But the comparison the entry asked for — does #8 lack a property #7 supplies — is answered **no**: #8 already has the safer behaviour of the two (bounded, paced, non-crashing retry vs. #7's crash-on-first-hit with no retry pacing at all).

## Disposition

`done`, **ACCEPTED, NOT FIXED**. Building a poison-row circuit-breaker for #8 alone, this late in the trilogy, for a gap #7 (the completed baseline) also has and never addressed, is out of proportion to the finding — #8 is not behind #7 here, it is ahead of it. Not filed as a spec amendment (this is an implementation-robustness gap, not something `specs/shared/` prescribes either way) and not fixed in #7 (touched only for spec amendments or on explicit request, per `CLAUDE.md`).

**Re-open trigger:** if a corrupted `outbox.payload` row is ever observed in a real environment (not merely hypothesised), reopen this entry to build the circuit-breaker for real, informed by the actual corruption shape observed rather than a guessed one.
