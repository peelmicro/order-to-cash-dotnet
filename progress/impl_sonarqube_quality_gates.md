# impl_sonarqube_quality_gates — backlog id 34, phase 21 (LIGHT)

Sole implementer round, no separate reviewer (CLAUDE.md "Cost discipline" —
LIGHT change, build/CI scripting). The leader reads this and re-runs the
affected gate itself.

## Contract (feature_list.json id 34, verbatim)

- coverage gate proven to FAIL when breached, not merely configured
- `dotnet format` clean at solution level
- SonarQube runs under its optional compose profile

## Files touched

- `quality.sh` — new section 5 (coverage gate); renumbered the old section 5
  ("Web app") to section 6 and fixed the two `QUALITY_ONLY=web` comments
  that named section numbers.
- `sonar-scan.properties` — new (see "Why not sonar-project.properties"
  below — a real, live-verified naming conflict, not a stylistic choice).
- `package.json` — added `sonar:scan` script.
- `feature_list.json` — id 34's `status` line only (`in_progress` →
  `in_review`; confirmed via `git diff feature_list.json`, one hunk).
- This file.

Nothing else was touched. `test.runsettings` was modified twice, transiently,
for the arming exercise below (a coverlet `Exclude` filter), and restored
both times; `git diff -- test.runsettings` is empty and a system reminder
during the session independently confirmed the file was back to its
original content.

## Coverage-gate mechanism, and why

Every test project uses `coverlet.collector` (`--collect:"XPlat Code
Coverage"`), confirmed by grepping `tests/*/*.csproj` — none reference
`coverlet.msbuild`, so there is no per-project build-time `/p:Threshold=`
to lean on (that flag is `coverlet.msbuild`-only).

`coverlet.collector` also instruments whatever assemblies happen to load
into each test host, so a single project's `coverage.cobertura.xml`
under-reports code a SIBLING project also exercises (e.g. `Orders/Domain/
*.cs` shows partial coverage under `Orders.UnitTests` alone, because
`Orders.IntegrationTests` covers different lines of the same files).
Averaging each report's own `line-rate` (what `quality.sh` did before this
feature) would therefore double-count some lines and miss the union of
others.

Mechanism chosen: `quality.sh` section 5 runs an inline Python 3 script
(no new package, no new file beyond `quality.sh` itself) that:

1. Globs every `TestResults/**/coverage.cobertura.xml` produced by
   section 3's `dotnet test ... --collect:"XPlat Code Coverage"`.
2. For every `<class filename="...">`, records each `<line number
   hits="...">` in a dict keyed by `(filename, line number)`, OR-ing
   `hits > 0` across every report — a line counts as covered if ANY report
   hit it, which is the correct union semantics coverlet.collector's
   per-project reports need.
3. Excludes `*/Migrations/*.cs` and `*ModelSnapshot.cs` (EF Core
   scaffolding from `dotnet ef migrations add`, auto-generated, never
   hand-written) from BOTH gates — the same principle #7's
   `sonar-project.properties` applies to its own generated code
   (`packages/contracts/src/generated/**`). Verified
   `Projector/Infrastructure/Persistence/TimelineOrderMigration.cs` is
   NOT excluded by this filter (hand-written application code, lives
   directly under `Infrastructure/Persistence/`, not a `Migrations/`
   directory, does not end in `ModelSnapshot.cs`).
4. Computes `domain = covered/total` for every file whose path contains
   `/Domain/`, and `overall = covered/total` for every file that produced
   a class entry anywhere.
5. Exits non-zero, printing one `GATE_FAIL <which layer> <pct>%
   (<covered>/<total> lines) is below the required <threshold>% threshold`
   line per breached gate, if `domain < 80%` or `overall < 60%`.

Domain layer = every `.cs` file under `/Domain/` across the SEVEN services
that have a `Domain/` folder — corrected mid-implementation: I first wrote
"six services... Seed has no saga domain, only a runner" in a quality.sh
comment, without checking it. Running `find src/*/Domain -type d` showed
Seed DOES have a `Domain/` folder (`SeedDomainPlaceholder.cs`,
`OrderSagaFixture.cs`, `SagaFixtures.cs`, and five more — deterministic
seed-data/fixture models, not a saga, but still a `Domain/` folder). Fixed
the comment before committing to it as a claim (Billing, Fulfillment,
Gateway, Notifications, Orders, Projector, Seed — all seven; SharedKernel,
Contracts, Cqrs have no `Domain/` folder and are correctly excluded from
the domain-layer numerator/denominator, while their files still count
toward "overall").

## Real coverage numbers, today (not assumed)

Full solution build + test run, from a clean `TestResults/`, right before
writing this report:

```
$ dotnet format OrderToCash.sln --verify-no-changes   # exit 0, clean
$ dotnet build OrderToCash.sln --nologo                # Build succeeded, 0 Warning(s), 0 Error(s)
$ rm -rf TestResults
$ dotnet test OrderToCash.sln --nologo --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

All 18 test projects passed, 0 failures (Gateway.UnitTests 248, Fulfillment.
UnitTests 146, Orders.UnitTests 503, Seed.UnitTests 47, Projector.UnitTests
121, Architecture.Tests 50, Seed.IntegrationTests 6, Notifications.
IntegrationTests 29, Fulfillment.IntegrationTests 64, Billing.
IntegrationTests 90, Gateway.IntegrationTests 78, Orders.IntegrationTests
155, Projector.IntegrationTests 68, plus Billing.UnitTests, Cqrs.
UnitTests, Contracts.UnitTests, Notifications.UnitTests, SharedKernel.
UnitTests not individually echoed above but all green in the full run's
"0 Failed" totals). 18 `coverage.cobertura.xml` files produced.

`quality.sh` section 5 (the actual file, run via a `source`d extraction —
see "Arm" below) against that fresh data:

```
[INFO]  domain layer coverage: 96.83% (2717/2806 lines) — threshold >=80.0%
[INFO]  overall coverage: 93.83% (15692/16723 lines) — threshold >=60.0%
[OK]    coverage gate passed (domain >=80.0%, overall >=60.0%)
```

Both real numbers are well clear of the thresholds — no headroom concerns
today.

## Arm (CLAUDE.md arming protocol) — three rounds, all restore-confirmed

The gate makes two independent countable claims (domain ≥80%, overall
≥60%), so it was armed twice against the REAL artifact the mechanism reads
(`TestResults/**/coverage.cobertura.xml` — "corrupt a field the test
supplied," the defeat list's mutation family #2, applied at the exact
input boundary of this feature's new code), plus once against a REAL,
freshly-collected report using a genuine coverlet `Exclude` filter (the
method the brief's "Do" list suggested), to prove the full pipeline (not
just the parsing script) can produce an authentic shortfall.

All three rounds ran the gate logic extracted VERBATIM from the real
`quality.sh` (`sed -n '/^DOMAIN_THRESHOLD=80.0/,/^fi # QUALITY_ONLY/p'
quality.sh`, sourced into a wrapper supplying `ok`/`info`/`warn`/`fail`,
`COVERAGE_DIR`, `REPORT_FOUND=1`) — not a hand-retyped copy — after first
confirming an early hand-typed copy produced identical output, to remove
any doubt of a transcription mismatch between what was tested and what
ships.

### Round 1 — domain-only breach (artifact corruption)

Backed up `TestResults/` (`cp -r`, 18 files). Zeroed every `hits="..."`
attribute inside every `<class filename="...Domain...">` block, across all
18 reports (regex-scoped to `<class>...</class>`, 12 of 18 files actually
contained a `/Domain/` class and were changed).

Verbatim failure:
```
[INFO]  domain layer coverage: 0.00% (0/2806 lines) — threshold >=80.0%
[INFO]  overall coverage: 77.59% (12975/16723 lines) — threshold >=60.0%
[FAIL]  domain layer coverage 0.00% (0/2806 lines) is below the required 80.0% threshold
[FAIL]  coverage gate FAILED — see the [FAIL] line(s) above for which layer and by how much
```
Exit code: 1. Overall stayed above 60% (77.59%) — confirms the domain
branch fires independently, naming the exact project-agnostic layer,
percentage, fraction and threshold.

Restore: `rm -rf TestResults && cp -r <backup> TestResults`, confirmed
with `diff -rq` (identical) AND a per-file `cmp -s` loop (18/18 files, no
mismatch reported). Re-run: back to `96.83% (2717/2806)` / `93.83%
(15692/16723)`, `[OK]`, exit 0 — bit-identical to the pre-corruption
numbers.

### Round 2 — overall-only breach (artifact corruption), domain untouched

Same backup. Zeroed every `hits="..."` in every class WITHOUT `/Domain/`
in its filename, across all 18 reports (17 of 18 changed — one report,
Architecture.Tests's, contained only a `SharedKernel` class with 0 hits
already at that point in its own execution, no change needed).

Verbatim failure:
```
[INFO]  domain layer coverage: 96.83% (2717/2806 lines) — threshold >=80.0%
[INFO]  overall coverage: 16.25% (2717/16723 lines) — threshold >=60.0%
[FAIL]  overall coverage 16.25% (2717/16723 lines) is below the required 60.0% threshold
[FAIL]  coverage gate FAILED — see the [FAIL] line(s) above for which layer and by how much
```
Exit code: 1. Domain stayed green (96.83%, unchanged) — confirms the
overall branch fires independently of the domain branch, and that the two
checks do not share a single pass/fail flag.

Restore: identical procedure, `diff -rq` identical, `cmp -s` loop clean
18/18, re-run green at the original numbers, exit 0.

### Round 3 — real pipeline, genuine coverlet `Exclude` filter (no threshold breach reached, and that itself is evidence)

To satisfy the brief's explicit suggestion ("narrow a coverlet include/
exclude filter... run quality.sh, or the relevant section") rather than
relying solely on artifact-level corruption, `test.runsettings` was
temporarily given a `DataCollectionRunSettings` block:
```xml
<DataCollectionRunSettings>
  <DataCollectors>
    <DataCollector friendlyName="XPlat code coverage">
      <Configuration>
        <Exclude>[OrderToCash.Seed]OrderToCash.Seed.Domain.*</Exclude>
      </Configuration>
    </DataCollector>
  </DataCollectors>
</DataCollectionRunSettings>
```
(marked `<!-- TEMPORARY — feature 34 arming exercise -->`), and `dotnet
test OrderToCash.sln --filter "FullyQualifiedName~Seed" --collect:"XPlat
Code Coverage" --results-directory <scratch>` was run fresh. Confirmed
LIVE that `Seed/Domain/*.cs` classes are entirely ABSENT from the
resulting reports (`grep -c 'filename="Seed/Domain/' <report>` → `0` in
all five reports that would otherwise have carried them) — the exclude
filter genuinely removed real, just-collected coverage data, not merely a
post-hoc edit.

Swapping only those two fresh Seed reports into a copy of the full 18-
report set and re-running the gate: `95.44%` domain (was `96.83%`),
`91.15%` overall (was `93.83%`) — dropped, but not far enough to cross
either threshold, because Seed's own `Domain/` layer (772 of the 2806
total domain lines, per a diagnostic breakdown by service: Fulfillment
429, Orders 501, Billing 640, Gateway 235, Projector 229, Seed 772) is
covered at 98% and its loss costs about 27 percentage points of ITS OWN
weight, not enough on its own to sink the aggregate below 80% given the
other six services' near-total coverage. (An earlier attempt excluding
only Fulfillment's smaller Domain/, 429 lines, moved the aggregate even
less — 96.83% → 95.44%/91.15% figures above are actually the Seed
exclusion's result; Fulfillment's alone barely moved it and is not
reported separately here since Round 1/2's artifact-level arms already
cover the FAIL branch with verbatim evidence.) This round's value is
narrower and different from Round 1/2: it proves the coverlet `Exclude`
mechanism itself genuinely removes real collected data from a live
`dotnet test` run (the tool-level premise Round 1/2's arms take as given),
not that the aggregate crosses 80% from a single service's loss alone —
which is itself informative: the 80% domain floor has real headroom
against any one of the six real (non-Seed-outsized) services disappearing
entirely, which is a reasonable property for a repository-wide gate to
have. `test.runsettings` was restored immediately after (`cp` from a
pre-edit backup); `git diff -- test.runsettings` is empty, confirmed twice
in-session.

## `dotnet format` — solution level

`dotnet format OrderToCash.sln --verify-no-changes` — exit 0, clean, run
three times across the session (before any edit, after the `quality.sh`
edits, and again as the final check after all other changes). No `.cs`
files were touched by this feature, so this was expected to stay clean;
confirmed rather than assumed.

## `sonar-project.properties` port — and why the file is named `sonar-scan.properties` instead

The brief asked to port `#7`'s `sonar-project.properties`, "adapting the
language-specific parts." Ported it verbatim in spirit (same role: opt-in,
documents what a scan WOULD analyse, never the enforced gate — SonarQube's
optionality principle carries over unchanged) — but two real, LIVE-verified
differences forced more than a syntax adaptation:

1. **`sonar.sources` / `sonar.tests` are silently ignored by SonarScanner
   for .NET.** Verified live: passing them as `/d:` flags to `dotnet-
   sonarscanner begin` printed `WARNING: The sonar.sources and sonar.tests
   properties are not supported by the Scanner for .NET and are ignored.
   They are automatically computed based on your repository.` The C#
   scanner walks the repository tree itself; `sonar.exclusions` and
   `sonar.test.inclusions` are the levers that actually apply. The ported
   file omits `sonar.sources`/`sonar.tests` and says why, rather than
   including values the tool would silently discard.
2. **A file literally named `sonar-project.properties` anywhere under the
   repo root makes `dotnet-sonarscanner end` FAIL outright**, not merely
   ignore it. Verified live, with a real SonarQube 26.8.0 container and a
   real admin-generated token:
   ```
   08:21:45.924  sonar-project.properties files are not understood by the SonarScanner for .NET. Remove those files from the following folders: /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet
   08:21:45.924  Post-processing failed. Exit code: 1
   ```
   This is not a "spec is wrong, stop and report" situation — it is a real
   tooling conflict discovered while implementing an item on my own touch
   list (`sonar-project.properties (new)`), squarely within an
   implementer's latitude to resolve. Renamed the port to
   `sonar-scan.properties` (documented in the file's own header, with the
   verbatim scanner error above) — content and role otherwise unchanged
   from what the brief asked for. After the rename, the identical
   `begin`/`build`/`end` sequence completed with `ANALYSIS SUCCESSFUL`
   (full transcript below).

Content differences from #7's file, both flagged inline in `sonar-scan.
properties`'s own comments:
- Coverage import: #7 uses `sonar.javascript.lcov.reportPaths` (every
  vitest workspace emits `lcov`). #8's `.cs` coverage reuses the SAME
  `TestResults/**/coverage.cobertura.xml` files `quality.sh`'s own gate
  merges, via `sonar.cs.cobertura.reportsPaths` — no new reporter, no new
  test run just for Sonar. `apps/web`'s `vitest.config.ts` also emits a
  `cobertura` reporter (not `lcov`), and `apps/web` is out of this LIGHT
  feature's touch list — its coverage is deliberately NOT imported; the
  file names this as a gap for a future feature that touches `apps/web/
  vitest.config.ts`.
- Generated-code exclusions: EF Core `*/Migrations/*.cs` and
  `*ModelSnapshot.cs` (mirrors `quality.sh` section 5's own exclusion,
  same reasoning) in place of #7's `packages/contracts/src/generated/**`;
  `apps/web/src/generated/**` (OpenAPI types) and `apps/web/src/
  components/ui/**` (shadcn-style primitives) carried over from #7's
  equivalent `vitest.config.mts` exclude list, confirmed against `apps/
  web/vitest.config.ts`'s own `coverage.exclude` (`src/generated/**`,
  `src/components/ui/**`, `src/test/**`, `src/**/*.test.{ts,tsx}`).

`package.json`'s new `sonar:scan` script gates on `SONAR_TOKEN` exactly
like #7's (same guard message shape), but its invocation is `dotnet-
sonarscanner begin ... && dotnet build ... && dotnet test ... --collect:
"XPlat Code Coverage" ... && dotnet-sonarscanner end ...` — the MSBuild-
wrapped shape .NET analysis requires — rather than #7's throwaway
`docker run sonarsource/sonar-scanner-cli` container. `dotnet-sonarscanner`
is a pre-existing GLOBAL dotnet tool on this machine (`dotnet tool list`
shows it alongside `dotnet-ef` and `ilspycmd`, no `.config/dotnet-tools.
json` manifest exists in this repository — it was provisioned at the
environment/harness level, not by this feature), so the script assumes it
is on `PATH` rather than containerising it; #7's containerised approach
needed `--network otc-net` to reach a hostname (`otc-sonarqube`) from
inside another container, which does not apply here since `dotnet-
sonarscanner` runs on the host and reaches SonarQube via its published
host port (`localhost:${SONARQUBE_HOST_PORT:-9000}`).

## SonarQube — live evidence, and that the profile stays optional

1. **Optionality, proven both directions:**
   - `pnpm run dc:up:infra:no-n8n` (core infra, no `sonar` profile) — full
     output captured; `docker ps --format '{{.Names}}'` afterward lists
     `otcnet-grafana, otcnet-jaeger, otcnet-kafka, otcnet-kafka-console,
     otcnet-kafka-exporter, otcnet-kafka-init, otcnet-mailpit,
     otcnet-mongodb, otcnet-mssql, otcnet-nats, otcnet-otel-collector,
     otcnet-prometheus` — 12 containers, NO `otcnet-sonarqube`; `grep -i
     sonar` against that list exits 1 (no match). Torn down afterward
     (`docker compose -f docker-compose.infra.yml down`), confirmed empty
     `docker ps`.
   - `pnpm run dc:up:sonar` (the `sonar` profile alone) — brought up
     `otcnet-sonarqube` with NOTHING else; `docker inspect --format
     '{{.State.Health.Status}}'` polled every 10s, reached `healthy` at
     the fifth poll (~50s, inside the compose healthcheck's documented
     30×10s/60s `start_period` budget).
   - `quality.sh` does not reference `sonar`, `sonarqube`, `9000` or
     `dc:up:sonar`/`dc:down:sonar` anywhere (grep confirms), and every
     round of section 1-5 arming above ran with NO SonarQube container up
     at all — the gate never depends on it.
2. **Reachability:** `curl -s http://localhost:9000/api/system/status` →
   `{"id":"...","version":"26.8.0.126808","status":"UP"}`.
3. **A real scan, attempted and completed** (the brief said this was
   "worth trying but not required" if it needed interactive setup — it
   did not): generated a user token headlessly via the REST API
   (`POST /api/user_tokens/generate` with the default `admin`/`admin`
   basic-auth credentials of a just-started, never-configured local
   container — SonarQube Community 26.8's API does not force the
   UI-only password-change flow), then ran the real
   `begin`/`build`/`end` sequence (using the already-fresh `TestResults/`
   from the coverage-number run above, to avoid a third ~35-minute full
   solution test run for a step that does not need fresh coverage data to
   prove the SCAN mechanism itself works). `end` finished:
   ```
   INFO: Analysis report uploaded in 351ms
   INFO: ANALYSIS SUCCESSFUL, you can find the results at: http://localhost:9000/dashboard?id=order-to-cash-dotnet
   INFO: SonarScanner Engine completed successfully
   Post-processing succeeded.
   ```
   `curl -s -o /dev/null -w "%{http_code}" http://localhost:9000/dashboard?id=order-to-cash-dotnet` → `200`. Both the C# analyser (`Sensor C#` phase during `begin`'s Roslyn-analyzer provisioning) and the JS/TS analyser ran (`end`'s log shows `Sensor JavaScript/TypeScript/CSS analysis`, 101 source files under `apps/web/tsconfig.json`, 92 analysed).
4. **Teardown:** `pnpm run dc:down:sonar` → `Container otcnet-sonarqube
   Stopped`; `docker ps` empty; the scanner's working directory
   (`.sonarqube/`, not gitignored — see Finding below) removed with
   `rm -rf .sonarqube` after both the dummy-token dry run and the real
   scan; `git status --short` shows no `.sonarqube` entry.

## Findings not fixed (outside this feature's touch list)

- `.sonarqube/` (SonarScanner for .NET's working directory, created by
  `begin`, normally removed by a successful `end`, but left behind on a
  failed `begin`/`end` — as happened once during the naming-conflict
  discovery above) is not in `.gitignore`. It never leaked into `git
  status` in this session because it was manually `rm -rf`'d each time,
  but a future contributor running `pnpm run sonar:scan` and hitting a
  network failure mid-scan would see it as an untracked directory.
  `.gitignore` is not on this feature's touch list (`quality.sh`,
  `Directory.Build.props`/`.csproj` files, `sonar-project.properties`
  [now `sonar-scan.properties`], `package.json`, progress record) so this
  was not fixed directly — flagging for the leader/reviewer to add
  `.sonarqube/` to `.gitignore` in a follow-up.
- The `dotnet-sonarscanner` tool is a global, machine-level install with
  no `.config/dotnet-tools.json` manifest pinning its version in this
  repository (confirmed: no such file exists). `sonar:scan` will fail
  with "command not found" on a machine that has not separately run
  `dotnet tool install --global dotnet-sonarscanner`. This mirrors how
  `dc:up:sonar`/`dc:down:sonar` were already "copied from the harness
  phase, never run against #8" per the brief — a full pin (local tool
  manifest) is a reasonable follow-up but is genuinely new scope beyond
  what was asked (SonarQube is explicitly optional infrastructure).

## Self-verification

- `dotnet format OrderToCash.sln --verify-no-changes` — exit 0.
- `dotnet build OrderToCash.sln --nologo` — 0 Warning(s) affecting build
  result, 0 Error(s) (the build run during the real Sonar scan showed 127
  SonarAnalyzer-specific warnings, but those are injected ONLY while
  `dotnet-sonarscanner begin` has registered its own Roslyn analyzers for
  that one build — a normal `dotnet build`/`quality.sh` run outside a
  Sonar scan session does not have them registered and is unaffected,
  confirmed by the earlier, analyzer-free `dotnet build` which reported
  `0 Warning(s)`).
- `dotnet test OrderToCash.sln --collect:"XPlat Code Coverage"` — 18/18
  test projects green, 0 failures.
- `quality.sh`'s coverage-gate code, extracted verbatim and sourced (not
  hand-retyped) — passes against real data (96.83%/93.83%), fails with
  the correct named message against two independently corrupted artifacts
  (domain-only, overall-only), restores cleanly (`diff -rq` + `cmp`) both
  times, and re-passes at the exact original numbers both times.
- `bash -n quality.sh` — no syntax errors.
- `git status --short` at the end of the session: `feature_list.json`,
  `package.json`, `progress/current.md` (pre-existing, not mine),
  `quality.sh` modified; `sonar-scan.properties` untracked (new). No other
  file touched. `docker ps` and `pgrep` show nothing of mine running.
- `./init.sh` — not re-run in this session (no environment/state-coherence
  concern this feature could have broken); the leader's own check can run
  it.

## Acceptance criteria — status

- "coverage gate proven to FAIL when breached, not merely configured" —
  MET (Rounds 1 and 2 above, both restore-confirmed).
- "dotnet format clean at solution level" — MET, unchanged (verified
  three times, never touched by this feature).
- "SonarQube runs under its optional compose profile" — MET (healthy
  container, real reachable API, real completed scan, profile confirmed
  absent from `dc:up:infra:no-n8n`).
