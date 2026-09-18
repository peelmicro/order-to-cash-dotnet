#!/bin/bash
# Imports the four committed n8n demo workflows (n8n/workflows/*.json) into the
# running n8n container — see specs/shared/n8n-workflows.md.
#
# Ported byte-for-byte in intent from #7 (order-to-cash-nestjs)'s
# scripts/import-n8n-workflows.sh, adapting only the container name
# (#8's compose project is `otcnet`, giving `otcnet-n8n`, not #7's `otc-n8n`
# — see docker-compose.infra.yml's own "Why name: otcnet" comment for why the
# two stacks never share a namespace). This is the manual, run-it-whenever-
# you-like path — for the automatic one that runs on every `docker compose
# up`, see the `n8n-init` one-shot service in docker-compose.infra.yml and
# infra/n8n/import-workflows-on-startup.sh. Both ultimately call the exact
# same `n8n import:workflow --separate --input=...` CLI command against the
# same mounted directory, so they cannot drift on WHAT they import — the
# only difference is one runs against the already-running container via
# `docker exec`, the other is its own ephemeral container sharing the same
# `n8n_data` volume.
#
# Every imported workflow always lands INACTIVE (n8n's own
# `import:workflow` default: newly imported workflows are deactivated),
# regardless of the committed JSON's own `active` field — activate a
# workflow from the n8n UI (http://localhost:5678/workflows) when you
# actually want its schedule/webhook to start firing.
#
# This manual path has no N8N_WORKFLOWS_ENABLED gate, unlike the auto-import
# path (infra/n8n/import-workflows-on-startup.sh) — a human running this
# explicitly always means it; see that script's header for the full
# reasoning.
#
# Usage:
#   pnpm n8n:import
#   ./scripts/import-n8n-workflows.sh

set -e

CONTAINER=otcnet-n8n
WORKFLOW_DIR=/home/node/workflows

echo "Importing n8n workflows from $WORKFLOW_DIR into container $CONTAINER..."

if ! docker ps --format '{{.Names}}' | grep -q "^$CONTAINER$"; then
  echo "Error: container $CONTAINER is not running. Run 'pnpm dc:up:infra' first." >&2
  exit 1
fi

docker exec "$CONTAINER" n8n import:workflow --separate --input="$WORKFLOW_DIR"

echo ""
echo "Done. Open http://localhost:5678/workflows to see the imported workflows."
echo "They are imported INACTIVE by design — activate the ones you want running from the UI."
