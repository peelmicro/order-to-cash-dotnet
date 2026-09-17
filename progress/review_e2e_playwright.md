# Review — `e2e_playwright` (id 32, phase 19, `sdd: false`)

**Verdict: APPROVED, with two corrections that must be discharged before the commit (C1, C2 below).**

Reviewer: `reviewer` subagent (Opus), 2026-09-17. Both acceptance bullets are met and I verified each one myself, against a stack I started and stopped, with three mutation probes of my own design that are not the implementer's. The defects I found are in the *record* — a ported-idiom ledger row whose "#7 relied on X" half is false when read against #7's checkout, and a missing ledger row — not in the behaviour. Neither hides a missing property: I checked the one that could have, and it is supplied.

## 1. Acceptance bullets, read verbatim from `feature_list.json`

| Bullet | Where it is proven | Verified by me |
|---|---|---|
| "order reaches completed in the UI" | `apps/web/e2e/happy-path.spec.ts:122` — `getByTestId('order-detail-status')` `toHaveText('completed')`, reached through a real login, a real placed order, a real payment registered through the billing UI | Yes — suite green in my own run, plus probe A below |
| ".99 order reaches cancelled with compensation visible" | `apps/web/e2e/compensation.spec.ts:70` (`cancelled`), `:71` (`credit_rejected`), `:104-106` (release before cancel), `:111-115` (the rendered causal edge) | Yes — probes B and C below |

## 2. What I ran (independent; I did not re-run the implementer's four green runs)

Nothing of the stack was listening beforehand: `docker ps` empty, no `dotnet run`/`next start` process, none of 3010/3001/1433/9092/4222/27017 held.

1. `scripts/dev-stack.sh start` — infra, solution build, seed, six services, `next build`/`next start` on 3010. Exit 0, all six `[OK]`, Gateway `/health/ready` answered. (The implementer's inotify workaround was exported but proved unnecessary on this run — all six started.)
2. `node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test` — **3 passed (8.2s)**. `@playwright/test` resolves to **1.62.1**.
3. `node --run test` in `apps/web` — **22 files, 286 passed**, matching the 286 baseline in `progress/current.md`. This is the surface the `package.json` edit touches.
4. Three arming probes (§3), each a single-spec run.
5. A throwaway live-DOM probe against the running app (written to the session scratchpad, never into the repository) for the structural-locator question (§4).
6. `./init.sh` — exit 0.

`./quality.sh` was **not** re-run. This feature changes no `src/`, no `tests/`, no `.csproj`; it adds three `.ts` files under `apps/web/e2e/`, one config, one devDependency and one npm script. Rows 2 and 3 cover that surface exactly. The .NET side is inherited from phase 18's wrap-up (`quality.sh` exit 0, .NET 2126) and is untouched here — stated so a reader can tell verification from assumption. A full `quality.sh` could not have been run while the stack was up in any case (a running service holds its build output open).

Teardown confirmed: `scripts/dev-stack.sh stop` reported "nothing left running, no port this stack bound is still held"; `docker compose -f docker-compose.infra.yml down` removed every container and the network; afterwards `docker ps` empty, `pgrep` no matches, no stack port held. The working tree after my review is byte-for-byte the tree I was given.

## 3. Do the assertions bite? Three probes, mine, not the implementer's

I deliberately did not repeat the implementer's two arms. Two of my three probes attack a **different mutation family** than theirs: they mutated the *expected value* and the *operator*; I mutated the *input the assertion reads* and *substituted sibling identifiers*.

**Probe A — the happy path's terminal claim, sibling-value substitution.** `happy-path.spec.ts:122`, `'completed'` → `'paid'` (the exact state #7's page got stuck on, per #7's own D1). Backup taken with `cp`, one spec run, restored, `cmp` identical.

Result: **passed**. Read correctly, this is not a defect and not a vacuous assertion — it shows the order badge genuinely *passes through* `paid` on its way to `completed`, so line 122 is doing real waiting work rather than reading an already-settled value. Combined with the implementer's own arm (expect `invoiced` → failed, `62 × locator resolved to ... completed`), the picture is complete: the page moves `invoiced` → … → `paid` → `completed`, and only `completed` satisfies the shipped assertion. A saga that stalled anywhere short of `completed` fails line 122 at 30 s.

**Probe B — the compensation ordering claim, corrupting the input rather than the operator.** I left the assertion at `:106` exactly as shipped and instead made `readEventTypes` return the rendered order **reversed** (`.reverse()` on the `evaluateAll` result) — i.e. I simulated the A1 render-order inversion the assertion exists to catch, and let the unchanged assertion judge it.

```
Error: stock.released.v1 must appear BEFORE order.cancelled.v1 in the rendered timeline

expect(received).toBeLessThan(expected)

Expected: < 0
Received:   1

  106 |   expect(stockReleasedIndex, 'stock.released.v1 must appear BEFORE order.cancelled.v1 in the rendered timeline').toBeLessThan(orderCancelledIndex);
```

The shipped assertion, unmodified, failed on an inverted timeline and named its own claim in the failure. Note the `expect.poll(...).toEqual(arrayContaining([...]))` at `:96-98` still passed on the reversed input — correctly, since `arrayContaining` is order-insensitive — so the failure landed precisely on the ordering premise and nowhere else. This is defeat-list row 9 (satisfy the closer half, leave the premise stale) closed properly. Restored, `cmp` identical.

**Probe C — the causal-link text, sibling-identifier substitution.** `:115`, expected `'stock.released.v1'` → `'stock.reserved.v1'` (a real sibling event type, one letter apart).

```
Error: expect(locator).toHaveText(expected) failed

Locator:  getByTestId('timeline-entry').nth(4).getByTestId('timeline-causation-link')
Expected: "stock.reserved.v1"
Received: "stock.released.v1"
  63 × locator resolved to <a data-testid="timeline-causation-link" href="#timeline-entry-9705a262-...">stock.released.v1</a>
```

The assertion reads a real rendered anchor whose `href` points at the cause's own entry id. It is not comparing a literal to a literal, and it tells the two sibling event types apart. Restored, `cmp` identical.

## 4. The structural locator — checked against the live DOM, not by reading

The implementer routes around a found frontend defect with `getByTestId('order-line').locator('select')`. I queried the running page four times:

```
load 1: {"orderLineCount":1,"selectsPerLine":[1],"selectIds":[["product-2"]],
         "labelFors":[["Product->product-7", ...]], "selectsOnPage":4,
         "selectIdsOnPage":["retailer","company","currency","product-2"]}
load 2: ... "Product->product-8"  ... selectIds [["product-2"]]
load 3: ... "Product->product-9"  ... selectIds [["product-2"]]
load 4: ... "Product->product-10" ... selectIds [["product-2"]]
```

- **Unambiguous:** exactly one `<select>` per `order-line`; four on the page, of which three (`retailer`, `company`, `currency`) are outside `order-line`. `data-testid="order-line"` occurs at exactly one site in the source (`place-order-form.tsx:267`).
- **It cannot silently match the wrong select.** I clicked "Add line" and re-ran the locator: `selectOption` failed loudly with `strict mode violation: getByTestId('order-line').locator('select') resolved to 2 elements` (`#product-2` and `#product-3`). If the form's structure changes, this locator fails; it does not drift onto the wrong control.
- **The defect behind backlog 106 is real and I reproduced it independently.** The label's `htmlFor` increments on every SSR render (`product-7`, `-8`, `-9`, `-10` across four hard loads) while the client-mounted `<select>` keeps `id="product-2"` — a free-running module-scope counter, exactly as reported. Playwright's own strict-mode dump even labels the *second* line's select "aka `getByLabel('Product')`", which is the practical consequence.

## 5. Ported-idiom ledger — checked row by row against #7's checkout

| Row | Claim | Verdict |
|---|---|---|
| 1 | #7 drove a custom dropdown via `...-trigger` + `getByRole('option')`, `happy-path.spec.ts:25-30` | **True.** The cited range is off by three lines (the trigger clicks are at `:28-33`; `:25-27` is the comment above them), but the idiom and the file are right, and #8's native-`<select>`/`selectOption` replacement is real. |
| 2 | #7 hopped through an "order list" link; #8 links straight to the order | **True, both halves.** #7 `apps/web/e2e/happy-path.spec.ts:54` — `page.getByRole('link', { name: 'order list' }).click()`. #8 `place-order-form.tsx:365` — `data-testid="accepted-order-link"` to `/orders/${accepted.orderId}`. |
| 3 | #7 used `dotenv-cli` (`apps/web/package.json:17`); #8 uses Node's `--env-file-if-exists` | **True, exactly as cited.** #7's line 17 is `"test:e2e": "dotenv -e ../../.env -- playwright test"`, and `dotenv-cli: catalog:` is at `:44`. |
| 4 | "#7 relied on `invoiceRow.getByTestId('payment-form')` — Nuxt's billing page nests the payment form inside the invoice row it belongs to" | **FALSE, both halves — see C1.** |

**C1 (correction required before the commit) — ledger row 4 is wrong, and the same false claim is baked into a shipped source comment.**

- #7's test does **not** scope the payment form to the row. `../order-to-cash-nestjs/apps/web/e2e/happy-path.spec.ts:74` reads `const paymentForm = page.getByTestId('payment-form');` — page-level, character-for-character what #8 does.
- #7's markup does **not** nest the payment form inside the invoice row either. `../order-to-cash-nestjs/apps/web/app/pages/billing/index.vue:357` puts `data-testid="payment-form"` inside a **sibling** `<TableRow v-if="activeInvoice?.invoiceId === invoice.invoiceId">`, not inside the `data-testid="invoice-row"` element. `invoiceRow.getByTestId('payment-form')` would not have resolved in #7 either.
- Row 4 also carries **no `file:line` for its "#7 relied on X" half**, which `CLAUDE.md` ("Porting from #7") requires of every row: *"The '#7 relied on X' half is read from #7's checkout, with a file and line."* That omission is what let the claim go unchecked.
- The error is propagated into two further places:
  - `apps/web/e2e/happy-path.spec.ts:93-96` — a shipped comment asserting "a real structural difference from #7's `invoiceRow.getByTestId('payment-form')`". This is the one that matters most: #9 (FastAPI) will read this file.
  - `progress/impl_e2e_playwright.md`, assertion-inventory item 9, classed "ported, adapted locator scope" when it is in fact **ported verbatim**. The headline totals are therefore off by one each: 25 verbatim, 4 adapted (not 24 / 5).

**Consequence: none behavioural.** #8's line 97 is identical to #7's line 74, and the assertion passes. This is a false statement in the record of record, not a missing property — which is why it is a condition on the commit rather than a rejection.

**C2 (correction required before the commit) — the ledger omits the one row #7's own history proves was load-bearing.**

#7's `progress/review_e2e_playwright.md` **rejected** this very feature over D1: the terminal-state waits (`toHaveText('completed')`, `toHaveText('cancelled')`) can poll a permanently stale DOM to timeout, because the order-detail page stopped refetching once a `ready` document landed and depended on a single SSE frame that can be lost. #7 fixed it in the **app** (`apps/web/app/composables/useOrderDetail.ts:112` — a `STALE_STATUS_BACKSTOP_MS` backstop while non-terminal, plus D7's one further poll after an observed terminal transition), not in the spec — which is why #7's shipped specs, and therefore #8's ported ones, use a bare `toHaveText` with no reload.

So #8's specs silently rely on that property. The ledger has no row for it. **I checked whether the property is actually supplied, and it is:** `apps/web/src/hooks/use-order-detail.ts:59-71` implements the same backstop and the same one-shot catch-up, and its doc comment at `:13-21` even names the origin ("#7 D2/D7, ported"). It arrived with the web feature, not with this one.

This is a genuine ledger miss of exactly the class `CLAUDE.md` calls out — invisible to traceability, invisible to arming, and the reason #7 lost a round here. It costs one row, and it must be written down so #9 does not port the bare `toHaveText` onto a stack that lacks the backstop.

## 6. Assertion inventory — recounted

`grep -o "expect(" | wc -l` over #7's three files: `global.setup.ts` 2, `happy-path.spec.ts` 14, `compensation.spec.ts` 15 — **31 total**, matching the record exactly. One `expect(` per line, so the line count and the occurrence count agree. The 2 deliberate non-ports (both `toHaveURL(/\/orders$/)` after an intermediate "order list" hop that #8's flow does not have) are correct and follow from ledger row 2, which I verified. The only inventory error is item 9's classification (C1).

## 7. The `1.62.1` pin

Sound, and for a better reason than the one given.

- The **load-bearing justification** is parity: `1.62.1` is exactly what #7 pins (`../order-to-cash-nestjs/apps/web/package.json:39`, `^1.62.1`). #8 pins it exactly, which is #8's own convention for every other dependency in that file. For a trilogy that compares stacks, pinning the same runner version is the right call independently of any CDN weather.
- The **CDN justification I could not confirm or refute**, and I am saying so rather than asserting it. My instrument was invalid: hand-built `cdn.playwright.dev` URLs returned HTTP 400 after redirects for revision **1234 as well** — the revision that is demonstrably installed and working on this machine. An instrument that reports failure for the known-good case cannot adjudicate the known-bad one. (I did confirm `playwright-core@1.63.0`'s `browsers.json` wants chromium revision `1243`, so that half of the account is right.)
- **No follow-up backlog entry is owed.** There is no obligation to bump, the pin matches #7, and the implementer's "re-verify the CDN before ever bumping past 1.62.1" is adequate as a note. If a future bump is ever proposed, that is the trigger.
- Lockfile is clean: `grep "1\.63\.0" apps/web/pnpm-lock.yaml` returns nothing; the only entries are `@playwright/test@1.62.1`, `playwright@1.62.1`, `playwright-core@1.62.1`. A `node_modules/.pnpm/playwright-core@1.63.0` directory survives in the local store from the failed attempt, but it is unreferenced by the lockfile and inside gitignored `node_modules/`.

## 8. Backlog id 106 — judged

**Sound; I would not change it.** Its factual basis is confirmed by my own probe (§4), independently of the implementer's. Specifically:

- The **acceptance bullets are well aimed.** Bullet 1 names the right unit ("on the FIRST render after any number of prior server-side renders in the same process, not only on a freshly restarted process") — which is exactly the failure mode, and exactly the one a naive test would miss by restarting the process. Bullet 3 demands a test that renders N times in the same module instance, which is the only shape that can fail before the fix and pass after it. Bullet 2 names a concrete remedy (`useId()`) without over-constraining it.
- Bullet 4 correctly makes the e2e simplification **optional and the leader's call**, rather than mandating churn in a suite that is green.
- **LIGHT is the right size** under `CLAUDE.md`'s cost discipline: the change is component-local id generation in one file, with no saga, money, wire-contract, persistence or `specs/shared/` surface.
- The **re-open trigger is named and specific** ("any test that starts relying on `getByLabel`/`getByRole` name matching for a per-line field"), which is what makes this an accepted-not-fixed disposition rather than a deferral into silence.
- One thing the notes understate, worth carrying into the fix: this breaks the label/control association for assistive technology on **every** per-line field, not only Product — my probe shows `Quantity`, `Unit price override` and `Line discount` labels pointing at `-7/-8/-9/-10` while their inputs carry `-2`. The fix is the same one fix; the blast radius of the *defect* is four fields per line, not one.

This finding is **not** rooted in `specs/shared/`, so no `SA-n` amendment is owed. It is already routed as a numbered backlog entry with a disposition, which is the artefact that outlives the feature.

## 9. Scope

Exactly as briefed, confirmed with `git status --porcelain --untracked-files=all`:

- `apps/web/e2e/{global.setup.ts,happy-path.spec.ts,compensation.spec.ts}` (new), `apps/web/playwright.config.ts` (new), `apps/web/package.json` + `apps/web/pnpm-lock.yaml` (modified), `feature_list.json` (id 32's status line, plus the leader's own id 106 entry).
- **No `src/` change of any kind.** No `tests/`, no `specs/`, no `.csproj`, no `.gitignore`.
- The sealed session cookie cannot be committed: `git check-ignore -v apps/web/test-results/.auth/operator.json` → `.gitignore:46:test-results/`. No `playwright-report/` exists; `test-results/` from my own runs is ignored.
- `test:e2e` is correctly **outside** `pnpm test` and `./quality.sh`, matching #7's precedent and the `test:integration` one.

## 10. `CHECKPOINTS.md` walk

**C1 — the harness is complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 (run by me).

**C2 — state is coherent**
- [x] At most one feature `in_progress` — zero, with id 32 at `in_review` when I started; set to `done` by this review.
- [x] Every status is in `rules.valid_status` (`init.sh` exit 0); 105 entries, 105 unique ids.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session (phase 19, id 32).
- [x] Every `blocked` feature records why — none blocked.

**C3 — architecture is respected** (inherited; this feature touches no backend code, no `Domain/`, no `.csproj`, and adds no runtime dependency — the one package added is a dev-only test runner)
- [x] No forbidden reference inside any `Domain/` — unchanged since phase 18's NetArchTest pass; nothing in this diff can affect it.
- [x] No cross-service DB access — the suite talks only to the web UI, which talks only to the Gateway.
- [x] No shared runtime code beyond `SharedKernel` / `Contracts` / `Cqrs` — unchanged.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — unchanged.
- [x] `src/SharedKernel` still has zero `PackageReference` entries — unchanged.
- [x] No `decimal` in domain arithmetic — unchanged.
- [x] Every interaction Kafka-fact or NATS-RPC — unchanged; the suite introduces no interaction.
- [x] No stray debug logging, no context-free TODOs in the four new files.

**C4 — verification is real**
- [x] Verification for the surface this feature touches: web unit suite 286/286 and the e2e suite 3/3, both run by me. Full `./quality.sh` not re-run — justification and inheritance stated in §2.
- [x] Domain tests still pure (untouched).
- [x] Integration tests still Testcontainers-based (untouched). The e2e suite itself runs against a real composed stack with real MS-SQL/Kafka/NATS/Mongo — no mocked broker anywhere.
- [x] Coverage thresholds unaffected — `e2e/**` is not a Vitest target and adds no source to the coverage surface.
- [x] **No Jest.** `@playwright/test` is a separate runner for a separate layer, invoked outside `pnpm test`. xUnit and Vitest unchanged.

**C5 — the session closed cleanly**
- [x] No suspicious untracked files; `test-results/` is gitignored and the working tree is exactly as I received it.
- [x] `progress/history.md` has an entry for id 32 including its effort record — appended by this review.
- [x] `feature_list.json` reflects true state — id 32 set to `done` by this review.
- [ ] The human has been told what was done and how to test it manually — **for the leader**, at the phase close. The manual script is: `scripts/dev-stack.sh start`, then `cd apps/web && pnpm run test:e2e`, then `scripts/dev-stack.sh stop`. Note that Chromium must be installed once (`node ./node_modules/@playwright/test/cli.js install chromium`), and that this line lives only in `progress/impl_e2e_playwright.md` — see N2.
- [x] **Claude did not commit.** No `git commit`, no `git push`, and no git command that writes the index or working tree was run in this review.

**C6 — SDD: not applicable.** Id 32 is `"sdd": false`; no `specs/<name>/` is expected and none was created. `specs/` is untouched (confirmed with `git status specs/`).

**C7 — trilogy reusability**
- [x] `specs/shared/` untouched by this pass — Playwright is an `apps/web` concern only.
- [x] No deviation, so no amendment is owed.
- [x] The `R<n>` ids are #7's — none is claimed or retired here; this feature adds no test-matrix row (the e2e layer sits above the matrix, as in #7).
- [x] `n8n/workflows/*.json` unchanged.
- [x] The black-box API script proves the same saga steps as #7's — inherited from id 31, not re-derived here.
- [x] `progress/history.md` effort records complete and honest — this feature's entry is appended, and it records honestly that the raw wall-clock is not the whole story (see the entry).
- [x] The README benchmark section — the leader's, at the wrap-up; unchanged by this feature.

## 11. Findings

**C1 (correction required before the commit)** — ledger row 4's "#7 relied on X" half is false in both its halves, carries no `file:line` as `CLAUDE.md` requires, and the error is propagated into a shipped source comment at `apps/web/e2e/happy-path.spec.ts:93-96` and into inventory item 9 (and thence the 24/5 totals, which should read 25/4). **Why it matters:** the ledger is the artefact #9 inherits, and a row asserting a #7 idiom that #7's file contradicts is a false premise in the record of record. **Route:** the `progress/` half is the leader's own edit; the `apps/web/e2e/happy-path.spec.ts` comment is a mechanical test edit — `test_maintainer` (haiku), per `CLAUDE.md`'s routing rule. Nothing else in the file changes.

**C2 (correction required before the commit)** — the ledger omits the row for the stale-DOM backstop that #7 rejected this feature over. Add one row: *"#7 relied on the order-detail page's backstop refetch while non-terminal plus one poll after an observed terminal transition (`../order-to-cash-nestjs/apps/web/app/composables/useOrderDetail.ts:112`, #7's D1/D2/D7) so a lost SSE frame cannot leave `toHaveText('completed')` polling a permanently stale DOM; in #8 that property is supplied by `apps/web/src/hooks/use-order-detail.ts:59-71`, shipped with the web feature, documented at `:13-21`."* Verified supplied by me — this is a record correction, not code work. **Route:** leader, in `progress/impl_e2e_playwright.md`.

**N1 (non-blocking, observation)** — the suite consumes seeded stock and never replenishes: each happy-path run takes 2 units of `ALBIONFOODS`/`PRD-0006`. After my two happy-path orders the live figure read `units: 496, availableUnits: 496, lowStockThreshold: 20` — consistent with a 500-unit seed at session start, so `dev-stack.sh start` appears to restore the baseline rather than accumulate across sessions. At 2 units per run the buffer allows on the order of 240 runs before the acceptance-time availability check would start refusing. #7 raised the same point (its review §8). No action now; worth knowing before anyone loops this suite.

**N2 (non-blocking, docs, for the wrap-up)** — the one-off `install chromium` step, the `--with-deps`-needs-`sudo` caveat and the "start the stack first" requirement live only in `progress/impl_e2e_playwright.md`. A new operator looks in `README.md`. #7's review raised the identical gap. This is the leader's at the phase close, not the implementer's.

**N3 (non-blocking, citation hygiene)** — ledger row 1 cites `happy-path.spec.ts:25-30` where the idiom actually sits at `:28-33`. The row is substantively correct; the range is off by three. Worth fixing in the same pass as C1/C2 since the file is open anyway.

## 12. What the implementer got right, explicitly

The claims I checked held up: 31 is the exact `expect(` count across #7's three files; the two deliberate non-ports follow correctly from a structural difference I verified; the `test-results/` storage-state path genuinely avoids a `.gitignore` edit and is genuinely ignored; the scope is exactly the authorised files with no `src/` change; the found frontend defect is real, correctly diagnosed down to the module-scope counter, correctly judged not to block either bullet, correctly routed around with a locator that fails loudly rather than drifting, and correctly filed rather than silently fixed out of scope. The D4 note on the ordering assertion being a probabilistic guard against a random tie-break — with feature 31's `assertCausalOrder` named as the deterministic one — is honest and correct, and it is cited rather than re-derived, which is the right call.

## 13. One action this approval hands to the leader

Setting id 32 `done` necessarily puts `init.sh` into lockstep failure until the session file is closed:

```
── 4. Session file in lockstep
[FAIL]  progress/current.md claims a feature while none is active:
        "**Feature:** **id 32 `e2e_playwright` — `in_progress`** (phase 19, full process). ..."
```

Everything else in `init.sh` is green — including the backlog tripwire ("no feature lost, no done reverted"), the commit-msg hook, and shared-spec parity with #7 ("byte-identical across 6 file(s)"). `progress/current.md` is the leader's session file and the phase-close artefact, so I have deliberately not touched it; the reviewer's writes are confined to this review, id 32's status line and the `progress/history.md` entry.

**Leader's close, in order:** discharge C1 (including the `test_maintainer` edit to `apps/web/e2e/happy-path.spec.ts:93-96`) and C2, then rewrite `progress/current.md` for the phase close, then re-run `init.sh` to green. C1 and C2 are corrections to the ported-idiom record, and the record is what #9 inherits — they are conditions on the commit, not follow-up work for "the next feature that touches this".
