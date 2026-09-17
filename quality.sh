#!/usr/bin/env bash
# quality.sh — format check + build + test + coverage, run from the solution root.
#
# Coverage is COLLECTED and PRINTED here. The gate that FAILS the build when
# coverage drops below the CLAUDE.md thresholds (>=80% domain, >=60% overall)
# is feature 34 (sonarqube_quality_gates, phase 21) — see CLAUDE.md's testing
# conventions ("Coverage gates ... verified to fail when breached"). Until
# that feature lands, do NOT fake a gate that does not gate: this script
# reports a number, it does not enforce one.
#
# Exit codes: 0 = format clean + build succeeded + all tests passed, for the
#             .NET solution AND apps/web.
#             non-zero = the first failing step's exit code.
#
# QUALITY_ONLY=web ./quality.sh runs only section 5 (apps/web) — the switch
# exists so each web gate can be armed without a 30-minute .NET run in
# between. Unset (the default), every section runs.

set -u

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[0;33m'; BLUE='\033[0;34m'; NC='\033[0m'
ok()      { printf "${GREEN}[OK]${NC}    %s\n" "$1"; }
info()    { printf "${BLUE}[INFO]${NC}  %s\n" "$1"; }
warn()    { printf "${YELLOW}[WARN]${NC}  %s\n" "$1"; }
fail()    { printf "${RED}[FAIL]${NC}  %s\n" "$1"; }
section() { printf "\n${BLUE}── %s ${NC}\n" "$1"; }

cd "$(dirname "$0")" || exit 1

SLN="OrderToCash.sln"
COVERAGE_DIR="./TestResults"
QUALITY_ONLY="${QUALITY_ONLY:-}"

if [ -n "$QUALITY_ONLY" ] && [ "$QUALITY_ONLY" != "web" ]; then
  fail "QUALITY_ONLY must be empty or 'web' (got '$QUALITY_ONLY')"
  exit 2
fi

if [ "$QUALITY_ONLY" = "web" ]; then
  warn "QUALITY_ONLY=web — skipping the .NET sections 1-4"
else

# ─────────────────────────────────────────────────────────────
section "1. Format check"

if dotnet format "$SLN" --verify-no-changes; then
  ok "dotnet format --verify-no-changes: clean"
else
  fail "dotnet format found unformatted files — run 'dotnet format $SLN' and re-commit"
  exit 1
fi

# ─────────────────────────────────────────────────────────────
section "2. Build"

if dotnet build "$SLN" --nologo; then
  ok "dotnet build: succeeded"
else
  fail "dotnet build failed"
  exit 1
fi

# ─────────────────────────────────────────────────────────────
section "3. Test + coverage"

rm -rf "$COVERAGE_DIR"

if dotnet test "$SLN" --nologo --collect:"XPlat Code Coverage" --results-directory "$COVERAGE_DIR"; then
  ok "dotnet test: all tests passed"
else
  TEST_EXIT=$?
  fail "dotnet test failed"
  exit "$TEST_EXIT"
fi

# ─────────────────────────────────────────────────────────────
section "4. Coverage summary"

REPORT_FOUND=0
while IFS= read -r -d '' report; do
  REPORT_FOUND=1
  LINE_RATE="$(grep -oE 'line-rate="[0-9.]+"' "$report" | head -1 | grep -oE '[0-9.]+')"
  if [ -n "${LINE_RATE:-}" ]; then
    PCT="$(awk -v r="$LINE_RATE" 'BEGIN { printf "%.1f", r * 100 }')"
    info "coverage report: $report — line coverage ${PCT}%"
  else
    warn "coverage report found but line-rate could not be parsed: $report"
  fi
done < <(find "$COVERAGE_DIR" -name "coverage.cobertura.xml" -print0 2>/dev/null)

if [ "$REPORT_FOUND" -eq 0 ]; then
  warn "no coverage.cobertura.xml produced under $COVERAGE_DIR — coverlet.collector may not be wired into every test project"
fi

# TODO(feature 34 — sonarqube_quality_gates, phase 21): enforce >=80% domain /
# >=60% overall here (or via coverlet.msbuild + a threshold check), and prove
# it fails when breached. Do not add a gate here until that feature does —
# see CLAUDE.md, "Coverage gates ... verified to fail when breached".

fi # QUALITY_ONLY

# ─────────────────────────────────────────────────────────────
section "5. Web app (apps/web)"

# Each step is a gate that has been seen to FAIL on a planted violation
# (progress/impl_web_app.md, "Quality-gate arming"). #7's web lint exited 0
# while linting zero component files, so `pnpm lint` also runs
# scripts/lint-coverage.mjs, which fails when any source file escapes the config.
# Web coverage is reported, not enforced — the same rule as section 4.
WEB_DIR="./apps/web"
if ! command -v pnpm >/dev/null 2>&1; then
  fail "pnpm is not installed — apps/web cannot be checked (corepack enable, see .nvmrc)"
  exit 1
fi

web_step() {
  local label="$1"; shift
  info "apps/web: $label"
  if (cd "$WEB_DIR" && "$@"); then
    ok "apps/web: $label"
  else
    local code=$?
    fail "apps/web: $label failed (exit $code)"
    exit "$code"
  fi
}

web_step "install (frozen lockfile)" pnpm install --frozen-lockfile --reporter=append-only
web_step "OpenAPI types match specs/shared/openapi.yaml" pnpm run --silent types:check
web_step "lint (+ every source file linted)" pnpm run --silent lint
web_step "typecheck" pnpm run --silent typecheck
web_step "unit + component tests (Vitest), with coverage" pnpm run --silent test:coverage
if [ -f "$WEB_DIR/coverage/coverage-summary.json" ]; then
  WEB_LINES="$(node -e 'const s=require(process.argv[1]).total.lines;console.log(s.pct)' "$WEB_DIR/coverage/coverage-summary.json")"
  info "apps/web line coverage ${WEB_LINES}%"
fi
web_step "production build" env NEXT_TELEMETRY_DISABLED=1 pnpm run --silent build
web_step "integration tests against the production build" pnpm run --silent test:integration

ok "quality.sh finished"
