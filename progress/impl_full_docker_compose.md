# impl_full_docker_compose.md — backlog id 36, phase 23

Brief: `brief_36.md` (LIGHT-classified but substantial construction; sole
implementer, no separate reviewer round — the leader checks this report's
evidence directly). Read `CLAUDE.md` on disk before starting and again
before writing this report (`grep -n` against the live file, not a cached
copy).

## Contract (verbatim, `feature_list.json` id 36)

- cold start reaches a demoable state within ~2 minutes
- infra-only mode still works with apps run from the CLI

## 1. #7's shape, enumerated as a search result (read in full, not from memory)

`../order-to-cash-nestjs/docker-compose.apps.yml` (399 lines) — one line per
service:

| Service | Healthcheck | `depends_on` |
|---|---|---|
| `orders-migrate` | none (one-shot) | `mysql: service_healthy` |
| `orders` | `GET /health/ready` (shared Node HEALTHCHECK) | `orders-migrate: service_completed_successfully`, `kafka-init: service_completed_successfully`, `nats: service_healthy` |
| `fulfillment-migrate` | none | `mysql: service_healthy` |
| `fulfillment` | `GET /health/ready` | `fulfillment-migrate: completed`, `kafka-init: completed`, `nats: healthy` |
| `billing-migrate` | none | `mysql: service_healthy` |
| `billing` | `GET /health/ready` | `billing-migrate: completed`, `kafka-init: completed`, `nats: healthy` |
| `notifications-migrate` | none | `mysql: service_healthy` |
| `notifications` | `GET /health/ready` | `notifications-migrate: completed`, `kafka-init: completed`, `mailpit: healthy` |
| `projector` | `GET /health/ready` | `mongodb: healthy`, `kafka-init: completed`, `nats: healthy` (no migrate pair — bootstraps its own Mongo indexes) |
| `gateway` | `GET /health/ready` | `mongodb: healthy`, `nats: healthy` |
| `web` | `GET /` best-effort (Node HEALTHCHECK, no `/health` route) | `gateway: healthy` |
| `seed` | none, `profiles: [seed]`, never started by `up -d` | `orders-migrate`/`fulfillment-migrate`/`billing-migrate: completed`, `mongodb: healthy` |

Plus the top-level `networks: otc-net: external: true` declaration. Dockerfiles
read in full: `infra/docker/service/Dockerfile` (192 lines, shared, `ARG
SERVICE`, `deps`/`build`/`prod-deps`/`runtime`/`migrate` stages, Node/tsc),
`infra/docker/web/Dockerfile` (94 lines, separate shape — Nuxt's own
self-contained `.output/`).

## 2. What #8 needed instead, and why (design choices)

**One shared Dockerfile for all six .NET services**
(`infra/docker/service/Dockerfile`), `ARG SERVICE` — chosen over six
near-identical files because, having read all nine `src/*.csproj` files, five
of the six (Orders/Fulfillment/Billing/Notifications/Projector) are
structurally identical (`Sdk`, a `ProjectReference` triangle onto
SharedKernel+Contracts[+Cqrs], `FrameworkReference
Microsoft.AspNetCore.App` for the health probe) and Gateway differs only in
`Sdk.Web` + an `EmbeddedResource`, which the Dockerfile handles by copying
`specs/shared/openapi.yaml` unconditionally rather than branching — the same
"cheaper to maintain" reasoning #7's own Dockerfile gives for its `ARG
SERVICE`.

**A separate `infra/docker/web/Dockerfile`** for `apps/web` — different build
context (`apps/web` itself, not the repo root) because `apps/web` is its own
standalone pnpm project (`apps/web/pnpm-workspace.yaml`'s own comment: "the
backend has no Node dependency"), unlike #7's Nuxt app which lives inside a
shared pnpm workspace with every NestJS service.

**Finding — the pinned SDK has no matching MCR image, so the build stage
installs it by hand.** `global.json` pins `10.0.111` with `rollForward:
latestPatch` (same feature band only). Checked, not assumed:
`curl -s https://mcr.microsoft.com/v2/dotnet/sdk/tags/list` enumerates every
`10.0.1xx` tag published — the highest is `10.0.103`, below the pin — and
running `dotnet --version` inside both `dotnet/sdk:10.0` (resolves to
`10.0.401`, a different band) and `dotnet/sdk:10.0.103` against this repo's
`global.json` reproduces "Requested SDK version: 10.0.111 ... Installed
SDKs: 10.0.103/10.0.401" — the SDK muxer refuses to run at all. The official
`dotnet-install.sh` script fetches the exact SDK build directly from
`builds.dotnet.microsoft.com` (a different release channel than the
container-image catalog): `dotnet-install.sh --version 10.0.111` succeeds.
The `sdk-base` stage therefore starts from plain `ubuntu:24.04` (the same
base distribution the official images use for `net10.0`, `noble`) and
installs the pinned SDK by hand — proved by a real build (§4 below), not
merely by resolving the Dockerfile. Editing `global.json` was out of scope
(not in the brief's touch list) and would be the wrong fix regardless.

**Migration shape — all four MS-SQL-backed services get their own
migrate-then-run container pair; `src/Seed` is untouched.** `src/Seed`
(`SeedRunner.cs`) migrates only Orders/Fulfillment/Billing (its own log line
says so; `Seed.csproj` references only those three `ProjectReference`s).
Rather than extend `SeedRunner.cs` (a `src/Seed` change, plus a new
`ProjectReference` onto `Notifications.csproj` to drag its `DbContext` in),
every one of the four services — Orders, Fulfillment, Billing **and
Notifications** — gets its own `<service>-migrate` container in
`docker-compose.apps.yml`, running `dotnet ef database update --project
src/<Service>` against a `migrate` Dockerfile target that reuses the
`restore` stage's full source tree (needed: `dotnet ef` does a design-time
build). This closes the exact gap the brief named — Notifications' own EF
Core migrations previously had no automatic path in a cold stack, only the
manual `pnpm db:migrate:notifications` — with **zero `src/` changes**: all
four services already ship their own
`IDesignTimeDbContextFactory<T>` reading `MSSQL_HOST`/`MSSQL_DB_<SERVICE>`/
`MSSQL_APP_USER`/`MSSQL_APP_PASSWORD` from the environment (confirmed by
reading all four), which is exactly what the automated `dotnet ef` command
needs, and is the same command `pnpm db:migrate:notifications` already runs
by hand — this only automates it. **`SeedRunner.cs` was NOT touched** (the
brief's other option). Seed's own three `MigrateAsync()` calls for
Orders/Fulfillment/Billing are harmless alongside the new migrate
containers — EF Core migrations are idempotent, confirmed live: `pnpm run
seed` ran clean against a stack whose migrate containers had already applied
those same migrations (§5 below).

**A `dotnet-ef` version pin matching `Directory.Packages.props`**
(`10.0.11`, same band as `Microsoft.EntityFrameworkCore.SqlServer`/`.Design`)
— `CLAUDE.md`'s "`dotnet-ef` must match the EF Core version band" rule,
installed with `--tool-path` (not `--global`) so it is reachable regardless
of which user (`root` during restore, `app` at `CMD` time) invokes it — a
`--global` install would have written into `root`'s `$HOME/.dotnet/tools`,
unreachable once the stage drops to the non-root `app` user.

**A world-writable `NUGET_PACKAGES=/nuget/packages`** — the `migrate` stage
restores as `root` but runs its `CMD` as `app`; a shared, explicitly-located
package cache sidesteps a `$HOME`-dependent default that would otherwise
leave `app` unable to read `root`'s restore output.

**No `output: 'standalone'` in `apps/web/next.config.ts`** (checked — the
file has no `output` key, and it is `apps/web/` source, out of this
feature's touch list) — the web runtime stage therefore carries a
`--prod`-only `node_modules` alongside `.next/`, running the exact same
`node ... next start --port ${WEB_PORT:-3010}` command
`apps/web/package.json`'s own `start` script already runs, just without the
`pnpm run` wrapper.

**curl added to the six .NET runtime images** for `HEALTHCHECK` — checked,
not assumed: `mcr.microsoft.com/dotnet/aspnet:10.0` ships neither curl nor
wget. Node's own `http` client (what #7's Dockerfile and this feature's own
`web` Dockerfile use) has no .NET equivalent without pulling in the full SDK.

**Finding, fixed — `external: true` in `docker-compose.apps.yml`'s own
`networks:` block breaks a genuinely cold combined `up`.** An early draft
mirrored #7's `networks: otc-net: external: true`. Reproduced live on a
stack with the network never created by any prior `up`: `docker compose -f
docker-compose.infra.yml -f docker-compose.apps.yml up -d` failed outright —
"network otcnet-net declared as external, but could not be found" — because
once ANY file in a multi-`-f` merge marks a network `external: true`, the
WHOLE MERGED network is external, even though the same command also passes
`docker-compose.infra.yml`, which would otherwise create it. Fixed by
dropping the top-level `networks:` block from `docker-compose.apps.yml`
entirely — `docker-compose.infra.yml` already declares `otcnet-net` fully
(name + `driver: bridge`), and every service in `docker-compose.apps.yml`
already joins it by name. The "run `docker-compose.apps.yml` alone should
fail loudly" property this was meant to give does not depend on the flag at
all: every service in the file already `depends_on:` an infra-only service
(`mssql`/`kafka-init`/`nats`/`mongodb`/`mailpit`), so Compose already refuses
the project outright — reproduced live: `docker compose -f
docker-compose.apps.yml config` → `service "fulfillment-migrate" depends on
undefined service "mssql": invalid compose project`, before network
resolution is ever reached. This may be a latent issue in #7's own
`docker-compose.apps.yml` too (its docs assume the combined invocation "just
works," but its own test history may always have run `dc:up:infra` first in
the same session, which pre-creates the network and never exercises this
merge order) — **not fixed there**: out of this feature's scope (#7 is
complete per `CLAUDE.md`; mirrored only on a spec amendment or explicit
maintainer request), flagged here for the record rather than silently
carried into #8's own file.

## 3. Files touched

- `infra/docker/service/Dockerfile` (new) — shared multi-stage build,
  `sdk-base`/`restore`/`build`/`runtime`/`migrate` stages, `ARG SERVICE`.
- `infra/docker/web/Dockerfile` (new) — `deps`/`build`/`prod-deps`/`runtime`.
- `docker-compose.apps.yml` (new) — 6 services × (runtime [+ migrate for the
  4 MS-SQL-backed ones]) + `web` = 16 service entries, joining
  `docker-compose.infra.yml`'s `otcnet-net`.
- `.dockerignore` (new, repo root) — excludes `bin/`/`obj/`/`node_modules/`/
  `.git`/etc. from the six .NET services' + would-be shared build context.
- `apps/web/.dockerignore` (new) — same purpose for `apps/web`'s own,
  separate build context.
- `feature_list.json` — id 36's `status` line only (`pending` → `in_review`;
  confirmed with `git diff`, one line).
- `progress/impl_full_docker_compose.md` (this file).

No `src/` file, `specs/shared/`, `CLAUDE.md`, or `apps/web/` **source** file
was touched (the two `.dockerignore` files are build tooling, not source).
`SeedRunner.cs` was read but not modified — see §2's migration-shape
decision.

## 4. Real builds (proof, not just Dockerfile inspection)

Every stage of the shared service Dockerfile was built and RUN standalone
before wiring the compose file:

- `docker build --target build --build-arg SERVICE=Orders` → succeeded,
  `dotnet restore`/`dotnet publish` both green, `OrderToCash.Orders.dll`
  produced (51.7s, cold SDK install included).
- `docker build --target runtime ... --build-arg PORT_ENV_VAR=ORDERS_HEALTH_PORT
  --build-arg DEFAULT_PORT=3002` → succeeded (7.9s on top of the cached
  `build` layer); `docker run` against it with only `MSSQL_APP_PASSWORD` set
  (no compose network) produced real Kestrel/Kafka/NATS connection-refused
  errors — i.e. the actual compiled `OrderToCash.Orders` process ran, tried
  to reach `localhost:9092`/`nats://localhost:4222` and logged the genuine
  .NET exceptions, proving the image runs the real app, not a stub.
- `docker build -f infra/docker/web/Dockerfile apps/web` → succeeded
  (`next build` ran inside the container, all 19 routes compiled); `docker
  run -p 13010:3010 ...otcnet-web:local` then answered `GET /` with `HTTP
  307` (redirect to `/login`, unauthenticated — proof the Next.js server is
  alive and routing) and reported `(healthy)` in `docker ps`.

All 13 images (6 services × 2 [runtime+migrate for 4, runtime-only for
Gateway/Projector] + `web` + the 2 already-existing infra images) then built
together via `docker compose -f docker-compose.infra.yml -f
docker-compose.apps.yml build`.

## 5. Acceptance bullet 1 — "cold start reaches a demoable state within ~2 minutes"

Armed as a countable claim: elapsed wall-clock seconds from `docker compose
... up -d` invocation to the LAST container among the full 18 (11 infra + 6
services + web) reporting its first successful `Health.Log` entry, read via
`docker inspect`, both timestamps captured independently of shell polling
delay.

**Run 1 — genuinely cold volumes, warm image cache** (images already built
from §4/§2; `docker compose ... down -v` first, confirmed `docker network
ls`/`docker compose ps -a` empty beforehand): `up -d` invoked at
`2026-09-18T11:53:47.267Z`. Last container to report healthy: `orders` at
`+91.8s`. **91.8s, comfortably under 120s.**

**Cold-image number, once, as requested** (`docker builder prune -af`
reclaimed 52.4 GB; all 11 `otcnet-*:local` images removed — base images
`ubuntu:24.04`/`node:24.19.0-bookworm-slim`/`mcr.microsoft.com/dotnet/
aspnet:10.0` stayed locally cached from earlier pulls this session, so this
is "no build-layer cache, base images already pulled," stated explicitly per
the arming instruction): `docker compose ... build` with **zero** cache took
**1m42s (102.1s)** for all 13 images. Bringing the stack up immediately
afterward (images now built, volumes fresh) to full health took **65.1s** —
i.e. a genuinely first-ever run (build + start, nothing cached) is
**build 102.1s + start 65.1s ≈ 167s (~2.8 min)**, over the 2-minute figure —
disclosed here rather than folded into the headline number. The bullet's
~2-minute claim reads naturally as the repeated/steady-state "start the
stack" case (images already built — the normal developer workflow after the
first `up`), which is the 91.8s/65.1s numbers above, both well under 120s.
The one-time image-build cost is a separate, disclosed number, not silently
absorbed into the "cold start" claim.

## 6. Acceptance bullet 2 — "infra-only mode still works with apps run from the CLI"

With the full stack up and healthy, `docker compose ... stop
orders fulfillment billing notifications projector gateway web` + `rm -f`
the same six+one, leaving only the 11 infra containers (confirmed via
`docker compose -f docker-compose.infra.yml ps` — all 11 healthy, nothing
else). Then `pnpm run dev:gateway` (`scripts/dev-stack.sh env dotnet run
--project src/Gateway`, unmodified — this feature does not touch that
script) started the Gateway as a bare host process. `curl
http://localhost:3001/health/ready` returned `200`,
`{"status":"up","checks":{"rpcTransport":{"status":"up"},"readModel":{"status":"up"}}}`
— i.e. the host process reached the containerized NATS and MongoDB over
their host-published ports exactly as it did before this feature existed.
Process killed afterward, confirmed gone (`pgrep`).

## 7. "Demoable state means something concrete" — a real order reaches `completed`

With the full stack back up (`up -d orders fulfillment billing notifications
projector gateway web`, all reporting healthy again), ran `pnpm run seed`
(host CLI, `scripts/dev-stack.sh env dotnet run --project src/Seed`) against
the composed stack's host-published MS-SQL/Mongo ports — succeeded,
`[seed] done.`, and separately confirmed the `notifications-migrate`
container itself had already applied Notifications' migration
(`docker logs otcnet-notifications-migrate` → `Applying migration
'20260901110547_InitialCreate'. Done.`, `ExitCode=0`) — the exact gap §2
closes, proven live, not just wired.

Then, through the Gateway's real REST API (`http://localhost:3001`, the
published container port — no bypass):

1. `POST /auth/login` (operator credentials from `.env`) → `200`, bearer
   token issued by the containerized Gateway.
2. `POST /orders` (`retailerCode: AldiDe`, `companyCode: BAUWERK`, `currency:
   EUR`, 5× `PRD-0001`) → `201`, `orderReference: ORD-000007`,
   `status: placed`.
3. Polled `GET /orders/{id}` — the saga advanced automatically, through the
   containerized Orders/Fulfillment/Billing/Projector, to `status: invoiced`
   within the first poll (stock reserved, credit approved, despatched,
   invoiced — all inside the composed Docker network, no host process
   involved).
4. `GET /invoices?status=issued` → found `INV-000006` for `ORD-000007`,
   `totalAmount: 124995` (minor units).
5. `POST /invoices/{id}/payments` (`amount: 124995 EUR`, `source: operator`)
   → `201`, `outcome: accepted`, `invoiceStatus: paid`.
6. `GET /orders/{id}` → **`status: completed`**, immediately — full event
   history readable from the Mongo-backed read model (Projector), naming
   every step: `order.placed.v1` → `stock.reserved.v1` →
   `credit.approved.v1` → `order.confirmed.v1` → `order.despatched.v1` →
   `invoice.issued.v1` → `payment.received.v1` → `credit.released.v1` →
   `order.completed.v1`.

Restarted the six app containers afterward (stop/rm then `up -d` again, a
separate run from §6) and re-queried `GET /orders/98516a7d-...` through a
fresh login — still `status: completed`, `orderReference: ORD-000007` — the
order survived a full container-set restart against the named MS-SQL/Mongo
volumes, corroborating (not required by the brief, but free evidence) that
nothing here is held only in a process's memory.

## 8. Teardown

`docker compose -f docker-compose.infra.yml -f docker-compose.apps.yml down
-v` at the end — all containers, the `otcnet-net` network and all five named
volumes removed. Confirmed clean: `docker ps` → empty; `pgrep -af "dotnet
run|OrderToCash\.|next (dev|start)"` → nothing; `docker network ls | grep
otcnet` → nothing. `docker builder prune -af` was already run mid-task (§5);
no further build cache cleanup was needed. The scratch `DOCKER_CONFIG`
directory used for every build/run in this feature (never the maintainer's
own `~/.docker/config.json`, which was never read or written) was deleted
afterward. One pre-existing, unrelated stopped container
(`otcnet-sonarqube`, exited 5+ hours before this session started) was left
untouched — not mine to manage.

## 9. What was not done / found and not fixed

- No Seed Dockerfile was built — the brief's Dockerfile list names only the
  six services + web; `pnpm run seed` (CLI) remains the only way to run it,
  consistent with bullet 2's "apps run from the CLI" framing. Not a gap
  against the brief's own scope.
- The `external: true` merge footgun (§2) may also affect #7's own
  `docker-compose.apps.yml` on a genuinely first-ever combined `up` — not
  investigated or fixed there (out of scope; #7 is complete).
- The image-build cost (102s cold, §5) is real and disclosed, not hidden —
  it does not, on its own, meet the "~2 minutes" figure; the steady-state
  start numbers (91.8s, 65.1s) do.

## Result

Both acceptance bullets are demonstrated with live, real evidence: a genuine
cold-volumes `up -d` reaching all-18-healthy in 91.8s (well under 2 minutes,
warm image cache, with the one-time cold-image build cost disclosed
separately at 102s); infra-only mode proven with a real host `dotnet run`
process talking to the composed infra containers with zero regression; and a
real order (`ORD-000007`) placed through the composed Gateway reaching
`completed` end to end, surviving a container restart.
