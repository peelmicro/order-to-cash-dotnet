#!/usr/bin/env bash
# init.sh — Environment and state coherence check.
#
# Run this at the START of every session, and again before declaring any
# feature `done`. If it fails, the session must not advance.
#
# Exit codes: 0 = healthy, 1 = at least one [FAIL].

set -u

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[0;33m'; BLUE='\033[0;34m'; NC='\033[0m'
ok()   { printf "${GREEN}[OK]${NC}    %s\n" "$1"; }
warn() { printf "${YELLOW}[WARN]${NC}  %s\n" "$1"; }
fail() { printf "${RED}[FAIL]${NC}  %s\n" "$1"; EXIT_CODE=1; }
section() { printf "\n${BLUE}── %s ${NC}\n" "$1"; }

EXIT_CODE=0
cd "$(dirname "$0")" || exit 1

# ─────────────────────────────────────────────────────────────
section "1. Environment"

# .NET is the backend toolchain. global.json pins the SDK band; if the pin cannot
# be satisfied, `dotnet --version` fails outright rather than silently choosing
# another SDK, which is exactly the behaviour we want a session check to surface.
if command -v dotnet >/dev/null 2>&1; then
  if DOTNET_ACTUAL="$(dotnet --version 2>/dev/null)"; then
    if [ -f global.json ]; then
      DOTNET_WANTED="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]+"' global.json | head -1 | grep -oE '[0-9][^"]*')"
      if [ "$DOTNET_ACTUAL" = "$DOTNET_WANTED" ]; then
        ok "dotnet $DOTNET_ACTUAL matches global.json"
      else
        ok "dotnet $DOTNET_ACTUAL satisfies global.json ($DOTNET_WANTED, rollForward)"
      fi
    else
      ok "dotnet $DOTNET_ACTUAL (no global.json)"
    fi
  else
    fail "dotnet is installed but cannot resolve an SDK — check global.json"
  fi
else
  fail "dotnet is not installed"
fi

# Node and pnpm exist for apps/web only; the backend has no Node dependency.
# (The backlog validator below is also Node — a deliberate reuse of #7's proven
# script rather than a gratuitous rewrite that would muddy the benchmark.)
if command -v node >/dev/null 2>&1; then
  NODE_ACTUAL="$(node -v | sed 's/^v//')"
  if [ -f .nvmrc ]; then
    NODE_WANTED="$(tr -d ' \n\r' < .nvmrc)"
    if [ "$NODE_ACTUAL" = "$NODE_WANTED" ]; then
      ok "node $NODE_ACTUAL matches .nvmrc"
    else
      warn "node $NODE_ACTUAL does not match .nvmrc ($NODE_WANTED) — run 'nvm use'"
    fi
  else
    ok "node $NODE_ACTUAL (no .nvmrc)"
  fi
else
  fail "node is not installed"
fi

if command -v pnpm >/dev/null 2>&1; then ok "pnpm $(pnpm -v)"; else fail "pnpm is not installed"; fi

if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then ok "docker daemon reachable"; else warn "docker installed but daemon not reachable"; fi
else
  warn "docker not installed (required from phase 4 onwards)"
fi

# ─────────────────────────────────────────────────────────────
section "2. Harness files"

for f in AGENTS.md CLAUDE.md CHECKPOINTS.md feature_list.json progress/current.md progress/history.md; do
  [ -f "$f" ] && ok "$f present" || fail "$f is missing"
done

AGENT_COUNT=$(find .claude/agents -maxdepth 1 -name '*.md' 2>/dev/null | wc -l | tr -d ' ')
if [ "$AGENT_COUNT" -ge 6 ]; then ok ".claude/agents/ has $AGENT_COUNT agent definitions"; else fail ".claude/agents/ has only $AGENT_COUNT definitions (expected >= 6)"; fi

# Every agent must declare its model explicitly, or say it deliberately inherits.
for f in .claude/agents/*.md; do
  [ -e "$f" ] || continue
  if grep -q '^model:' "$f"; then
    ok "$(basename "$f") pins model: $(grep '^model:' "$f" | head -1 | cut -d' ' -f2)"
  elif grep -qi 'inherit' "$f"; then
    ok "$(basename "$f") deliberately unpinned (documented)"
  else
    fail "$(basename "$f") neither pins a model nor documents inheriting one"
  fi
done

# ─────────────────────────────────────────────────────────────
section "3. Backlog coherence"

if [ -f feature_list.json ] && command -v node >/dev/null 2>&1; then
  node - <<'NODE'
const fs = require('fs');
let d;
try { d = JSON.parse(fs.readFileSync('feature_list.json', 'utf8')); }
catch (e) { console.log(`\x1b[0;31m[FAIL]\x1b[0m  feature_list.json is not valid JSON: ${e.message}`); process.exit(1); }

const ok   = m => console.log(`\x1b[0;32m[OK]\x1b[0m    ${m}`);
const fail = m => { console.log(`\x1b[0;31m[FAIL]\x1b[0m  ${m}`); process.exitCode = 1; };
const warn = m => console.log(`\x1b[0;33m[WARN]\x1b[0m  ${m}`);

const valid = d.rules.valid_status;
ok(`feature_list.json parsed — ${d.features.length} features`);

const bad = d.features.filter(f => !valid.includes(f.status));
bad.length ? fail(`invalid status on: ${bad.map(f => `${f.name}=${f.status}`).join(', ')}`)
           : ok('every status is in the valid set');

const inProgress = d.features.filter(f => f.status === 'in_progress');
if (inProgress.length > 1) fail(`${inProgress.length} features in_progress (max 1): ${inProgress.map(f => f.name).join(', ')}`);
else if (inProgress.length === 1) ok(`1 feature in_progress: ${inProgress[0].name}`);
else ok('no feature in_progress');

const ids = d.features.map(f => f.id);
new Set(ids).size === ids.length ? ok('feature ids are unique') : fail('duplicate feature ids');

const blocked = d.features.filter(f => f.status === 'blocked');
if (blocked.length) warn(`${blocked.length} blocked: ${blocked.map(f => f.name).join(', ')}`);

// SDD coherence: a sdd:true feature past `pending` needs its triple-doc.
const needsSpec = d.features.filter(f => f.sdd && ['spec_ready','in_progress','in_review','done'].includes(f.status));
let missing = 0;
for (const f of needsSpec) {
  for (const doc of ['requirements.md','design.md','tasks.md']) {
    if (!fs.existsSync(`specs/${f.name}/${doc}`)) { fail(`specs/${f.name}/${doc} missing (feature is ${f.status})`); missing++; }
  }
}
if (!missing) ok(`SDD coherence: ${needsSpec.length} sdd feature(s) past pending have their triple-doc`);

const done = d.features.filter(f => f.status === 'done').length;
ok(`progress: ${done}/${d.features.length} features done`);
NODE
  [ $? -ne 0 ] && EXIT_CODE=1
else
  fail "cannot validate feature_list.json (missing file or node)"
fi

# ─────────────────────────────────────────────────────────────
section "4. Session file in lockstep"

# CHECKPOINTS.md C2's fourth box: progress/current.md describes the ACTIVE
# session, never leftovers. It has been re-opened and hand-closed every feature
# — three reviews in a row here, and three times in #7 before that. A checkpoint
# that is broken and repaired by hand every single feature is not a discipline,
# it is a chore with a good excuse; its persistence across six reviews is what
# makes it a check rather than a seventh advisory.
if [ -f progress/current.md ] && [ -f feature_list.json ] && command -v node >/dev/null 2>&1; then
  LOCKSTEP="$(node -e '
    const fs = require("fs");
    const d = JSON.parse(fs.readFileSync("feature_list.json", "utf8"));
    const cur = fs.readFileSync("progress/current.md", "utf8");
    const line = (cur.match(/^\*\*Feature:\*\*.*$/m) || [""])[0];
    // A feature is ACTIVE while it is in_progress OR in_review: during a
    // review pass current.md should still name it, not claim idleness. The
    // first version of this check omitted in_review and so failed on correct
    // state during every review — a guard firing on something that is not
    // wrong, which trains its reader to ignore it. Found by review D7.
    const active = d.features.filter(f => f.status === "in_progress" || f.status === "in_review");
    if (active.length >= 1) {
      const named = active.some(f => line.includes(f.name));
      process.stdout.write(named ? "" : `names none of the active feature(s) [${active.map(f => `${f.name}=${f.status}`).join(", ")}]: "${line.trim()}"`);
    } else {
      const idle = /none|idle|awaiting/i.test(line);
      process.stdout.write(idle ? "" : `claims a feature while none is active: "${line.trim()}"`);
    }
  ' 2>/dev/null)"
  if [ -z "$LOCKSTEP" ]; then ok "progress/current.md is in lockstep with the backlog"
  else fail "progress/current.md $LOCKSTEP"; fi
else
  warn "cannot check progress/current.md lockstep"
fi

# ─────────────────────────────────────────────────────────────
section "5. Superseded rules"

# An amendment is not finished when the canonical file is edited — only when
# nothing anywhere still asserts the old rule. Found the hard way in Phase 8:
# .claude/agents/reviewer.md kept a superseded non-negotiable and would have had
# the next reviewer reject correct code, from disk, exactly as instructed.
if [ -f .superseded-rules ]; then
  SUPERSEDED_HITS=0
  while IFS= read -r rule; do
    case "$rule" in ''|'#'*) continue ;; esac
    HITS="$(grep -rlF -- "$rule" --include='*.md' --include='*.json' --include='*.sh' --include='*.yml' . 2>/dev/null \
              | grep -vE '^\./(progress/|\.superseded-rules|node_modules/|bin/|obj/)' || true)"
    if [ -n "$HITS" ]; then
      fail "superseded rule text still present: \"$(printf '%.60s' "$rule")...\""
      printf '%s\n' "$HITS" | sed 's/^/          /'
      SUPERSEDED_HITS=$((SUPERSEDED_HITS+1))
    fi
  done < .superseded-rules
  [ "$SUPERSEDED_HITS" -eq 0 ] && ok "no superseded rule text outside progress/"
else
  warn ".superseded-rules not present — amendments are unswept"
fi

# ─────────────────────────────────────────────────────────────
section "5b. Backlog tripwire"

# A feature id that VANISHES, or a `done` that reverts, is the one backlog
# corruption every other check is blind to: section 3 validates SHAPE, and a
# backlog with a feature missing is still perfectly shaped. Twice in Phase 8 an
# agent mangled feature_list.json with a whole-file rewrite and reverted with
# `git checkout --`, silently discarding uncommitted entries — once losing a
# backlog item added minutes earlier, which nothing caught. Prose did not stop
# the second occurrence, and the rule was in the offending agent's context both
# times, so this is the mechanical form.
#
# The snapshot is refreshed by THIS script on every clean run, so it needs no
# discipline to maintain. It is deliberately untracked: it is a within-session
# tripwire, not a shared artifact.
BACKLOG_SNAPSHOT=".backlog-snapshot"
CURRENT_BACKLOG="$(node -e '
  const d = require("./feature_list.json");
  console.log(d.features.map(f => f.id + ":" + f.status).sort((a,b) => Number(a.split(":")[0]) - Number(b.split(":")[0])).join("\n"));
' 2>/dev/null || true)"

if [ -z "$CURRENT_BACKLOG" ]; then
  warn "backlog tripwire skipped — feature_list.json could not be read"
elif [ -f "$BACKLOG_SNAPSHOT" ]; then
  TRIPWIRE_FAILED=0
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    OLD_ID="${line%%:*}"; OLD_STATUS="${line#*:}"
    NEW_LINE="$(printf '%s\n' "$CURRENT_BACKLOG" | grep -E "^${OLD_ID}:" || true)"
    if [ -z "$NEW_LINE" ]; then
      fail "backlog tripwire: feature id ${OLD_ID} has DISAPPEARED since the last clean init.sh run"
      TRIPWIRE_FAILED=1
    else
      NEW_STATUS="${NEW_LINE#*:}"
      if [ "$OLD_STATUS" = "done" ] && [ "$NEW_STATUS" != "done" ]; then
        fail "backlog tripwire: feature id ${OLD_ID} reverted from done to ${NEW_STATUS}"
        TRIPWIRE_FAILED=1
      fi
    fi
  done < "$BACKLOG_SNAPSHOT"
  if [ "$TRIPWIRE_FAILED" -eq 0 ]; then
    ok "backlog tripwire: no feature lost, no done reverted"
  else
    printf '          %s\n' "Recover from git or from the session record — do NOT run 'git checkout -- feature_list.json'."
  fi
else
  ok "backlog tripwire: no snapshot yet — baseline created by this run"
fi

# ─────────────────────────────────────────────────────────────
section "6. Repository state"

if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  ok "git repo on branch '$(git rev-parse --abbrev-ref HEAD)'"
  DIRTY=$(git status --porcelain | wc -l | tr -d ' ')
  [ "$DIRTY" -eq 0 ] && ok "working tree clean" || warn "$DIRTY uncommitted change(s) — expected mid-session"
  git config --local --get user.email >/dev/null 2>&1 \
    && ok "repo-local git identity: $(git config --local --get user.name) <$(git config --local --get user.email)>" \
    || warn "no repo-local git identity set"
else
  fail "not inside a git repository"
fi

# ─────────────────────────────────────────────────────────────
section "7. Tests"

if ls ./*.sln >/dev/null 2>&1; then
  warn "solution present — run './quality.sh' before closing a feature (not run here to keep init.sh fast)"
else
  warn "no .sln yet (arrives in phase 5)"
fi

if [ -f apps/web/package.json ]; then
  warn "apps/web present — run 'pnpm --filter web test' before closing a web feature"
fi

# ─────────────────────────────────────────────────────────────
printf "\n"
if [ "$EXIT_CODE" -eq 0 ]; then
  # Refresh the tripwire baseline ONLY on a clean run, so a damaged backlog is
  # never blessed as the new normal by the very script that flagged it.
  [ -n "${CURRENT_BACKLOG:-}" ] && printf '%s\n' "$CURRENT_BACKLOG" > "${BACKLOG_SNAPSHOT:-.backlog-snapshot}"
  printf "${GREEN}══ init.sh: environment and state are coherent ══${NC}\n"
else
  printf "${RED}══ init.sh: FAILURES above — do not advance the session ══${NC}\n"
fi
exit $EXIT_CODE
