# impl — backlog id 96 `gateway_ignores_gateway_port`

**Result: PASS.** The Gateway now reads `GATEWAY_PORT` (default 3001) and listens on it. `Gateway.UnitTests` **245/245** (was 237: +2 in `GatewayProgramConfigurationTests`, +6 in the new `GatewayListenPortTests`). `Gateway.IntegrationTests` **70/70** (unchanged count; includes all five `SagaEndToEndVerificationTests`). `Architecture.Tests` **50/50**. `dotnet format --verify-no-changes` on `src/Gateway/` and `tests/Gateway.UnitTests/` exits 0. `./init.sh` exits 0. The live `dev-stack.sh` check passed without `ASPNETCORE_URLS`.

`feature_list.json` was not touched, as the brief required. The status transition is the leader's to make.

## The fix

| File | Change |
|---|---|
| `src/Gateway/Infrastructure/GatewayOptions.cs` | New `int? Port`. `null` means no port was chosen. |
| `src/Gateway/GatewayProgramConfiguration.cs` | `Configure` now sets `options.Port = int.TryParse(Environment.GetEnvironmentVariable("GATEWAY_PORT"), out var port) ? port : 3001;`. This mirrors #7's `apps/gateway/src/main.ts:30` (`Number(process.env.GATEWAY_PORT ?? 3001)`) and uses the same TryParse-or-default shape as the five sibling `*_HEALTH_PORT` reads. |
| `src/Gateway/GatewayHost.cs` | New private `ApplyListenPort`, called from `CreateBuilder` right after `configure(options)`. If `options.Port` has a value and no explicit `urls` setting exists, it calls `builder.WebHost.UseUrls($"http://+:{port}")`. |
| `tests/Gateway.UnitTests/GatewayProgramConfigurationTests.cs` | `_envVars` now includes `GATEWAY_PORT` and its six siblings. Adds two tests (default and substitution) and a `Describe` helper that names which key was read. |
| `tests/Gateway.UnitTests/GatewayListenPortTests.cs` (new) | Six tests for the binding and its precedence. Uses the existing environment-variable collection because the tests change `ASPNETCORE_URLS`. |
| `scripts/dev-stack.sh` | Removed the `ASPNETCORE_URLS="http://localhost:${GATEWAY_PORT}"` prefix. Replaced the stale header comment ("The Gateway does not read GATEWAY_PORT…") with one saying the Gateway listens on `GATEWAY_PORT` itself and that an `ASPNETCORE_URLS` exported in the shell overrides it. Restructured `cmd_start_service`: `web` now `return`s, and the Gateway's port-in-use check is a separate guard before the shared `dotnet run` line. |

`Program.cs` is unchanged. The read lives inside the existing `Configure` delegate, so `CompositionRootDelegationWiringTests`' expected delegation set (Gateway 2) still holds. That suite passed 50/50.

### Where the binding lives, and why

- **The read** is in `GatewayProgramConfiguration.Configure`, the method `Program.cs` calls and the unit tests can call directly. Acceptance bullet 1 names this method.
- **The binding** is in `GatewayHost.CreateBuilder`, the only place that has both the options and the `WebApplicationBuilder`. The sibling services also bind in their composition (`builder.WebHost.UseUrls(...)` in each `HealthProbeService`).
- **Every interface (`http://+:port`)**, because #7's `app.listen(port)` passes no host, which binds all interfaces. #7's apps compose also publishes `${GATEWAY_PORT}:${GATEWAY_PORT}` from a container, which needs a non-loopback bind. The sibling health ports bind `127.0.0.1`, but they are local probe surfaces, not the public API.
- **`Port` is `int?` and has no initialiser.** A caller-supplied `configure` delegate that does not choose a port leaves Kestrel's own URL configuration untouched. Every test host supplies its own delegate, so the default can never leak into a test. Only `GatewayProgramConfiguration.Configure`, the production path, always sets a value.

### Test-host decision

I listed every way a test builds a Gateway: `grep -rn "WebApplicationFactory|TestServer|UseTestServer|UseUrls|GatewayHost\." src tests`.
- **Nothing uses `TestServer` or `WebApplicationFactory`.** There were no hits.
- **Every `GatewayHost.CreateBuilder` call passes `--urls http://127.0.0.1:0`**:
  - `GatewayTestHost.StartAsync:66`, the only real-Kestrel host, used by every integration suite including `SagaEndToEndVerificationTests:734`
  - `DocsAndAnonymousRouteHttpTests:57`
  - `OpenApiContractTests:84`
  - `GatewayDispatcherRegistrationTests:51`

  None of these delegates sets `Port` either.
- **No test-host change was needed.** An explicit `urls` setting takes precedence (next section), and `Port` defaults to `null`, so these hosts cannot bind 3001. The 70/70 integration run confirms it. `CreateBuilder_LeavesAnExplicitUrlsArgumentUntouched_EvenWhenAPortIsConfigured` guards the precedence, and arm A6 shows it failing.

### `ASPNETCORE_URLS` precedence decision

**An explicit `urls` setting wins over `GATEWAY_PORT`.** That covers `--urls`, `ASPNETCORE_URLS` and `DOTNET_URLS`, which all land in `builder.Configuration["urls"]`. Reasons:
1. It is .NET's own explicit, full-URL override (scheme, host and port), and overriding it silently would be surprising.
2. It is the seam every in-process test host uses to get an ephemeral port. Letting `GATEWAY_PORT` override it would put every test host on 3001, which is the collision risk the brief named. Arm A6 shows the two precedence tests failing under that mutation.

**`ASPNETCORE_HTTP_PORTS` / `DOTNET_HTTP_PORTS` do not count as explicit.** The official ASP.NET Core container images set `ASPNETCORE_HTTP_PORTS=8080` by default. Respecting it would silently defeat `GATEWAY_PORT` in the one environment that declares it, and `UseUrls` already takes precedence over it. `CreateBuilder_StillBindsGatewayOptionsPort_WhenOnlyAspNetCoreHttpPortsIsSet` guards this (arm A8).

The `dev-stack.sh` header comment now says an `ASPNETCORE_URLS` exported in the shell overrides `GATEWAY_PORT`.

## Tests and what each proves

| Test | Proves |
|---|---|
| `GatewayProgramConfigurationTests.Configure_DefaultsGatewayPortTo3001_WhenGatewayPortIsUnset` | Bullet 1: the default is 3001, as in #7 main.ts:30 |
| `GatewayProgramConfigurationTests.Configure_ReadsGatewayPort_FromItsOwnDistinctVariableName_NeverASiblingsKey` | Bullets 2 and 5: `GATEWAY_PORT=13001`, with `ORDERS/FULFILLMENT/BILLING/NOTIFICATIONS/PROJECTOR_HEALTH_PORT` and `WEB_PORT` set to 23002–23006 and 23010. The failure message names the key actually read. A deleted read fails with "never read". |
| `GatewayListenPortTests.StartedHost_ListensOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured` | Bullet 1, "the host listens on it": a real Kestrel start with `Port=0` reports an every-interface address and answers `GET /health/live` 200 |
| `GatewayListenPortTests.CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured` | The exact port number reaches Kestrel's `urls` (`http://+:13001`) |
| `GatewayListenPortTests.CreateBuilder_LeavesAnExplicitUrlsArgumentUntouched_EvenWhenAPortIsConfigured` | Bullet 3: the test-host seam (`--urls`) wins |
| `GatewayListenPortTests.CreateBuilder_LeavesAnExplicitAspNetCoreUrlsVariableUntouched_EvenWhenAPortIsConfigured` | `ASPNETCORE_URLS` wins (precedence decision) |
| `GatewayListenPortTests.CreateBuilder_StillBindsGatewayOptionsPort_WhenOnlyAspNetCoreHttpPortsIsSet` | `ASPNETCORE_HTTP_PORTS` does not win (precedence decision) |
| `GatewayListenPortTests.CreateBuilder_SetsNoUrls_WhenNoPortIsConfigured` | A `null` port invents no binding, which is the property that keeps test hosts safe |

**The real-socket test uses `Port=0`, not a pre-chosen free port.** My first version picked one with a `TcpListener` on port 0. `Architecture.Tests.ContainerFixtureHostPortAssignmentTests.ExactlyOneMethodInTheRepositoryStillConstructsATcpListener` (backlog id 85) rejected it: *"Unexpected listener-constructing method(s): tests/Gateway.UnitTests/GatewayListenPortTests.cs::FreeLoopbackPort"*. I rewrote the test. Kestrel's own default is `http://localhost:5000`, so a host that ignores the option reports that address and fails the every-interface pattern. The exact-number claim moved to the `CreateBuilder_SetsUrls…` test, and arm A10 shows it failing.

## Arming table

For each arm I took a `cp -p` backup, applied the mutation, ran `dotnet build tests/Gateway.UnitTests --no-incremental`, ran the named test alone, restored with `cp`, touched the file and compared it with `cmp` (every restore printed `RESTORED`). The script is `scratchpad/arm.sh`. After all arms I read the restored lines back (`GatewayHost.cs:126,131,136`, `GatewayProgramConfiguration.cs:35`), compared both files byte-for-byte with their pre-arm backups (`host_same_as_pre_arm`, `cfg_same`), ran `--no-incremental` again, and got 245/245 green.

| Arm | Mutation | Family | Named test | Verbatim failure |
|---|---|---|---|---|
| A1 | Deleted the `options.Port = …GATEWAY_PORT…` line | deletion | `Configure_ReadsGatewayPort_FromItsOwnDistinctVariableName_NeverASiblingsKey` | `GatewayOptions.Port must come from GATEWAY_PORT (=13001); got null — GATEWAY_PORT was never read.` |
| A2 | `"GATEWAY_PORT"` → `"ORDERS_HEALTH_PORT"` | substitution | same | `GatewayOptions.Port must come from GATEWAY_PORT (=13001); got 23002 — the value of ORDERS_HEALTH_PORT, so the read is repointed at ORDERS_HEALTH_PORT.` |
| A3 | `"GATEWAY_PORT"` → `"WEB_PORT"` | substitution | same | `… got 23010 — the value of WEB_PORT, so the read is repointed at WEB_PORT.` |
| A4 | Default `3001` → `3000` | corruption | `Configure_DefaultsGatewayPortTo3001_WhenGatewayPortIsUnset` | `GATEWAY_PORT unset must default GatewayOptions.Port to 3001 (#7 main.ts:30); got 3000.` |
| A5 | Deleted `builder.WebHost.UseUrls(...)` | deletion | `StartedHost_ListensOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured` | `Assert.Matches() Failure: Pattern not found in value` / `Value: "http://localhost:5000"` |
| A6 | Precedence check disabled (`if (false && …)`) | deletion | `CreateBuilder_LeavesAnExplicitUrlsArgumentUntouched_…` and `…AspNetCoreUrlsVariableUntouched_…` | `Expected: "http://127.0.0.1:0"` / `Actual: "http://+:3001"`; `Expected: "http://127.0.0.1:14999"` / `Actual: "http://+:3001"` |
| A7 | `http://+:` → `http://127.0.0.1:` | substitution | `CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_…` | `Expected: "http://+:13001"` / `Actual: "http://127.0.0.1:13001"` |
| A7b | same as A7 | substitution | `StartedHost_ListensOnGatewayOptionsPort_…` | `Assert.Matches() Failure: Pattern not found in value` / `Value: "http://127.0.0.1:35597"` |
| A8 | `http_ports` also treated as explicit | corruption | `CreateBuilder_StillBindsGatewayOptionsPort_WhenOnlyAspNetCoreHttpPortsIsSet` | `Expected: "http://+:13001"` / `Actual: null` |
| A9 | `null` port → `?? 3001` | corruption | `CreateBuilder_SetsNoUrls_WhenNoPortIsConfigured` | `Assert.Null() Failure: Value is not null` / `Actual: "http://+:3001"` |
| A10 | `{port}` → hard-coded `{3001}` | substitution | `CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_…` | `Expected: "http://+:13001"` / `Actual: "http://+:3001"` |

Note: A5 was first run against the earlier free-port version of the test (`Not found: ":35505"`, `String: "http://localhost:5000"`). The table shows the re-run against the final test.

### Defeat list (CLAUDE.md's ten)

- **Ran:**
  - 1 (deletion): A1, A5, A6
  - 2 (corruption): A4, A8, A9
  - 3 (sibling substitution): A2, A3, A7, A10
  - 7 (drop an optional element): A9 covers the absent port. A6 covers the absent-`urls` branch the other way round.
- **Do not apply**, because these guards execute the code rather than scanning text:
  - 4 (comment or string shadow)
  - 5 (dead region)
  - 6 (raw string)
  - 10 (build-output copy)
- **8 (literal compared with a literal):** each test calls the real `Configure` or `CreateBuilder` and reads back what they produced.
- **9 (premise half):** the premise is "the host listens". A5 and A7b attack it on a real socket, not only through the config key.

## Ported-idiom ledger

| # | #7 relied on | In #8 that property comes from | Guard |
|---|---|---|---|
| L1 | `apps/gateway/src/main.ts:30`: `Number(process.env.GATEWAY_PORT ?? 3001)`, then `app.listen(port)` with no host, so all interfaces | `GatewayProgramConfiguration.Configure` → `GatewayOptions.Port` → `GatewayHost.ApplyListenPort` → `UseUrls("http://+:{port}")` | the default and substitution tests, the `CreateBuilder_SetsUrls…` test and the `StartedHost…` test (A1–A5, A7, A7b, A10) |
| L2 | #7's only test coverage of the read was indirect: `apps/gateway/src/black-box-api.integration.spec.ts:327,396,409` spawns the real Gateway with `GATEWAY_PORT` set to a `getFreePort()` value and connects to `127.0.0.1:<that port>`, so deleting the read would break that suite. No #7 test sets siblings to distinct values, so a substitution to another `*_PORT` was unguarded in #7. | A unit-level default test and sibling-substitution test, plus a real-socket test. This is a **strengthening** over #7 for the substitution family. | A2 and A3 |
| L3 | Node has no host-level URL override that competes with `app.listen(port)` | .NET does (`--urls`, `ASPNETCORE_URLS`, `ASPNETCORE_HTTP_PORTS`), so the precedence had to be decided here: explicit `urls` wins, `HTTP_PORTS` does not | A6 and A8 |

#7 test enumeration (content-based, not by filename): `find . \( -name '*.ts' -o -name '*.mjs' -o -name '*.js' -o -name '*.sh' -o -name '*.yml' \) -not -path '*/node_modules/*' -not -path '*/dist/*' -not -path './.git/*' -print0 | xargs -0 grep -n "GATEWAY_PORT"` (run in #7's checkout) returned 7 hits:
- `docker-compose.infra.yml:442`: n8n URL. Not a test; #8 has the same line.
- `docker-compose.apps.yml:306,318,347`: apps compose. Not a test; #8 has no apps compose file.
- `apps/web/nuxt.config.ts:30`: web base URL. Not a test; #8's `apps/web/src/server/config.ts:12` is the equivalent.
- `apps/gateway/src/main.ts:30`: the source read, ported as L1.
- `apps/gateway/src/black-box-api.integration.spec.ts:396`: the only assertion that touches it, and only indirectly. Ported in a strengthened form (L2) as unit and real-socket tests, rather than as a spawned-process test.

## Class enumeration (bullet 6), re-run after the fix

For every variable `.env.example` declares, I counted `src/**/*.cs` files (excluding `bin/` and `obj/` by path) that contain the quoted key: `for v in $(grep -oE '^[A-Z_][A-Z0-9_]*=' .env.example …); do find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l "\"$v\"" | wc -l …`. **`GATEWAY_PORT` no longer appears among the zero-count variables.** 59 variables remain at zero. The full output, with the first non-C# consumer found for each, is in `scratchpad/id96_enum.txt`. By group:
- **Compose or infra only:** `GRAFANA_*`, `JAEGER_UI_HOST_PORT`, `KAFKA_CLUSTER_ID`, `KAFKA_CONTROLLER_PORT`, `KAFKA_EXPORTER_HOST_PORT`, `KAFKA_INTERNAL_HOST`/`_PORT`, `MAILPIT_*`, `MSSQL_PID`, `N8N_*` (auth, host port, encryption key), `NATS_MONITOR_HOST_PORT`, `OTEL_COLLECTOR_*_HOST_PORT`, `PROMETHEUS_HOST_PORT`, `REDPANDA_CONSOLE_HOST_PORT`, `SONARQUBE_HOST_PORT`, `TZ`.
- **Infra entrypoints:** `KAFKA_TOPIC_*` (`infra/kafka/create-topics.sh`), `MSSQL_READY_TIMEOUT_SECONDS` and `MSSQL_SA_PASSWORD` (`infra/mssql/entrypoint.sh`), `N8N_WORKFLOWS_ENABLED` (`infra/n8n/import-workflows-on-startup.sh`).
- **n8n workflows, via compose:** `BURST_*`, `ORDER_GENERATOR_*`, `OTC_REQUEST_TIMEOUT_MS`, `PAYMENT_*`, `STOCK_REPLENISH_*`.
- **Web app or `dev-stack.sh`:** `GATEWAY_BASE_URL`, `WEB_PORT`, `WEB_SESSION_PASSWORD`, `WEB_COOKIE_SECURE`.
- **Docker Compose itself:** `COMPOSE_PROJECT_NAME`.

None of these is a .NET service setting.

## Live check

- `scripts/dev-stack.sh start` (default ports; 3000 was free) exited 0. It printed `[OK] Gateway answers http://localhost:3001/health/ready` and then `[OK] web answers http://localhost:3000/login`.
- `curl http://localhost:3001/health/ready` returned `{"status":"up","checks":{"rpcTransport":{"status":"up"},"readModel":{"status":"up"}}}` with HTTP 200.
- `ss -ltnp` showed `*:3001` held by `OrderToCash.Gat` (pid 78003). `logs/dev-stack/Gateway.log` shows `Now listening on: http://[::]:3001`.
- The Gateway's `dotnet run` process environment (`/proc/77787/environ`) contains `GATEWAY_PORT=3001` and **no** `ASPNETCORE_*` variable.
- `scripts/dev-stack.sh stop` exited 0: all seven processes stopped, and it printed `[OK] nothing left running`. Afterwards, `pgrep -af "OrderToCash\.|dotnet run --no-build|next (dev|start)"` was empty and `ss -ltn` showed nothing on 3000–3006. The infrastructure containers are left running, which is what the script does by design.

## Not done, and why

- **`.env.example:234-236` is now stale.** It says *"the Gateway itself does not read GATEWAY_PORT yet (scripts/dev-stack.sh binds it through ASPNETCORE_URLS); that is a known defect being fixed"*. The brief did not include `.env.example` in my scope, so I left it. The leader should delete those lines.
- **`progress/impl_web_app.md:272,432` describe the workaround as current.** That record belongs to another feature, so I did not edit it.
- **`./quality.sh` was not run in full.** The brief asked for the two Gateway suites. I also ran `Architecture.Tests` (50/50, which caught the TcpListener issue) and a scoped `dotnet format --verify-no-changes` (exit 0).

## Surprises

- **Backlog id 85's architecture guard caught my first real-socket test.** It used the retired free-port helper shape. Moving to `Port=0` made the test stronger: under deletion it now reports Kestrel's own `localhost:5000` default.
- **Removing the `ASPNETCORE_URLS` prefix in `dev-stack.sh` first introduced a fall-through.** The `web` branch would also have run `dotnet run --project src/web`. I caught it on reading the edit and fixed it with an explicit `return`; the live start/stop exercised the corrected path.
- **`pgrep -fl "dotnet (build|test|format)"` matched my own wrapper shell once** (pid 4164364, `bash`), which is the self-match CLAUDE.md warns about. I waited on real PIDs (`kill -0`) throughout.
