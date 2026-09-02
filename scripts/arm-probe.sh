#!/usr/bin/env bash
# arm-probe.sh — arm one guard, correctly, every time.
#
# Why this exists. CLAUDE.md's arming protocol is three clauses long, and the
# leader got it wrong twice in snippets handed to the human: once taking the
# backup AFTER the mutation (so the "backup" held the armed file), and both
# times falling back to `git checkout --` on an untracked path, which restores
# nothing and leaves the file armed while its own error scrolls past. A snippet
# written from memory is a guess. This is the thing that ran.
#
#   ./scripts/arm-probe.sh <file> <sed-expression> <test-project>
#
# Backs up FIRST, mutates, forces a rebuild, runs the suite, restores from the
# backup it actually took, forces a rebuild again, re-runs, and says whether the
# guard fired. It never uses git to restore, and an EXIT trap means a crash
# mid-probe cannot leave the tree armed.
set -euo pipefail

FILE="${1:?usage: arm-probe.sh <file> <sed-expression> <test-project>}"
EXPR="${2:?missing sed expression}"
PROJ="${3:?missing test project}"
BAK="$(mktemp)"

restore() {
  cp "$BAK" "$FILE"; touch "$FILE"; rm -f "$BAK"
  dotnet build --no-incremental -v q --nologo >/dev/null 2>&1 || true
}
trap restore EXIT

cp "$FILE" "$BAK"
sed -i "$EXPR" "$FILE"
if cmp -s "$BAK" "$FILE"; then
  echo "FATAL: the sed expression changed nothing — the probe would prove nothing." >&2
  exit 1
fi
touch "$FILE"
if ! dotnet build --no-incremental -v q --nologo >/dev/null 2>&1; then
  echo "FATAL: armed source does not compile. A build failure is not a fired guard." >&2
  exit 1
fi
ARMED="$(dotnet test "$PROJ" --nologo -v q 2>&1 | grep -cE '^Failed!' || true)"

restore; trap - EXIT
GREEN="$(dotnet test "$PROJ" --nologo -v q 2>&1 | grep -cE '^Passed!' || true)"

if [ "$ARMED" -gt 0 ]; then echo "  armed    -> suite FAILED (the guard fires)"
else echo "  armed    -> *** suite still green — GUARD DOES NOT GUARD ***"; fi
if [ "$GREEN" -gt 0 ]; then echo "  restored -> suite green"
else echo "  restored -> *** still failing — the restore did not take ***"; fi
[ "$ARMED" -gt 0 ] && [ "$GREEN" -gt 0 ]
