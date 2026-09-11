# impl: test_hosts_exhaust_the_per_user_inotify_limit (id 77, phase 14, sdd: false)

## What already existed (built during id 71, carried unguarded into this entry)

- Root `test.runsettings` (new file at id 71, still untracked): sets
  `DOTNET_hostBuilder__reloadConfigOnChange=false` under
  `RunConfiguration/EnvironmentVariables`.
- `Directory.Build.props`: `<RunSettingsFilePath>$(MSBuildThisFileDirectory)test.runsettings</RunSettingsFilePath>`,
  added to the existing root `PropertyGroup`.
- Both files were already in the tree when this entry started (git status:
  `M Directory.Build.props`, `?? test.runsettings`). This entry's job was the
  guard id 71's review round 2 said was missing, plus the residual
  quality.sh-path check, plus the two search-result re-runs. Neither file's
  *content* needed to change for this feature — only a guard needed to be
  added that fails when either is broken.

## What was built

One new file: `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs`, one
test:

**`HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled`**

Builds a REAL host the way the suite does — `OrdersHost.CreateBuilder(...)`,
the SAME composition root `Program.cs` calls (already factored out at id 71's
predecessor, review D6 round 2) — with the same fake, unreachable
connection strings `OrdersDispatcherRegistrationTests.BuildRealHostBuilder`
already uses (no real MS-SQL/Kafka/NATS needed; `ValidateOnBuild` checks the
DI graph, it does not connect). Calls `.Build()`, then reads the built host's
OWN `IConfiguration` — `host.Services.GetRequiredService<IConfiguration>()["hostBuilder:reloadConfigOnChange"]`
— and asserts it is `"false"`. It never reads or sets the process environment
itself (that would prove only that the test's own environment has the
variable, not that a host built the way the suite builds one receives it via
`test.runsettings`/`RunSettingsFilePath`). The host is disposed (`using`).

The assertion message names the setting
(`hostBuilder:reloadConfigOnChange`), its source
(`DOTNET_hostBuilder__reloadConfigOnChange` under root `test.runsettings`'
`RunConfiguration/EnvironmentVariables`) and its delivery mechanism
(`Directory.Build.props`' `RunSettingsFilePath`), and reports the actual
value observed.

## Arming table

Protocol followed exactly: `cp` backup → mutate → `dotnet build --no-incremental`
of the test project → plain `dotnet test` (no explicit `--settings`, so
`RunSettingsFilePath` is the only thing that can deliver the variable) on the
one named test → record verbatim → restore from the backup → `cmp` against
the backup → forced rebuild (`touch` + `dotnet build --no-incremental`) →
confirming green run. Never `git checkout --`; both mutated files (`Directory.Build.props`,
`test.runsettings`) are dirty/untracked in this session, so `git checkout`
would either restore the wrong baseline or fail outright.

### Arm (i): delete the `<RunSettingsFilePath>` line from `Directory.Build.props`

Backup: `cp Directory.Build.props /tmp/.../scratchpad/Directory.Build.props.bak`.
Mutation: removed the `<RunSettingsFilePath>...</RunSettingsFilePath>` line
(and its preceding comment) from the root `PropertyGroup`.
`dotnet build --no-incremental tests/Orders.UnitTests/Orders.UnitTests.csproj`
→ Build succeeded, 0 warnings, 0 errors.
`dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --nologo --filter "FullyQualifiedName~HostInotifyReloadGuardTests"`:

```
[xUnit.net 00:00:00.45]     OrderToCash.Orders.UnitTests.HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled [FAIL]
  Failed OrderToCash.Orders.UnitTests.HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled [115 ms]
  Error Message:
   Expected the built host's own configuration key 'hostBuilder:reloadConfigOnChange' to be "false", set by root test.runsettings' DOTNET_hostBuilder__reloadConfigOnChange under RunConfiguration/EnvironmentVariables and applied to every test project via Directory.Build.props' RunSettingsFilePath, but it was "<null>". Every real host a test builds opens one inotify instance per appsettings*.json watcher when this setting is not disabled, and a parallel quality.sh run exhausts this machine's fs.inotify.max_user_instances.

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 115 ms - OrderToCash.Orders.UnitTests.dll (net10.0)
```

Restore: `cp` the backup back over `Directory.Build.props`. `cmp backup live`
→ **IDENTICAL** (no output, exit 0). `touch Directory.Build.props` (forced
rebuild), `dotnet build --no-incremental` → Build succeeded. Confirming run
(same filter) → **Passed! 1/1**, 104 ms.

### Arm (ii): delete the `DOTNET_hostBuilder__reloadConfigOnChange` entry from `test.runsettings`

Backup: `cp test.runsettings /tmp/.../scratchpad/test.runsettings.bak`.
Mutation: removed the `<DOTNET_hostBuilder__reloadConfigOnChange>false</DOTNET_hostBuilder__reloadConfigOnChange>`
line, leaving an empty `<EnvironmentVariables>` element.
`dotnet build --no-incremental tests/Orders.UnitTests/Orders.UnitTests.csproj`
→ Build succeeded, 0 warnings, 0 errors (the runsettings mutation is
read at test-run time, not build time, but the build was re-run anyway per
protocol).
`dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --nologo --filter "FullyQualifiedName~HostInotifyReloadGuardTests"`:

```
[xUnit.net 00:00:00.55]     OrderToCash.Orders.UnitTests.HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled [FAIL]
  Failed OrderToCash.Orders.UnitTests.HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled [122 ms]
  Error Message:
   Expected the built host's own configuration key 'hostBuilder:reloadConfigOnChange' to be "false", set by root test.runsettings' DOTNET_hostBuilder__reloadConfigOnChange under RunConfiguration/EnvironmentVariables and applied to every test project via Directory.Build.props' RunSettingsFilePath, but it was "<null>". Every real host a test builds opens one inotify instance per appsettings*.json watcher when this setting is not disabled, and a parallel quality.sh run exhausts this machine's fs.inotify.max_user_instances.

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 122 ms - OrderToCash.Orders.UnitTests.dll (net10.0)
```

Restore: `cp` the backup back over `test.runsettings`. `cmp backup live` →
**IDENTICAL** (no output, exit 0). `touch test.runsettings`, forced rebuild
(`dotnet build --no-incremental`) → Build succeeded. Confirming run (same
filter) → **Passed! 1/1**, 106 ms.

Both arms fail the same named test, naming the setting and its source, and
both restores were confirmed byte-identical to the pre-mutation backup before
the forced rebuild and the confirming green run.

## The quality.sh-path check (acceptance bullet 3 / review round 2's residual (b))

`quality.sh` (§3) invokes `dotnet test "$SLN" --nologo --collect:"XPlat Code Coverage" --results-directory "$COVERAGE_DIR"`
— no `--settings` flag anywhere in the script (confirmed: `grep -n -- '--settings' quality.sh`
returns nothing). `--settings` is the only command-line flag that overrides
`RunSettingsFilePath` in VSTest's precedence rules; since quality.sh never
passes it, `RunSettingsFilePath` (an MSBuild property read from
`Directory.Build.props`, inherited by every project including under a
solution-level `dotnet test`) is not overridden by quality.sh's invocation.

**Observed directly**, not just reasoned about: ran the guard test through
quality.sh's own exact flags — `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --nologo --collect:"XPlat Code Coverage" --results-directory <dir> --filter "FullyQualifiedName~HostInotifyReloadGuardTests"`
— and it passed:

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 95 ms - OrderToCash.Orders.UnitTests.dll (net10.0)
```

Then confirmed again inside a full, unfiltered `./quality.sh` run (see
Verification below): `HostInotifyReloadGuardTests` is part of
`Orders.UnitTests`' 445 passing tests in that run's `[OK] dotnet test: all
tests passed` line, with 0 `Failed!` lines and 0 `IOException`/`inotify`/`user
limit` occurrences anywhere in the 21 KB log. The setting reaches test hosts
under quality.sh's own coverage-collecting invocation exactly as it does
under a plain `dotnet test`.

## Step 4: enumeration of test projects that could escape the setting

```
find tests -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print
find tests -iname 'Directory.Build.*' -not -path '*/bin/*' -not -path '*/obj/*' -print
find tests \( -name '*.csproj' -o -iname 'Directory.Build.*' -o -name '*.props' -o -name '*.targets' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln "RunSettingsFilePath" 2>/dev/null
find . \( -name '*.sh' -o -name '*.yml' -o -name '*.yaml' -o -iname 'Directory.Build.*' -o -name '*.csproj' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -print0 | xargs -0 grep -n -- '--settings' 2>/dev/null
```

Output: 18 `*.csproj` files under `tests/` (`Billing.UnitTests`,
`Seed.IntegrationTests`, `Fulfillment.IntegrationTests`,
`Billing.IntegrationTests`, `Orders.UnitTests`, `SharedKernel.UnitTests`,
`Projector.UnitTests`, `Seed.UnitTests`, `Contracts.UnitTests`,
`Cqrs.UnitTests`, `Orders.IntegrationTests`, `Notifications.UnitTests`,
`Fulfillment.UnitTests`, `Gateway.UnitTests`,
`Notifications.IntegrationTests`, `Projector.IntegrationTests`,
`Architecture.Tests`, `Gateway.IntegrationTests`) — **classification: none of
the 18 sets its own `RunSettingsFilePath`** (the `RunSettingsFilePath` grep
against every `.csproj`/`Directory.Build.*`/`.props`/`.targets` under `tests/`
returned no hits). No `Directory.Build.*` file exists anywhere under `tests/`
(the second `find` returned nothing) — every test project inherits the root
`Directory.Build.props` unmodified. The `--settings` grep across every
`.sh`/`.yml`/`.yaml`/`Directory.Build.*`/`.csproj` in the repository (bin/obj/
node_modules excluded by path) returned nothing — no invocation anywhere in
this repository passes `--settings`, so none can override
`RunSettingsFilePath`. All 18 projects share one delivery path.

## Step 5: "no test depends on configuration hot-reload" — re-run

> **Narrower than its heading claims (leader note after review round 1, N1).** This is not a re-run of id 71's round-2 search. It covers `tests/` only, not `src/`, and uses 2 of that search's 11 terms. It also replaces the search for tests that *write* a config file mid-run with a plain `appsettings` grep. The search of record is the reviewer's wider one (`progress/review_test_hosts_exhaust_the_per_user_inotify_limit.md`, probe 6), which is clean. The narrower commands below are kept as written.

```
find src tests \( -iname 'appsettings*.json' \) -not -path '*/bin/*' -not -path '*/obj/*' -print
→ (no output — none found)

find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln 'IOptionsMonitor\|GetReloadToken' 2>/dev/null
→ (no output — none found)

find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln 'appsettings' 2>/dev/null
→ tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs
```

Classification of the one hit: this feature's own guard test's doc comment
(explaining WHY the setting matters — "one inotify instance ... per
`appsettings*.json`"), not a test writing or reading a config file mid-run.
No test in the repository depends on configuration hot-reload.

## Measurements (carried forward from id 71, restated here per acceptance bullet 4)

- `fs.inotify.max_user_instances` = 128 on this machine.
- 113–115 inotify instances held by desktop processes at rest (gnome-shell,
  tracker-miner, ibus-\*, evolution-\*, gvfs-\*, kwalletd5, postman-agent, …
  — confirmed none are `dotnet`).
- `Gateway.IntegrationTests` alone, same conditions, sampled every 1 s:
  peaked at **123/128 without** the fix, **118/128 with** it (both runs
  59/59 green).
- The full `quality.sh` run, sampled every 5 s throughout the ~30-minute run
  (id 71's close-out), peaked at **118/128** — 10 below the limit, 3 above
  the desktop floor.
- This entry's own full `quality.sh` run (below) was not separately sampled
  at 5 s resolution for its whole duration — a partial sampler covering only
  the run's last ~50 s recorded a peak of 116/128, which is reported here
  only as a partial-window observation, not as this run's full peak. The
  authoritative full-run peak remains id 71's measured **118/128**.
- **Thin-margin statement:** ~10 spare inotify instances stand between the
  measured full-run peak and the machine's limit. Raising
  `fs.inotify.max_user_instances` beyond 128 is the user's decision, never
  the harness's, and was not made or proposed here.

## Verification

- `dotnet format OrderToCash.sln --verify-no-changes` → exit 0, no output
  (clean).
- Full `./quality.sh`, run once, in the background, waited on by PID (never
  `pgrep -f`), no other build/test/format process alive concurrently:

```
[OK]    dotnet format --verify-no-changes: clean
[OK]    dotnet build: succeeded
[OK]    dotnet test: all tests passed
[OK]    quality.sh finished
```

  18 projects' `Passed!` totals sum to **1821** (`grep -oE 'Total:\s*[0-9]+' | ...` →
  SUM: 1821, 18 dlls), 0 `Failed!` lines, 0
  `IOException`/`inotify`/`user limit` occurrences anywhere in the log.
  **Reconciliation against id 71's own close-out total: 1820 + 1 (this
  entry's new guard test) = 1821.** Per-project delta: `Orders.UnitTests`
  went from 444 (id 71's own table) to **445** (+1, exactly this entry's new
  test); every other of the 17 projects' totals is unchanged from id 71's
  table (50, 23, 24, 130, 82, 211, 238, 44, 6, 16, 64, 120, 59, 25, 135, 59, and `Billing.IntegrationTests` 90). *(Corrected by the leader after review round 1, N2: the list originally named 16 totals for the "17 projects" it described, omitting `Billing.IntegrationTests` = 90. The sum reconciled to 1821 either way.)*
- `./init.sh` → `══ init.sh: environment and state are coherent ══`, no
  errors; backlog coherence, SDD coherence, backlog tripwire, commit-msg
  hook and shared-spec parity all `[OK]`.
- `git diff -U0 -- Directory.Build.props`: shows only the `RunSettingsFilePath`
  block already added at id 71 (9 lines, the comment + the property) — no
  residue from arming.
- `git diff -U0 -- test.runsettings`: empty (the file is untracked, restored
  byte-identical to its pre-mutation state).
- New test file `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs` is
  untracked (`??`), as expected for a new file mid-session.

## What was not done, and why

- The system's `fs.inotify.max_user_instances` limit was not raised —
  explicitly out of scope, the user's decision per the spec's own acceptance
  bullet 4.
- No second, separately-sampled full `quality.sh` run with 1 s/5 s inotify
  sampling for its entire ~30-minute duration was performed for this entry;
  id 71's own 5 s-throughout sample (118/128 peak) is cited as the
  authoritative full-run measurement instead of re-measuring, since nothing
  about the fix's mechanism changed in this entry — only a guard was added
  around it.

## Files touched

- `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs` — new, one test.
- `Directory.Build.props`, `test.runsettings` — mutated and restored twice
  each for arming only; both confirmed `cmp`-identical to their pre-mutation
  state before the confirming green runs. No net change to either file's
  content from this entry.
