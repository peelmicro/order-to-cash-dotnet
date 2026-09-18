# Order-To-Cash (.NET) — shared multi-stage build for every backend service
# (orders, fulfillment, billing, notifications, projector, gateway). Phase 23
# (docker-compose.apps.yml). One Dockerfile, parameterised by the `SERVICE`
# build ARG (the src/<Service> directory name, which is also the .csproj file
# name and the AssemblyName suffix — e.g. src/Orders/Orders.csproj builds
# OrderToCash.Orders.dll) — the six services build identically (all nine
# src/*.csproj files were read; Orders/Fulfillment/Billing/Notifications/
# Projector/Gateway share the exact same shape: Sdk/Sdk.Web, a
# ProjectReference triangle onto SharedKernel+Contracts(+Cqrs), and
# FrameworkReference Microsoft.AspNetCore.App for the health probe — see
# progress/impl_full_docker_compose.md's enumeration), so six near-duplicate
# Dockerfiles would just be six places to forget to fix the same bug in —
# the same "cheaper to maintain" choice #7's infra/docker/service/Dockerfile
# already made for its six NestJS services (ARG SERVICE there too).
#
# ── Why the SDK is installed by hand, not pulled from mcr.microsoft.com/dotnet/sdk ──
# `global.json` pins the SDK to EXACTLY 10.0.111 with rollForward: latestPatch
# (which only rolls forward WITHIN the same feature band — 10.0.1xx — never
# to a later band). Checked, not assumed: as of this build, MCR's own
# `dotnet/sdk` repository has no 10.0.111 tag, and its highest published
# 10.0.1xx-band tag is 10.0.103 (`curl -s https://mcr.microsoft.com/v2/dotnet/sdk/tags/list`,
# every 10.0.1xx tag enumerated) — lower than the pinned patch, so `dotnet`
# inside that image refuses to run at all ("Requested SDK version: 10.0.111
# ... Installed SDKs: 10.0.103", reproduced live against both
# `dotnet/sdk:10.0` (resolves to 10.0.401, band 400 — rollForward: latestPatch
# rejects a different band) and `dotnet/sdk:10.0.103` before writing this
# Dockerfile). Editing global.json to relax rollForward is out of scope for
# this feature (CLAUDE.md: touch only the files this brief names) and would
# be the wrong fix regardless — the pin is deliberate. The official
# dotnet-install.sh script fetches the exact SDK build directly from
# builds.dotnet.microsoft.com (not from the container-image catalog, which
# lags the CLI installer's own release cadence), and 10.0.111 IS available
# there (verified: `dotnet-install.sh --version 10.0.111` succeeds). This
# build stage therefore starts from plain `ubuntu:24.04` (the same base
# distribution the official SDK/ASP.NET Core images themselves use for
# `net10.0` — `noble`) and installs the pinned SDK onto it exactly the way
# Microsoft's own image build does internally.

ARG DOTNET_SDK_VERSION=10.0.111
ARG DOTNET_EF_VERSION=10.0.11
ARG ASPNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# ── sdk-base — Ubuntu (noble) + the EXACT pinned SDK, nothing else ─────────
FROM ubuntu:24.04 AS sdk-base
ARG DOTNET_SDK_VERSION
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl libicu74 \
    && rm -rf /var/lib/apt/lists/* \
    && curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --version "${DOTNET_SDK_VERSION}" --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && groupadd -g 1654 app \
    && useradd -u 1654 -g app -m -s /usr/sbin/nologin app \
    && mkdir -p /nuget/packages \
    && chmod -R a+rwX /nuget/packages
ENV PATH="/usr/share/dotnet:/opt/dotnet-tools:${PATH}" \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip \
    NUGET_PACKAGES=/nuget/packages

# ── restore — the full src/ tree (3.8 MB of .cs/.csproj, .dockerignore
#    strips bin/obj/node_modules/.git so the build context stays small) plus
#    the three repo-root MSBuild files every project imports, then restore
#    ONLY the target project — its ProjectReference graph
#    (SharedKernel/Contracts[/Cqrs]) is restored transitively with it.
#    specs/shared/openapi.yaml is copied unconditionally (small, 68 KB-ish)
#    because Gateway.csproj EmbeddedResource-includes it at a path relative
#    to the repo root (../../specs/shared/openapi.yaml) — the only one of
#    the six that reads from specs/ at COMPILE time; copying it for every
#    service is simpler than a service-specific branch in a Dockerfile this
#    file is shared across (same reasoning #7's own Dockerfile gives for its
#    equivalent copy). ─────────────────────────────────────────────────────
FROM sdk-base AS restore
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src ./src
COPY specs/shared/openapi.yaml ./specs/shared/openapi.yaml
ARG SERVICE
RUN dotnet restore "src/${SERVICE}/${SERVICE}.csproj"

# ── build — framework-dependent publish (the runtime stage below already
#    carries the matching ASP.NET Core shared framework; a self-contained
#    publish would just duplicate it in every image). ──────────────────────
FROM restore AS build
ARG SERVICE
RUN dotnet publish "src/${SERVICE}/${SERVICE}.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --no-self-contained

# ── runtime — the official ASP.NET Core runtime image (every one of the six
#    services carries FrameworkReference Microsoft.AspNetCore.App: five for
#    the health probe's own tiny WebApplication, Gateway for its whole
#    surface — so `aspnet`, not the leaner `runtime`, image is correct for
#    all six, not just Gateway). curl is added for the HEALTHCHECK below —
#    this image ships neither curl nor wget (checked:
#    `docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 sh -c "which curl wget"`
#    prints nothing), and there is no .NET-native equivalent of #7's Node
#    `require('http')` one-liner (no script host without the full SDK, and
#    it would need to run a whole extra assembly). ─────────────────────────
FROM ${ASPNET_RUNTIME_IMAGE} AS runtime
ARG SERVICE
ARG PORT_ENV_VAR
ARG DEFAULT_PORT
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV APP_ASSEMBLY=OrderToCash.${SERVICE}.dll \
    PORT_ENV_VAR=${PORT_ENV_VAR} \
    DEFAULT_PORT=${DEFAULT_PORT}
WORKDIR /app
# --chown after every COPY: the base image's built-in unprivileged `app` user
# (uid 1654, gid 1654 — confirmed present: `id app` inside the base image)
# is used rather than adding a second one, same reasoning #7 gives for
# reusing node:*-bookworm-slim's own `node` user.
COPY --chown=app:app --from=build /app/publish ./
USER app

# curl -f (fail on >=400, which /health/ready returns as 503 when a
# dependency is down) against the LOOPBACK address the health probe binds to
# (HealthProbeService.cs: `http://127.0.0.1:{port}`, all six services —
# confirmed by reading each one) — reachable from inside this same
# container's network namespace regardless of that loopback-only bind.
# PORT_ENV_VAR/DEFAULT_PORT let one shared HEALTHCHECK work across all six
# services despite each reading a differently-named port env var
# (ORDERS_HEALTH_PORT, ..., GATEWAY_PORT) — same trick as #7's own
# PORT_ENV_VAR/DEFAULT_PORT build args, translated from a Node one-liner to
# a shell one using POSIX indirect parameter expansion (`eval echo`).
HEALTHCHECK --interval=5s --timeout=5s --retries=20 --start-period=20s \
  CMD sh -c 'p=$(eval echo \$$PORT_ENV_VAR); p=${p:-$DEFAULT_PORT}; curl -fsS "http://127.0.0.1:$p/health/ready" > /dev/null || exit 1'

CMD dotnet ${APP_ASSEMBLY}

# ── migrate — one-shot EF Core migration CLI (orders/fulfillment/billing/
#    notifications only; gateway owns no MS-SQL database, projector
#    bootstraps its own Mongo indexes in-process on every boot — neither
#    ever gets `target: migrate` in docker-compose.apps.yml). Needs the SDK
#    (dotnet-ef performs a design-time build of the startup project), so it
#    reuses the `restore` stage's full source tree rather than the
#    published, source-free `runtime` stage above.
#
#    #8's migration shape gap this closes: unlike #7 (which runs a
#    migrate-then-run container PAIR per service for all four of its
#    MySQL-backed services), #8's `src/Seed` (SeedRunner.cs) only applies
#    migrations for THREE of #8's four MS-SQL-backed services — its own log
#    line says so verbatim, "applying migrations (orders, fulfillment,
#    billing)..." — leaving Notifications' EF Core migrations
#    (src/Notifications/Infrastructure/Persistence/Migrations/) with no
#    automatic path in a cold compose stack, only the manual
#    `pnpm db:migrate:notifications`. Rather than extend SeedRunner.cs (a
#    src/Seed code change, and Seed.csproj references only Orders/
#    Fulfillment/Billing — adding Notifications there would mean a new
#    ProjectReference too), this Dockerfile gives ALL FOUR MS-SQL-backed
#    services their own migrate-then-run container pair, matching #7's
#    shape exactly and closing the Notifications gap with ZERO src/ changes:
#    every one of the four already ships its own
#    IDesignTimeDbContextFactory<T> (OrdersDbContextFactory,
#    FulfillmentDbContextFactory, BillingDbContextFactory,
#    NotificationsDbContextFactory — all four read MSSQL_HOST/
#    MSSQL_HOST_PORT/MSSQL_DB_<SERVICE>/MSSQL_APP_USER/MSSQL_APP_PASSWORD
#    from the environment, confirmed by reading all four), which is exactly
#    what `dotnet ef database update --project src/<Service>` needs and is
#    also exactly the same command `pnpm db:migrate:<service>` already runs
#    by hand (package.json) — this stage automates that command, it does
#    not invent a new mechanism. Seed's own three MigrateAsync() calls are
#    left untouched and are harmless here: EF Core migrations are
#    idempotent, so Seed re-applying orders/fulfillment/billing's already-
#    applied migrations a second time is a no-op, not a conflict.
FROM restore AS migrate
ARG SERVICE
ARG DOTNET_EF_VERSION
ENV SERVICE=${SERVICE}
# --tool-path (not --global): a global install writes into $HOME/.dotnet/tools
# for whichever user runs it (root, at this point in the stage) — the `USER
# app` switch below would then have PATH pointing at a directory it cannot
# read as a different, non-root HOME. A fixed --tool-path sidesteps HOME
# entirely; `sdk-base` already put it on PATH for every user.
RUN dotnet tool install dotnet-ef --version "${DOTNET_EF_VERSION}" --tool-path /opt/dotnet-tools \
    && chmod -R a+rX /opt/dotnet-tools
RUN chown -R app:app /src
USER app
CMD dotnet ef database update --project "src/${SERVICE}"
