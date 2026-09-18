# Correction — `specs/shared/test-matrix.md`'s Coverage summary was stale (phase 25 final checkpoint)

## Classification and why it is legitimately FULL despite being text-only

`specs/shared/` sits in CLAUDE.md's cost-discipline FULL group unconditionally,
so this pass was run at that weight (Opus, own session, defeat-list discipline
on the countable claims below).

**But this is not a spec amendment.** `test-matrix.md`'s own header ("What is
reused verbatim, and what is not") states explicitly that columns 1–4 of every
row — the id, the requirement, the test level, the stack-neutral sketch — plus
the rules, conventions and summary *structure* are the shared contract #7/#8/#9
take unchanged. Column 5 (**Status**) and the derived Coverage-summary counts
are named, in the same paragraph, as **"each assessment's own realisation
record."** This edit touched only Status cells (`R48`, `R56`), the Coverage
summary's derived counts, and the two prose paragraphs directly under it that
narrate the current counts — all three are explicitly the per-assessment class
the header describes. No cell in columns 1–4, no rule, no level table and no
amendment note changed. It is the same class of edit feature 72 made earlier
in this project for an identical defect
(`progress/review_test_matrix_rows_that_outlived_their_named_closer.md`), not
an `SA-n` proposal, and I am not raising one.

## What I independently verified vs. what I accepted from the brief

Independently re-run/re-derived in this session, not copied from the brief:

- `feature_list.json` ids 28 and 31 are `done`:
  `python3 -c "import json;d=json.load(open('feature_list.json'));print([(i,next(f for f in d['features'] if f['id']==i)['status']) for i in [28,31]])"`
  → `[(28, 'done'), (31, 'done')]`.
- **Re-ran `BlackBoxApiTests` myself** (chose to re-run rather than only accept
  the leader's citation, given the FULL classification and that this evidence
  is load-bearing for flipping a Status cell): `dotnet test
  tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --filter
  "FullyQualifiedName~BlackBoxApiTests"` → `Passed! - Failed: 0, Passed: 8,
  Skipped: 0, Total: 8, Duration: 6 s`. Read the R48 case itself
  (`tests/Gateway.IntegrationTests/BlackBoxApiTests.cs:274-324`,
  `DuplicatePaymentReference_YieldsExactlyOnePayment_InBillingsOwnDatabase`)
  to confirm what it actually asserts (201 `accepted` → 200 `duplicate` with
  `Idempotent-Replay: true`, then Billing's own `payments` table count == 1
  for that reference) rather than trusting the brief's summary of it.
- `grep -n Skip tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs`
  → no match; the `Criterion5_R56_OneTraceIdSpansTheComposedRealStack` case
  (line 422) carries `[Fact(Timeout = 180_000)]`, not `[Fact(Skip = ...)]`.
- `grep -n "id 28\|Id 28\|saga_e2e_verification" progress/history.md` → no
  output, confirming the brief's premise-check correction (the primary record
  is `progress/impl_id28_saga_e2e_verification.md`, not `progress/history.md`).
- Read `progress/impl_id28_saga_e2e_verification.md:142-209` directly: the
  criterion was genuinely `[Fact(Skip = "…")]` on first implementation, found
  three different `trace_parent` values for one order
  (`orders=00-432509b7…`, `fulfillment=00-949abcf3…`, `billing=00-81271d40…`),
  root-caused to `SagaCommandDispatchWorker`'s channel handoff losing
  `Activity.Current`.
- `git show --stat b144788` — commit exists, message reads "feat(gateway):
  end-to-end saga verification against real infrastructure, and fix a
  trace-context gap it found", and states "counted: all 5 criteria now pass,
  confirmed on two consecutive full runs." Combined with the live absence of
  `[Skip]` today, this closes the loop: skipped → fixed same commit → unskipped
  → still unskipped now.
- Read `progress/history.md` around line 3191 (the "Id 35" entry,
  `observability_dashboards`, phase 22, 2026-09-18): a real order placed
  through the fully composed, live stack, one shared trace id across all six
  services confirmed directly via Jaeger's `/api/traces/<id>` API (42 spans,
  depth 26) — an independent, post-fix, live re-confirmation of the exact
  property `R56` asserts, by an unrelated feature.
- Recomputed the Coverage-summary arithmetic against every row myself by
  re-reading the whole document (all 8 feature sections, §8.1, and the
  Verification section) rather than trusting the brief's subtraction: every
  row from `R1` to `R63` reads `DONE` in its own Status cell after my two
  edits. Feature 3 (`R19`–`R29`, 11 rows): all 11 `DONE` → 11/0/0. Feature 6
  (`R45`–`R49`, 5 rows): all 5 `DONE` → 5/0/0. Feature 8 (`R56`–`R60`, `R62`,
  6 rows): all 6 `DONE` → 6/0/0. Total: 63/0/0. This matches the brief's
  arithmetic; I verified it row-by-row rather than accepting it.

Accepted from the brief without a fresh independent re-derivation (stated
explicitly, per the brief's own instruction not to fabricate what I didn't
do): the exact line numbers cited for the summary table and prose paragraphs
(I located and edited them directly by content match, not by trusting the
line numbers); the characterisation of `R24`'s and `R49`'s own row text as
already correctly `DONE` — I did independently re-read both rows in full and
confirm they say `DONE` with no `pending`/`SCOPED` language, so this is
verified, not merely accepted.

## Exact before/after

**`R48` (`specs/shared/test-matrix.md`, `billing_invoicing` section).**
Before: `SCOPED` — the Gateway endpoint exists but the row named the four
`InvoicesHttpTests` stub cases as the reason it fell short of the sketch's API
level, with closer "feature 31 `api_tests` (phase 18, still `pending`)".
After: `DONE` — cites `BlackBoxApiTests.cs` ›
`DuplicatePaymentReference_YieldsExactlyOnePayment_InBillingsOwnDatabase`
directly, states what it proves against Billing's own `payments` table, cites
the live 8/8 re-run, and keeps the existing unit/integration evidence
unchanged beneath it as before.

**`R56` (`observability_reliability` section).** Before: "MECHANISM leg DONE,
composed-stack leg unproven", closer "feature `saga_e2e_verification` (id 28,
still `pending`)". After: `DONE` for both legs — cites
`SagaEndToEndVerificationTests.cs` › `Criterion5_R56_OneTraceIdSpansTheComposedRealStack`,
narrates the original skip and the same-commit fix (`b144788`), the live
absence of `[Skip]` today, and the independent post-fix Jaeger
re-confirmation from the "Id 35" `progress/history.md` entry.

**Coverage summary table.**

| Feature | Before (Green/Scoped/Not-yet-green) | After |
|---|---|---|
| 3. `order_saga_orchestrator` | 10/1/0 | 11/0/0 |
| 6. `billing_invoicing` | 3/2/0 | 5/0/0 |
| 8. `observability_reliability` | 5/1/0 | 6/0/0 |
| **Total** | **59/4/0** | **63/0/0** |

**"Scoped rows, and what closing them would take" paragraph.** Rewritten to
state plainly that no row is scoped and none is not-yet-green, name the four
former rows (`R24`, `R48`, `R49`, `R56`) and how each actually closed, and
disclose that `R48` specifically was the one left stale by oversight after its
own closer shipped (the coverage table for `R24`/`R49` had already been
updated correctly at the time; `R48`'s row text simply wasn't).

**Historical paragraph beneath it** ("This paragraph replaces the prior
state…") kept verbatim as instructed, with one sentence appended closing the
loop it describes, rather than rewritten.

## Reasoning on `R56`

I judged the existing evidence chain sufficient to flip the row to `DONE`
without performing a fresh live composed-stack Jaeger observation myself, for
four reasons taken together: (1) the composed-stack test itself is shipped,
unskipped and committed green, with its own commit message asserting two
consecutive full green runs; (2) the original skip was not a placeholder —
it was a genuine, documented finding of a real production defect, fixed in
the same commit, which is stronger evidence than an untested claim would be;
(3) an unrelated, later feature (id 35) independently re-confirmed the exact
property this row asserts on a live, fully composed stack, via Jaeger's own
API rather than its UI, after the fix landed — this is closer to the row's
own prescribed mechanism ("a real Jaeger") than the shipped test itself uses
(which reads `trace_parent` columns rather than querying Jaeger, a gap its
own implementation report discloses at
`progress/impl_id28_saga_e2e_verification.md:201-209`); and (4) running a
fresh live Jaeger observation myself would mean standing up the full
composed stack for a text-correction pass whose own classification note in
the brief says is "not a spec amendment" — disproportionate to what remains
in question. I recorded this reasoning in the row itself and did not
fabricate a live run I did not perform.

## What I found in #7's copy — untouched, and why

Read `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/specs/shared/test-matrix.md`
directly (not assumed): `R24`, `R48` and `R49` already read `DONE` there, with
no stale "still pending" language. `R56` in #7's copy is `SCOPED — RATIFIED`,
but for a **different, still-genuine** reason: #7's composed-stack leg is
itself green; what #7 defers is the **e2e leg** (driven through the browser),
consciously ratified at #7's own Phase 25 traceability-closure gate, with the
three specific exclusions (`the inbound-request leg`, `the Projector`, `no
span at consumption`) stated and what closing each would take. #7 does not
carry #8's defect (a stale summary describing an already-shipped-and-fixed
composed-stack leg as unproven) — #7's row is internally consistent with its
own summary. I did not touch #7's repository. This is not a case of "the same
per-assessment convention independently carrying the same defect" — it
independently does not.

## Rules followed

Touched only `specs/shared/test-matrix.md`: the `R48` and `R56` Status cells,
the Coverage summary table's three affected feature rows and the Total row,
and the two prose paragraphs beneath it. No column 1–4 text, no rule, no Test
levels table, no path convention, and no other file in `src/`, `tests/`,
`apps/web/` or `feature_list.json` was touched by me. `git diff --stat` on
`specs/shared/test-matrix.md` shows `8 insertions(+), 8 deletions(-)` — one
line per touched cell/paragraph, confirmed by reading the diff directly. No
git command that writes the index or working tree was run in either
repository; no commit was made.

**Note on unrelated pre-existing repository state**: at the time of this
session, `git status` also showed `feature_list.json` and
`src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` as modified,
neither of which I touched and neither of which appeared in the
conversation's initial git-status snapshot. This is pre-existing/concurrent
repository state, not something this pass introduced — flagging it for the
maintainer's awareness rather than silently working around it.

## Traceability

No `R<n>` test-matrix row itself changed level, file or case name — this pass
corrected the recorded Status of existing, already-named tests, not the
tests themselves. No `test-matrix.md` row needs a further row-of-its-own
update as a result (this file *is* the test matrix).

## Result

PASS — `R48` and `R56` corrected from `SCOPED`/unproven to `DONE` in
`specs/shared/test-matrix.md`, the Coverage summary table and its two prose
paragraphs brought back into agreement with the rows (Total 63/0/0), all
claims independently re-verified live in this session (feature statuses,
`BlackBoxApiTests` 8/8, the `SagaEndToEndVerificationTests` unskipped state,
commit `b144788`, the "Id 35" Jaeger re-confirmation), and #7's copy checked
and left untouched because it does not carry the same defect.
