# Order-To-Cash (.NET) — apps/web (Next.js 16) build. Phase 23
# (docker-compose.apps.yml).
#
# Different build context from infra/docker/service/Dockerfile on purpose:
# apps/web is its OWN standalone pnpm project (`apps/web/pnpm-workspace.yaml`
# carries no `packages:` list — its own comment says so: "apps/web is a
# standalone pnpm project (the backend has no Node dependency)"), unlike
# #7's Nuxt app which lives inside a pnpm workspace shared with every NestJS
# service and needs the whole monorepo lockfile to install (see #7's own
# `infra/docker/web/Dockerfile` header comment). `context: apps/web`, not
# the repo root, is therefore both correct and simpler here.
#
# apps/web/next.config.ts declares no `output: 'standalone'` (checked — the
# file has no `output` key at all, and it is apps/web/ SOURCE, out of this
# feature's touch list, so it was read, not edited): `next build` produces a
# `.next/` directory that still needs `node_modules` at runtime, unlike a
# standalone build's self-contained `server.js`. The runtime stage below
# therefore carries a `--prod`-only `node_modules` alongside `.next/`,
# mirroring exactly what `apps/web/package.json`'s own `start` script
# already runs locally (`next start`), just without pnpm in the final image.

ARG NODE_VERSION=24.19.0

# ── deps — full install (next build needs devDependencies: typescript,
#    tailwind, eslint's type-aware config, etc.) ───────────────────────────
FROM node:${NODE_VERSION}-bookworm-slim AS deps
RUN corepack enable && corepack install -g pnpm@11.22.0
WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

# ── build — `next build` (apps/web/package.json's own `build` script) ─────
FROM deps AS build
COPY . .
# GATEWAY_BASE_URL/WEB_SESSION_PASSWORD are read by apps/web/src/server/config.ts
# at REQUEST time (`server-only`, never inlined into a client bundle — see
# that file), so no secret or environment-specific value needs to be a
# build arg here; the values that matter are supplied at container start
# (docker-compose.apps.yml's `web` service `environment:` block).
RUN pnpm run build

# ── prod-deps — the same install, --prod only (no typescript/eslint/vitest/
#    playwright in the runtime image) ──────────────────────────────────────
FROM node:${NODE_VERSION}-bookworm-slim AS prod-deps
RUN corepack enable && corepack install -g pnpm@11.22.0
WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile --prod

# ── runtime — plain Node, no pnpm (avoids a corepack shim + registry check
#    for a package manager the final image never runs), no source ─────────
FROM node:${NODE_VERSION}-bookworm-slim AS runtime
ENV NODE_ENV=production
WORKDIR /app
COPY --chown=node:node --from=prod-deps /app/node_modules ./node_modules
COPY --chown=node:node --from=prod-deps /app/package.json ./package.json
COPY --chown=node:node --from=build /app/.next ./.next
COPY --chown=node:node --from=build /app/next.config.ts ./next.config.ts
# No `public/` directory exists in apps/web today (checked: `ls apps/web/public`
# — does not exist), so there is nothing to copy; add a COPY here the day
# one is committed.

# ── non-root ──────────────────────────────────────────────────────────────
# The official node:*-bookworm-slim image's own unprivileged `node` user
# (uid 1000, gid 1000) — same reasoning as infra/docker/service/Dockerfile's
# `app` user: `USER` after every COPY so the `--chown` flags above are what
# actually grant ownership.
USER node

# No curl/wget in this slim image (checked:
# `docker run --rm node:24.19.0-bookworm-slim sh -c "which curl wget"` — no
# output) — Node's own http client drives the healthcheck instead, same
# mechanism #7's own web Dockerfile uses for the identical reason. apps/web
# exposes no /health route (checked: no match under apps/web/src for a
# health endpoint) — GET / is the best-effort proxy, same as #7: a redirect
# or even a 401 still proves the Next.js server process is alive and
# serving, which is the only thing this check is for.
HEALTHCHECK --interval=5s --timeout=5s --retries=20 --start-period=20s \
  CMD node -e "const p=process.env.WEB_PORT||3010;require('http').get('http://127.0.0.1:'+p+'/',r=>process.exit(r.statusCode<500?0:1)).on('error',()=>process.exit(1))"

# Mirrors apps/web/package.json's own `start` script exactly (shell form, so
# ${WEB_PORT:-3010} is expanded by the container's shell at run time against
# whatever docker-compose.apps.yml's `web` service sets) — just without the
# `pnpm run` wrapper, which the runtime stage does not carry.
CMD node --env-file-if-exists=../../.env ./node_modules/next/dist/bin/next start --port ${WEB_PORT:-3010}
