# Phase 15 brief — written at the close of phase 14, 2026-09-14

## What phase 15 is

**Id 28 — end-to-end saga verification before any frontend work — is the phase.** Ids 93, 94 and 95 are its inherited backlog: three findings that surfaced *while finishing phase 14* and were deliberately routed here rather than into the phase being closed.

| Id | What it is | Leader's sizing |
|---|---|---|
| 28 | End-to-end saga verification | The phase's actual feature |
| 95 | A cold NATS connection loses a reply in `NatsStockAvailabilityCheckerTests` — the same shape id 81 diagnosed in the Gateway, with the one-line fix already written down | **Small.** Port a known fix to a known site |
| 94 | `SagaConsumptionTests.SO9`'s recorded arm no longer kills | **Small-medium.** Mostly a decision: strengthen the assertion, or rename the test to say what it actually proves |
| 93 | `orders.*`/`catalog.*` RPC payloads still defined twice | **Medium, and partly a DESIGN decision** — whether `src/Contracts` should own them. Not the leader's call |

## Start here, before anything else

**Read `progress/phase14_finishing_plan.md` in full.** It is the record of how a phase that had run away was brought to a close, and its stopping rule is the thing phase 15 should copy rather than rediscover.

## The three rules phase 14 paid for, in the order they cost the most

**1. Write the stopping rule before the phase starts.** Phase 14 closed nine features and filed ten new entries; the backlog grew from 77 to 89 while the work itself went well. It ended only because the maintainer stopped it. The finish worked because the list was **frozen at 13 in writing first**, and completion became a command anyone can run rather than a claim anyone makes:

```
python3 -c "import json;d=json.load(open('feature_list.json'));print([f['id'] for f in d['features'] if f.get('phase')==15 and f['status']!='done'])"
```

**Decide phase 15's list now, and put findings that arrive later into phase 16.** Three went to phase 15 from phase 14 without the phase-14 count moving once.

**2. Premise-check every brief before dispatching it, and every recommendation before the maintainer acts on it.** `.claude/agents/premise_checker.md`. Its phase-14 record: 2 false claims in the finishing plan, 9 in one brief, 9 in another — including telling an implementer that correct line citations were stale. A wrong brief cost 140k–370k tokens; the check costs a fraction of one. **Run it; do not deliberate over whether this one is worth it.**

**3. A brief must not paraphrase acceptance bullets.** Six of one brief's nine false claims were the leader silently narrowing what an entry required. Briefs then named the entry, said *"the bullet wins and you report the discrepancy"*, and flagged only what is easy to skim past — and the class did not recur. Four consecutive briefs cleared on their first version afterwards.

## The bright line that sorted every leader error in phase 14, in both directions

**Did I produce this fact with a command, in this session?** If yes, state it and carry the command. If no, it is a question for the subagent, never an assertion.

Every claim the leader got right came from a command it ran. Every claim it got wrong was copied from prose or inferred — *"neither repository has X"* when one did, a wrong test harness recommended, a 24-file feature sized as a "small alignment", twelve rows read off a truncated `head` and called six.

**And the corollary phase 14 added, which the premise check does NOT catch: a verified premise is not a verified inference drawn from it.** The check confirmed two definitions of a record existed — true — while the leader's inference, that a batch would *delete* one, was false. It confirmed a line's content — true — while the inference that the citation had drifted was false. Check the step you are actually relying on.

## What to expect from the entries themselves

**An entry's own premise may be wrong, and finding that out is the work.** Id 81 blamed a 2 000 ms budget; the cause was a lazily-connecting client, provable because one loss happened under a **120 000 ms** budget on an idle machine. Id 90 said `<= 0` produced its failure mode; only `0` does. Both entries were corrected in the source rather than satisfied as written.

**An entry names where a class was noticed, never where it ends.** Id 74's positional-read class did not stop at its three named tests; id 86's "delete two false absolutes" reached three copies, and a fourth sat in `progress/current.md`. Enumerate on the wording of the claim being **retired** — a grep for the new claim structurally cannot find text asserting the old one.

**"Already correct" and "closed as a documented decision" are legitimate outcomes** where a bullet allows them, and cheaper than building what an entry does not require.

## Ground state at the close of phase 14

- `./quality.sh`: 18 projects, **2 042 passed, 0 failed, 0 skipped, 0 build warnings**. `./init.sh` exits 0.
- Backlog **78 of 94** done. Phase 14: **13 of 13**.
- #8 pushed at `1ed82a1`; #7 pushed and clean at `c20bdc0`.
- `Directory.Build.props` now has `GenerateDocumentationFile=true` with only `CS1591` suppressed, under `TreatWarningsAsErrors` — **a broken `<see cref>` now fails the build.** New as of id 78 and easy to trip over.
- `specs/shared/` is byte-identical to #7 across six files; `test-matrix.md`'s Status column is the one exempt surface.
