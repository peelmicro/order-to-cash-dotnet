# Review: test_hosts_exhaust_the_per_user_inotify_limit (id 77, phase 14, `sdd: false`)

**Verdict: APPROVED** — round 1. Reviewed 2026-09-11, 12:00:52 → 12:12 CEST.

Both armed mutations and my own value-corruption probe fail the named guard with the exact message the acceptance requires. The coverage-collecting invocation path was armed independently, not only observed green. Every negative claim below is a search result with its command and output. Two non-blocking record-text defects (N1, N2) and three advisories (A1–A3) are recorded; none is rooted in `specs/shared/`.

## What was run, and what was deliberately not

- **Not re-run:** the full `./quality.sh`. The claim under test is one guard and one delivery path, not the suite. I re-read the leader's log instead (`scratchpad/quality_final.log`, born 11:44:46, modified 11:57:07 — inside the implementer's transcript window 11:38:19 → 11:59:45): 18 `Passed!` lines summing to **1821**, 0 `Failed!`, 0 matches for `inotify|IOException|user limit`, `[OK] quality.sh finished`. `Orders.UnitTests` = 445 = id 71's 444 + this guard.
- **Run:** three mutations of the harness (two arms plus probe 3), each under the full protocol — `cp -p` backup, mutate, `dotnet build --no-incremental tests/Orders.UnitTests/Orders.UnitTests.csproj`, one test with a plain `dotnet test` (no `--settings`), verbatim failure, restore from backup, `cmp`, `touch`, forced rebuild, confirming green. Arm (ii) was additionally run under `quality.sh`'s exact flags, failing and then passing. Plus one shell-environment masking probe and nine enumerations.
- **Filter used for every run:** `FullyQualifiedName=OrderToCash.Orders.UnitTests.HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled` (exact match, not `~`).
- **Restore evidence:** pre-probe md5 `1fde59db94161accef025cdeae11447e` (`Directory.Build.props`) and `3f8c6c24d4782be3e2ba114e63bb7d18` (`test.runsettings`); identical after the last restore. No `git checkout` used. `pgrep -a dotnet` before the first build: no dotnet process; after the last: only idle `MSBuild.dll /nodemode:1` reuse nodes.

## Probe table

| # | Mutation | Path | Result | Verbatim tail of the failure |
|---|---|---|---|---|
| 1 | `<RunSettingsFilePath>` line removed from `Directory.Build.props` | plain `dotnet test` | **FAILED**, exit 1, 110 ms | `…applied to every test project via Directory.Build.props' RunSettingsFilePath, but it was "<null>". Every real host a test builds opens one inotify instance…` |
| 1b | same mutation, `DOTNET_hostBuilder__reloadConfigOnChange=false` exported by the invoking shell | plain, `--no-build` | Passed 1/1 | (see ruling on probe 3) |
| 1✓ | restored; `cmp` IDENTICAL; `touch`; `--no-incremental` | plain | Passed 1/1, 100 ms | — |
| 2 | `<DOTNET_hostBuilder__reloadConfigOnChange>` entry removed from `test.runsettings` (empty `<EnvironmentVariables>` left) | plain `dotnet test` | **FAILED**, exit 1, 169 ms | `…but it was "<null>". …` |
| 2q | same mutation | `--collect:"XPlat Code Coverage" --results-directory <dir>` (quality.sh's flags) | **FAILED**, exit 1, 196 ms; one `coverage.cobertura.xml` written, so the collector was live | `…but it was "<null>". …` |
| 2✓ | restored; `cmp` IDENTICAL; `touch`; `--no-incremental` | plain / quality.sh flags | Passed 1/1 (148 ms) / Passed 1/1 (116 ms), cobertura written | — |
| 3 | entry's value `false` → `true` in `test.runsettings` | plain `dotnet test` | **FAILED**, exit 1, 173 ms | `…applied to every test project via Directory.Build.props' RunSettingsFilePath, but it was "true". Every real host a test builds opens one inotify instance…` |
| 3✓ | restored; `cmp` IDENTICAL; `touch`; `--no-incremental` | plain | Passed 1/1, 147 ms | — |

All three kills name the intended cause: `"<null>"` for the two deletions, `"true"` for the corruption. None fell back to an unrelated failure.

## Acceptance → evidence

| Bullet | Verified by | Status |
|---|---|---|
| 1 — the landed change recorded as this entry's | `test.runsettings:23` sets the variable under `RunConfiguration/EnvironmentVariables`; `Directory.Build.props:31` carries `RunSettingsFilePath`; `git diff -U0 -- Directory.Build.props` is the 9-line block only. The record's "What already existed" section claims it for id 77. Both paths are proved by probes 2 and 2q plus their greens | met |
| 2 — guard reads the host's own `IConfiguration`, armed two ways | `HostInotifyReloadGuardTests.cs:45-62` builds through `OrdersHost.CreateBuilder` (the same method `Program.cs` calls; `OrdersHost.cs:38` is `Host.CreateApplicationBuilder(args)`) and reads `host.Services.GetRequiredService<IConfiguration>()["hostBuilder:reloadConfigOnChange"]`. It never touches `Environment`. Probes 1 and 2 fail it | met |
| 3 — setting reaches test hosts under quality.sh's coverage invocation | `quality.sh:53` = `dotnet test "$SLN" --nologo --collect:"XPlat Code Coverage" --results-directory "$COVERAGE_DIR"`, and no `--settings`, `export`, `DOTNET_` or `VSTEST` appears anywhere in `quality.sh` (search E5). Observed green inside the full run and **armed red under the same flags** (probe 2q) | met — see ruling 4 |
| 4 — measurements and the thin margin | record `:179-197`: 128; 113–115 desktop floor; Gateway.IntegrationTests 123 without / 118 with; full-run peak **118/128 cited as authoritative** from id 71 (`impl_operator_note…md:629-630`, `:676` confirm); the 116 stated only as a partial last-~50 s window; raising the limit "the user's decision, never the harness's, and was not made or proposed here" | met |
| 5 — no test depends on hot-reload, search re-run and pasted | record `:159-175`, narrower than id 71's search (**N1**); my id-71-equivalent re-run below is the search of record, and it is clean | met, via this review |

## Rulings on the leader's probes

**Probe 3 — does the guard read the host, not something weaker?** Yes. Changing the value alone, with the key still present, fails with `"true"`, so the assertion reads the value and not merely the key's presence. And it reads the value **as the host's configuration received it**: probe 1b shows that, with the harness broken, exporting the variable from the invoking shell makes the guard pass. An in-process `Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false")` running earlier in the same test assembly would mask it the same way, because `HostApplicationBuilder` reads `DOTNET_*` variables when the builder is created. That masking is correct for the property being guarded, since those hosts would genuinely have reload off. It only makes the failure message's attribution to `test.runsettings` wrong. **Nothing in the suite sets it:**

```
$ find src tests -type f \( -name '*.cs' -o -name '*.json' -o -name '*.csproj' -o -name '*.props' -o -name '*.targets' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "reloadConfigOnChange\|hostBuilder__\|hostBuilder:"
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:11:/// reload-on-change (<c>DOTNET_hostBuilder__reloadConfigOnChange=false</c>
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:25:/// <c>hostBuilder:reloadConfigOnChange</c> as <c>"false"</c>. It never reads
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:37:    /// <c>DOTNET_hostBuilder__reloadConfigOnChange</c> entry. Either mutation
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:62:        var reloadSetting = configuration["hostBuilder:reloadConfigOnChange"];
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:67:            "'hostBuilder:reloadConfigOnChange' to be \"false\", set by root " +
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:68:            "test.runsettings' DOTNET_hostBuilder__reloadConfigOnChange under " +
```
Classification: all six hits are the guard itself (four doc/message text, one read at :62, one message line). Zero writers, and none in `src/`, so no composition root sets the key either.

```
$ find tests src -type f \( -name '*.cs' -o -name '*.json' -o -name '*.csproj' -o -name '*.props' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'DOTNET_\|ASPNETCORE_'
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:11:/// reload-on-change (<c>DOTNET_hostBuilder__reloadConfigOnChange=false</c>
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:37:    /// <c>DOTNET_hostBuilder__reloadConfigOnChange</c> entry. Either mutation
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:68:            "test.runsettings' DOTNET_hostBuilder__reloadConfigOnChange under " +
src/Orders/OrdersHost.cs:70:        // ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT is unset — which is every
```
Classification: three guard text lines and one comment. No `DOTNET_` literal exists that any `Environment.SetEnvironmentVariable` could use.

```
$ find tests src -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "SetEnvironmentVariable(name\|SetEnvironmentVariable(string"
tests/Billing.UnitTests/BillingProgramConfigurationTests.cs:33:            Environment.SetEnvironmentVariable(name, null);
tests/Orders.UnitTests/OrdersProgramConfigurationTests.cs:34:            Environment.SetEnvironmentVariable(name, null);
tests/Projector.UnitTests/ProjectorProgramConfigurationTests.cs:35:            Environment.SetEnvironmentVariable(name, null);
tests/Seed.UnitTests/SeedDbConfigTests.cs:28:            Environment.SetEnvironmentVariable(name, null);
tests/Seed.UnitTests/SeedMongoConfigTests.cs:24:            Environment.SetEnvironmentVariable(name, null);
tests/Notifications.UnitTests/NotificationsProgramConfigurationTests.cs:31:            Environment.SetEnvironmentVariable(name, null);
tests/Fulfillment.UnitTests/FulfillmentProgramConfigurationTests.cs:31:            Environment.SetEnvironmentVariable(name, null);
tests/Gateway.UnitTests/OperatorIdentityLoaderTests.cs:31:            Environment.SetEnvironmentVariable(name, null);
tests/Gateway.UnitTests/GatewayMongoOptionsTests.cs:26:            Environment.SetEnvironmentVariable(name, null);
tests/Gateway.UnitTests/GatewayEnvironmentVariableTestCollection.cs:8:/// <see cref="Environment.SetEnvironmentVariable(string, string?)"/> shares
tests/Gateway.UnitTests/GatewayProgramConfigurationTests.cs:39:            Environment.SetEnvironmentVariable(name, null);
```
Classification: ten sites set `name` to **null**, a reset that can only remove a variable, never supply `false`; the eleventh is a `cref`. Every literal-named call site (208 lines across 16 files, from `grep -n "SetEnvironmentVariable"`) was grouped by name: 47 distinct names, all service config keys (`MSSQL_*`, `MONGO_*`, `KAFKA_*`, `NATS_*`, `JWT_*`, `GATEWAY_*`, `*_HEALTH_PORT`, `TZ`, …), none `DOTNET_*`. The ambient shell was also checked: `env | grep -i 'hostBuilder\|reloadConfig\|^DOTNET_\|^VSTEST'` prints only `DOTNET_BUNDLE_EXTRACT_BASE_DIR=…`, so the arming runs above were not masked.

**Probe 4 — is "the guard passed inside the coverage-collecting run" sufficient?** Not alone, and it is not alone here. A green observation proves the value was `false` in that run; it cannot say where the value came from. The shell might have supplied it, or `quality.sh` might have. And an observation that cannot fail is not a guard. Two things close that. First, `quality.sh` exports nothing (E5: the only hit for `export|DOTNET_|VSTEST|env ` is the shebang line `#!/usr/bin/env bash`), and the ambient shell does not carry the variable. Second, **probe 2q armed the path**: under `quality.sh`'s own flags, with the coverage collector demonstrably active, deleting the runsettings entry turns the guard red, and restoring it turns it green. The record's own quality-flags run was green-only; the review supplies the red. **Residual:** 2q ran at project level with `quality.sh`'s flags, not at solution level. Solution-level delivery is covered by the full run's green (445 includes the guard), by id 71 round 2's nine sampled solution-level test hosts all carrying the variable (`review_operator_note…md:625`), and by evaluation: `RunSettingsFilePath` is evaluated per project from `Directory.Build.props` either way. I accept that as sufficient.

**Probe 5 — escape enumeration.** Every hit is classified.

```
$ find . -type f \( -name '*.csproj' -o -name '*.props' -o -name '*.targets' -o -name '*.sh' -o -name '*.yml' -o -name '*.yaml' -o -name '*.json' -o -name '*.runsettings' -o -name '*.sln' -o -name '*.slnx' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -not -path './.git/*' -print0 | xargs -0 grep -n "RunSettingsFilePath\|--settings\|VSTestSetting\|ImportDirectoryBuildProps\|DirectoryBuildPropsPath"
./feature_list.json:1095:        "the change already in the tree is recorded as this entry's work, not id 71's: root test.runsettings sets …RunSettingsFilePath… (acceptance text)
./feature_list.json:1096:        "a guard that FAILS when the setting stops reaching test hosts: … armed by deleting the RunSettingsFilePath line, … (acceptance text)
./Directory.Build.props:28:      why. Harmless for non-test (src/) projects: RunSettingsFilePath is
./Directory.Build.props:31:    <RunSettingsFilePath>$(MSBuildThisFileDirectory)test.runsettings</RunSettingsFilePath>
./test.runsettings:4:  RunSettingsFilePath — both `dotnet test OrderToCash.sln` (quality.sh's own
./test.runsettings:6:  because RunSettingsFilePath is an MSBuild property read by the VSTest
```
(The two `feature_list.json` lines are abbreviated here; they are id 77's own acceptance bullets 1 and 2, quoted in full above.) Classification: two backlog prose, two comments, **one** setter (`Directory.Build.props:31`). No `--settings` invocation anywhere, and no project opts out of `Directory.Build.props` via `ImportDirectoryBuildProps`.

```
$ find . \( -iname 'Directory.Build.*' -o -iname '*.runsettings' -o -iname 'Directory.Packages.props' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -not -path './.git/*' -print
./Directory.Build.props
./test.runsettings
./Directory.Packages.props
$ find tests -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "<Import\|RunSettings"
(no output, exit 123)
$ find tests -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print | sort | wc -l
18
```
Classification: one `Directory.Build.props` and one runsettings file, both at the root, and no nested `Directory.Build.*` under `tests/` or `src/`. All 18 test projects: `Architecture.Tests`, `Billing.{Integration,Unit}Tests`, `Contracts.UnitTests`, `Cqrs.UnitTests`, `Fulfillment.{Integration,Unit}Tests`, `Gateway.{Integration,Unit}Tests`, `Notifications.{Integration,Unit}Tests`, `Orders.{Integration,Unit}Tests`, `Projector.{Integration,Unit}Tests`, `Seed.{Integration,Unit}Tests`, `SharedKernel.UnitTests`. None has an `<Import>` or its own `RunSettings*`. Test-platform check: `global.json` pins only the SDK, with no `test.runner`. `Directory.Packages.props:16-17` uses `xunit` 2.9.2 plus `xunit.runner.visualstudio` 2.8.2, i.e. VSTest, and no csproj mentions `TestingPlatform`, `xunit.v3`, `UseMicrosoft…` or `OutputType` (exit 123). So no project runs under a platform that ignores `RunSettingsFilePath`.

**Probe 6 — hot-reload search, id-71-equivalent and wider.** This is the search of record for bullet 5.

```
$ find src tests -type f -iname 'appsettings*.json' -not -path '*/bin/*' -not -path '*/obj/*' -print
(no output)
$ find src tests -type f -name '*.json' -not -path '*/bin/*' -not -path '*/obj/*' -print
tests/Contracts.UnitTests/GoldenEnvelopes/{order_completed,stock_released,order_despatched,payment_received,order_cancelled,order_placed,credit_rejected,order_confirmed,credit_released,credit_approved,stock_reserved,invoice_issued}_v1.json   (12 files, listed individually by the command)
tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "IOptionsMonitor\|OptionsMonitor\|\.OnChange(\|GetReloadToken\|ChangeToken\|FileSystemWatcher\|PhysicalFileProvider\|reloadOnChange\|ReloadOnChange\|AddJsonFile\|AddIniFile\|AddXmlFile"
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:16:/// opens one inotify instance (a <c>FileSystemWatcher</c>) per
$ find tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "File\.Write\|File\.AppendAll\|File\.Create\|new StreamWriter\|File\.Copy\|File\.Move\|appsettings"
tests/Billing.UnitTests/CreditRpcPayloadTests.cs:82:            File.WriteAllText(scratchPath, corrupted);
tests/Billing.UnitTests/BillingConsumesNoFactsTests.cs:92:            File.WriteAllText(scratchFile, "// probe: …");
tests/Billing.UnitTests/CentsRuleFixtureGuardTests.cs:47:            File.WriteAllLines(scratchFile,
tests/Orders.UnitTests/SagaCommandPayloadTests.cs:252:            File.WriteAllText(scratchPath, corrupted);
tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs:163:            File.WriteAllText(scratchPath, corrupted);
tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs:121:            File.WriteAllText(scratchPath, corrupted);
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:17:/// <c>appsettings*.json</c>, and this repository builds ~12 such projects in
tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs:72:            "builds opens one inotify instance per appsettings*.json watcher " +
tests/Orders.UnitTests/OrdersCancelPayloadTests.cs:88:            File.WriteAllText(scratchPath, corrupted);
tests/Projector.UnitTests/ReadModelSoleWriterTests.cs:69:            File.WriteAllText(
tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs:188:            File.WriteAllText(scratchPath, corrupted);
tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs:118:            File.WriteAllText(scratchPath, corrupted);
```
Classification:
- **No config file exists to reload.** Zero `appsettings*.json` under `src/` or `tests/`. The 13 JSON files are wire fixtures, not host configuration sources.
- **The one reload-API hit is the guard's own doc comment** (`:16`). There is no reader of `IOptionsMonitor`, a change token or a file provider in `src/` or `tests/`, and no explicit `AddJsonFile`.
- **The ten file writers are exactly id 71 round 2's ten, unchanged**: corrupted scratch payloads, scratch source files, and a scratch `RogueWriter.cs`. None writes a file a host reads. The other two lines are the guard's doc text.

**Probe 7 — bullet 4.** Confirmed as tabled above. The 118/128 full-run peak is cited as authoritative (record `:186-188`, `:192-193`), and id 71's record agrees (`:629-630` for 123/118, `:676` for 118). The 116 is labelled a partial last-~50 s window, not a peak (`:189-192`). Raising the limit is the user's decision (`:194-197`, and again at `:234-236`).

**Probe 8 — is one guard in `Orders.UnitTests` enough?** For the tree as it stands, yes. Probe 5 shows **one** delivery path shared by all 18 projects: one setter, no per-project override, no nested `Directory.Build.*`, no `--settings`, no MTP runner. Both global breakages the acceptance names are therefore visible from any single project, and they were seen to fail from this one. What a single-project guard **cannot** see is a *per-project* escape introduced later: a new test csproj setting its own `RunSettingsFilePath` or `ImportDirectoryBuildProps=false`, or a script passing `--settings`. That is advisory **A1**. It is not blocking, for three reasons. The acceptance asks for a guard of the global delivery, which exists and is armed. Today's escape set is empty by search, not by reading. And the escape's consequence is the loud `IOException` naming inotify, on a machine near the limit, rather than a wrong result.

## Findings

**N1 — non-blocking, record text.** `progress/impl_test_hosts_exhaust_the_per_user_inotify_limit.md:165` narrows the search bullet 5 names. The reload-reader leg searches `tests` only, where id 71's covered `src tests`, and uses 2 of id 71's 11 terms (`IOptionsMonitor|GetReloadToken`, dropping `ChangeToken`, `FileSystemWatcher`, `PhysicalFileProvider`, `reloadOnChange` and more). The "no test writing a config file" leg (`:168`) greps only `appsettings`, where id 71 classified file writers. It is presented under the heading *"re-run"*. The conclusion is true (probe 6 above, wider than id 71's), so it costs nothing to ship. But this is the shape CLAUDE.md warns about: a narrower sweep presented as the prior one. It would have missed an `IOptionsMonitor` consumer in any `src/` composition root. **Recommended:** the leader adds a one-line pointer at record `:159` to this review's probe 6 as the search of record.

**N2 — non-blocking, record text, a count that does not reconcile.** Record `:219-220` says *"every other of the 17 projects' totals is unchanged from id 71's table"* and then lists **16** values, omitting `Billing.IntegrationTests` = **90**. The claim itself is true: the final log has 90, and id 71's record has 90 at `:458` and `:699`. The sum 1821 reconciles with 90 included (50+23+24+238+211+130+445+82+44+120+25+6+16+64+59+90+59+135). **Recommended:** add the missing `90` to the list at `:220`.

**A1 — advisory, design limit of a single-project guard.** Stated under probe 8. If the leader wants the per-project escape class closed rather than accepted, the shape is a structural test that enumerates every `tests/**/*.csproj` (a literal expected set of 18, per CLAUDE.md's *literal set, derive by subtraction*). It would assert that none sets `RunSettingsFilePath` or `ImportDirectoryBuildProps`, and that no `*.sh`/`*.yml` passes `--settings`. That is a leader decision to file as its own entry or explicitly accept. It is not a condition of this approval.

**A2 — advisory, what the guard's message can misattribute.** Probe 1b: with the harness broken and the variable supplied by the invoking shell, the guard passes. That is correct for the property, but it would silently mask a harness break on a machine or CI runner that exports the variable. The guard's message names `test.runsettings` as the source and cannot verify that. No suite code or shell setting does it today (probe 3's searches). No change required; noted so a future red-free run on such a machine is not read as proof of delivery.

**A3 — commit-time, for the leader.** `test.runsettings` and `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs` are **untracked** (`git status --short`: `??` both; `git check-ignore -v` exit 1, not ignored). `Directory.Build.props` is `M`. A commit that stages the props change without `test.runsettings` points every test project at a non-existent settings file. The three must go in together.

**Id 71's bypass caveat** (review round 2 `:627-630`): `dotnet vstest`, a direct assembly run and IDE runners that ignore `RunSettingsFilePath`. Its suggested acceptance item *"the bypass paths stated in `test.runsettings`' own comment"* was **not** carried into id 77's acceptance, and `test.runsettings:2-19` does not state them. Not a defect against this entry's binding acceptance; recorded so the omission is visible rather than assumed closed.

**`specs/shared/`:** nothing here is rooted in it. No `SA-n` is owed.
```
$ find specs/shared -type f -print0 | xargs -0 grep -n -i "inotify\|runsettings\|reloadConfigOnChange\|reloadOnChange\|hostBuilder\|RunSettingsFilePath"
(no output, exit 123 — searched asyncapi.yaml, domain-model.md, n8n-workflows.md, openapi.yaml, requirements.md, saga.md, test-matrix.md)
```

**Ported-idiom ledger:** not owed; no #7 mechanism is ported.
```
$ cd ../order-to-cash-nestjs && find . -type f \( -name 'feature_list.json' -o -name '*.md' -o -name '*.json' -o -name '*.ts' -o -name '*.sh' \) -not -path '*/node_modules/*' -not -path './.git/*' -not -path '*/dist/*' -print0 | xargs -0 grep -ln -i "inotify\|max_user_instances\|reloadConfigOnChange\|runsettings"
(no output, exit 123)
```

## `CHECKPOINTS.md`

- **C1** — [x] harness files exist. [ ] `./init.sh` exits 0: **exit 1** after my status edit, on one line only — `[FAIL] progress/current.md claims a feature while none is active: "**Feature:** test_hosts_exhaust_the_per_user_inotify_limit (id 77, phase 14)"`. This is the §4 lockstep; the leader resets `progress/current.md` (I may not). The leader reported exit 0 before the transition.
- **C2** — [x] at most one `in_progress`, and every status is valid: `init.sh` raised no backlog-coherence failure, only the lockstep above. [x] id 77 now `done` with a passing, armed test. [ ] `progress/current.md` still names id 77 (leader's).
- **C3** — [x] no `src/` file changed by this entry (its files are one test, `Directory.Build.props` and `test.runsettings`); `Architecture.Tests` 25 passed in the leader's log (not re-run). [x] no shared runtime code: `test.runsettings` is harness. Domain purity, cross-service DB, decimal and Kafka/NATS classification: n/a, untouched.
- **C4** — [x] `./quality.sh` passed per the leader's log (not re-run; summed at 1821). [x] no Jest; xUnit 2.9.2. Domain purity, Testcontainers: n/a (a unit test against unreachable fake addresses that never connects). [ ] coverage thresholds: not walked. This entry adds one test and no production code, and the log's §4 prints per-report percentages only, so I did not rule on the gate.
- **C5** — [x] no stray files: the two untracked files are this entry's (A3). [x] history entry with effort record appended. [x] `feature_list.json` id 77 `done`: one line, `:1093`. `git diff -U0` hunk list unchanged at `406, 408, 415, 468, 862, 992, 998, 1000`, because id 77 sits inside the leader's own uncommitted addition `@@ -998 +1001,115`. [x] no commit made.
- **C6** — n/a (`sdd: false`).
- **C7** — [x] this entry does not touch `specs/shared/` (search above; the `M specs/shared/test-matrix.md` in `git status` predates it). [x] effort record honest; #8-only, no ratio.

## Effort

One implementer pass, 11:38:19 → 11:59:45 CEST (≈21 min, transcript timestamps). It included the full `quality.sh`, whose log was born 11:44:46 and closed 11:57:07. The leader's read of the log took ≈1 min. This review ran 12:00:52 → 12:12. No fix round.
