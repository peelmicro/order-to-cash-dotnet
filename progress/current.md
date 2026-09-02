# Current session

**Feature:** `cqrs_dispatcher` (id 43, phase 8) — `sdd: false`
**Status:** in_progress
**Session started:** 2026-09-02

## Goal

The hand-rolled in-process CQRS dispatcher: `ICommandHandler<T>` / `ICommandHandler<T,R>` / `IQueryHandler<T,R>` / `IEventHandler<T>`, assembly-scan registration, and a **startup validation pass that fails the boot** when a command has zero handlers or more than one. Roughly 150 lines, written once here and used by every later service.

## Decisions taken this session

- **Binding across all six services** (human gate, this phase), matching #7's own gate ruling on `@nestjs/cqrs` at its feature 16. It does not fit all six equally — the Gateway's commands are outward RPC, and Notifications and Projector are pure consumers — but a #8 that used its dispatcher in three would turn an architecture difference into what looks like a language difference in the benchmark.

## Blockers

None.

## Notes

- **This feature has no #7 counterpart.** #7 got its command bus free from `@nestjs/cqrs`; MediatR v13 is commercially licensed, so #8 writes ~150 lines instead. In the benchmark it is a row **without a baseline**, not a row that came out slower — `feature_list.json` carries that in the feature's own `note` so the Phase 24 table cannot silently gain a comparison that does not exist.
- **The startup validation is the part that matters.** It is the .NET stand-in for the lesson #7 paid for with its DI-metadata divergence: a DI failure must be loud at boot, not a surprise at first use. A dispatcher that resolves handlers lazily and throws on the first command is strictly worse than one that refuses to start.
- Queued for features 14/15 from feature 13's review: `Rehydrate`'s two unguarded validations (O1, O2 — they survive their own deletion), `CancellationReason` assigned after `TransitionTo` rather than inside it, and a business error code currently used for a load-time corruption.

---

## Template (reset to this on session close)

```markdown
# Current session

**Feature:** `<name>` (id <n>, phase <n>)
**Status:** <status>
**Session started:** <date>

## Goal

## Decisions taken this session

## Blockers

## Notes
```
