#!/usr/bin/env bash
# notification-template-payload-sweep.sh — the committed instrument for the
# Notifications templates' payload-interpolation-site population.
#
# Why this exists. The population of "every place a payload field's VALUE
# reaches an outgoing email" has now been counted by hand three times, by
# three different parties, in three different closing rounds of the same
# feature (ids 58/59), and produced three different numbers: 14 (the
# date-only slice), 89 (a string-scoped hand sweep that could not see
# collection-element fields), and 95 (this round's reviewer, who found the
# three nested-element sites and one live survivor among them, D1 in
# progress/review_notifications_mutation_gaps.md). A population that changes
# every time somebody counts it by hand is exactly what a committed,
# re-runnable instrument exists to settle, for this feature and for whoever
# opens these seven files next — including assessment #9's equivalent
# service, which has no reason to inherit a hand count rather than a script.
#
# What this script is NOT: a fully generic C#-type-inferring mutator. The
# templates in scope interpolate four shapes of value — string, DateTimeOffset,
# money (long, always via FormatMoney), and IReadOnlyList<T> collection
# elements — and a mutation must stay the same C# type as what it replaces or
# the armed source will not compile (a build failure is not a fired guard,
# per CLAUDE.md's arming protocol). So string/date/money sites are mutated by
# one GENERIC, type-safe substitution rule each (see classify_field below);
# the five sites that are not a bare `payload.<Field>` reference — the three
# collection/nested-element sites this round's review found, plus the
# survivor's own site — are hand-specified in the OVERRIDES table, because a
# generic token swap for e.g. `payload.CompensationSteps.Count == 0` would
# either not compile (`IEnumerable<T>.Take(0)` has no `.Count` property) or
# would mutate the wrong clause. Every one of those five is still reached by
# the SAME live enumeration (never a cached list) — the table only says HOW
# to mutate a site the enumeration already found, never adds a site the
# enumeration did not.
#
# Usage:
#   scripts/notification-template-payload-sweep.sh --enumerate
#       Live population enumeration + classification. Prints every
#       payload.<Field> and nested line./step.<Field> reference in the seven
#       templates, one classification line per hit, and the totals per
#       family. Re-run this any time the templates change — it is a search,
#       not a reading, every time.
#
#   scripts/notification-template-payload-sweep.sh --probe <site-number>
#       Arms exactly ONE site (1-based, in the order --enumerate prints),
#       following CLAUDE.md's arming protocol: backup, mutate, force rebuild,
#       run tests/Notifications.UnitTests, restore from the backup, force
#       rebuild again, confirm green. Reports CAUGHT, SURVIVED or
#       NOT-APPLIED. NOT-APPLIED means the expected old text was not found on
#       the expected line — the site has moved or the source has changed —
#       and the script aborts before running any test, exactly as
#       arm-probe.sh already does for the generic case.
#
#   scripts/notification-template-payload-sweep.sh --all
#       Runs --probe over the whole enumerated population and prints a
#       summary table. This is the full sweep: ~95 site probes, each a
#       backup + two rebuilds + two test runs, so budget for it — it is the
#       instrument for reproducing a population-wide verdict end to end, not
#       the fast path for one session.
#
# Every mutated file is restored from a fresh per-probe backup, never from
# `git checkout --` (most of these files are as likely to be mid-feature and
# untracked as not, and `git checkout` on an untracked path fails silently —
# CLAUDE.md's arming protocol names this failure mode explicitly).
set -euo pipefail
cd "$(dirname "$0")/.."

TEMPLATES_DIR="src/Notifications/Application/Templates"
TEST_PROJECT="tests/Notifications.UnitTests"

# ---------------------------------------------------------------------------
# Family classification for a bare `payload.<Field>` reference. Anything not
# listed here, and not a key in OVERRIDES, is the default family: string.
declare -A DATE_FIELDS=(
  [OrderDate]=1 [ConfirmedAt]=1 [DespatchDate]=1 [InvoiceDate]=1
  [ValueDate]=1 [CompletedAt]=1 [CancelledAt]=1
)
declare -A MONEY_FIELDS=([TotalAmount]=1 [Amount]=1)
# Collection-typed payload fields are never mutated by the generic rule
# (a literal cannot stand in for IReadOnlyList<T>) — they are always looked
# up in OVERRIDES by their own file:line:token key instead.
declare -A COLLECTION_FIELDS=([Lines]=1 [CompensationSteps]=1)

classify_field() {
  local field="$1"
  if [ -n "${DATE_FIELDS[$field]:-}" ]; then echo date
  elif [ -n "${MONEY_FIELDS[$field]:-}" ]; then echo money
  elif [ -n "${COLLECTION_FIELDS[$field]:-}" ]; then echo collection
  else echo string
  fi
}

# ---------------------------------------------------------------------------
# Hand-specified mutations for the five sites a generic token swap cannot
# safely reach (collection sites + nested-element sites). Key: "file:line:token".
# Value: "OLD<TAB>NEW" — OLD must occur exactly once on that line, or the
# probe reports NOT-APPLIED rather than guessing.
declare -A OVERRIDES=(
  ["OrderDespatchedTemplate.cs:14:payload.Lines"]="payload.Lines.Select	payload.Lines.Take(0).Select"
  ["OrderDespatchedTemplate.cs:14:line.ProductCode"]="line.ProductCode	\"WRONG-SKU\""
  ["OrderDespatchedTemplate.cs:14:line.Units"]="line.Units	(line.Units + 1)"
  ["OrderCancelledTemplate.cs:14:payload.CompensationSteps"]="Count == 0	Count == 999"
  ["OrderCancelledTemplate.cs:16:payload.CompensationSteps"]="payload.CompensationSteps.Select	payload.CompensationSteps.Take(0).Select"
  ["OrderCancelledTemplate.cs:16:step.Step"]="step.Step	step.EventType"
)

# ---------------------------------------------------------------------------
# Live enumeration. Path-excluded at the source (find -not -path), never by
# post-filtering grep's path:lineno:content output — CLAUDE.md is explicit
# that `grep -rn ... | grep -v` matches CONTENT, not path, and silently drops
# hits that happen to quote the excluded pattern.
enumerate() {
  find "$TEMPLATES_DIR" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -noE 'payload\.[A-Za-z]+' | sort -t: -k1,1 -k2,2n
  find "$TEMPLATES_DIR" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -noE '\b(line|step)\.[A-Za-z]+' | sort -t: -k1,1 -k2,2n
}

cmd_enumerate() {
  local i=0
  local n_string=0 n_money=0 n_date=0 n_collection=0 n_nested=0
  while IFS=: read -r file line token; do
    i=$((i + 1))
    local base fam
    base="$(basename "$file")"
    case "$token" in
      payload.*)
        local field="${token#payload.}"
        fam="$(classify_field "$field")"
        if [ "$fam" = collection ]; then
          fam="collection (via OVERRIDES: ${OVERRIDES["$base:$line:$token"]+set})"
          n_collection=$((n_collection + 1))
        else
          case "$fam" in
            string) n_string=$((n_string + 1)) ;;
            money) n_money=$((n_money + 1)) ;;
            date) n_date=$((n_date + 1)) ;;
          esac
        fi
        ;;
      line.*|step.*)
        fam="nested-element (via OVERRIDES: ${OVERRIDES["$base:$line:$token"]+set})"
        n_nested=$((n_nested + 1))
        ;;
    esac
    printf '%3d  %-28s %-30s %s\n' "$i" "$base:$line" "$token" "$fam"
  done < <(enumerate)
  echo "---"
  echo "string=$n_string money=$n_money date=$n_date collection=$n_collection nested=$n_nested total=$((n_string + n_money + n_date + n_collection + n_nested))"
}

# ---------------------------------------------------------------------------
# One arm-and-restore probe, CLAUDE.md's protocol exactly (see arm-probe.sh,
# which this reuses for the mutate/build/test/restore/build/confirm sequence).
probe_site() {
  local base="$1" line="$2" token="$3"
  local file="$TEMPLATES_DIR/$base"
  [ -f "$file" ] || { echo "NOT-APPLIED  (no such file: $file)"; return 0; }

  local old new key
  key="$base:$line:$token"
  if [ -n "${OVERRIDES[$key]:-}" ]; then
    old="${OVERRIDES[$key]%%$'\t'*}"
    new="${OVERRIDES[$key]#*$'\t'}"
  else
    case "$token" in
      payload.*)
        local field="${token#payload.}"
        old="$token"
        case "$(classify_field "$field")" in
          date) new="DateTimeOffset.UnixEpoch" ;;
          money) new="0L" ;;
          *) new="\"MUTATED-$field\"" ;;
        esac
        ;;
      *)
        echo "NOT-APPLIED  (no generic rule and no OVERRIDES entry for $key)"
        return 0
        ;;
    esac
  fi

  # Line-scoped count check: the mutation must apply to exactly one
  # occurrence on the named line, or this is NOT-APPLIED, not a guess.
  # `|| true` on the whole pipeline: with `pipefail`, grep's exit status when
  # it finds ZERO matches (a legitimate, expected outcome here — it is what
  # NOT-APPLIED means) would otherwise trip `set -e` before the hits==0 case
  # is even checked, aborting the script instead of reporting NOT-APPLIED.
  local hits
  hits="$(sed -n "${line}p" "$file" | grep -oF "$old" | wc -l || true)"
  if [ "$hits" -ne 1 ]; then
    echo "NOT-APPLIED  (expected exactly 1 occurrence of '$old' on $base:$line, found $hits)"
    return 0
  fi

  # No trap/nested-function trickery here on purpose: bash's dynamic scoping
  # of locals across a RETURN trap is not reliable enough to trust as the
  # only path back to a clean file, so every branch below that has mutated
  # the file restores it explicitly, itself, before returning — including
  # the build-failure branch.
  local bak
  bak="$(mktemp)"
  cp "$file" "$bak"

  # sed's regex metacharacters must be escaped in OLD; & is special in NEW.
  local esc_old esc_new
  esc_old="$(printf '%s' "$old" | sed -e 's/[.[\*^$/]/\\&/g')"
  esc_new="$(printf '%s' "$new" | sed -e 's/[&/\]/\\&/g')"
  sed -i "${line}s/${esc_old}/${esc_new}/" "$file"

  if cmp -s "$bak" "$file"; then
    rm -f "$bak"
    echo "NOT-APPLIED  (sed made no change — pattern anchored wrong)"
    return 0
  fi
  touch "$file"
  if ! dotnet build --no-incremental -v q --nologo >/dev/null 2>&1; then
    cp "$bak" "$file"; touch "$file"; rm -f "$bak"
    dotnet build --no-incremental -v q --nologo >/dev/null 2>&1 || true
    echo "NOT-APPLIED  (armed source does not compile — mutation not type-safe on this site; restored)"
    return 0
  fi
  local armed_failed
  armed_failed="$(dotnet test "$TEST_PROJECT" --nologo -v q 2>&1 | grep -cE '^Failed!' || true)"

  cp "$bak" "$file"; touch "$file"; rm -f "$bak"
  dotnet build --no-incremental -v q --nologo >/dev/null 2>&1 || true

  local restored_pass
  restored_pass="$(dotnet test "$TEST_PROJECT" --nologo -v q 2>&1 | grep -cE '^Passed!' || true)"
  if [ "$restored_pass" -eq 0 ]; then
    echo "*** RESTORE DID NOT TAKE — suite still failing after restore ***"
    return 1
  fi

  if [ "$armed_failed" -gt 0 ]; then echo "CAUGHT"
  else echo "SURVIVED"; fi
}

cmd_probe() {
  local target="${1:?usage: --probe <site-number>}"
  local i=0
  while IFS=: read -r file line token; do
    i=$((i + 1))
    if [ "$i" -eq "$target" ]; then
      local base
      base="$(basename "$file")"
      echo "site $i: $base:$line  $token"
      probe_site "$base" "$line" "$token"
      return $?
    fi
  done < <(enumerate)
  echo "NOT-APPLIED  (no site number $target — population is smaller than that)"
}

cmd_all() {
  local i=0
  local caught=0 survived=0 notapplied=0
  while IFS=: read -r file line token; do
    i=$((i + 1))
    local base result
    base="$(basename "$file")"
    result="$(probe_site "$base" "$line" "$token")"
    case "$result" in
      CAUGHT) caught=$((caught + 1)) ;;
      SURVIVED) survived=$((survived + 1)) ;;
      *) notapplied=$((notapplied + 1)) ;;
    esac
    printf '%3d  %-28s %-30s %s\n' "$i" "$base:$line" "$token" "$result"
  done < <(enumerate)
  echo "---"
  echo "CAUGHT=$caught SURVIVED=$survived NOT-APPLIED=$notapplied"
}

case "${1:-}" in
  --enumerate) cmd_enumerate ;;
  --probe) cmd_probe "${2:?usage: --probe <site-number>}" ;;
  --all) cmd_all ;;
  *)
    echo "usage: $0 --enumerate | --probe <n> | --all" >&2
    exit 1
    ;;
esac
