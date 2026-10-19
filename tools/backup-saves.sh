#!/usr/bin/env bash
# Archive the USER's RimWorld profile (Saves, Config, mod lists, prefs; logs excluded) outside the repo.
# Read-only on the profile: it only tars it. Refuses while the user's game runs, so no save is mid-write.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: backup-saves.sh    (archive dir: \$BACKUP_DIR, default ~/RimWorldBackups)"
  exit 0
fi

[[ -z "$(user_game_pid)" ]] || die "the user's RimWorld is running; it must be closed so no save is mid-write"
[[ -d "$USER_RW_DATA/Saves" ]] || die "no Saves/ in $USER_RW_DATA"

BACKUP_DIR="${BACKUP_DIR:-$HOME/RimWorldBackups}"
mkdir -p "$BACKUP_DIR"
out="$BACKUP_DIR/$(date +%Y%m%d-%H%M%S).tar.gz"

tar -C "$(dirname "$USER_RW_DATA")" --exclude='*.log' -czf "$out" "$(basename "$USER_RW_DATA")"

listing="$(tar -tzf "$out")"
for d in Saves Config; do
  grep -q "/$d/" <<<"$listing" || die "archive is missing $d/: $out"
done

echo "$out ($(du -h "$out" | cut -f1), $(grep -c '\.rws$' <<<"$listing") saves)"
echo "restore: tar -C \"$(dirname "$USER_RW_DATA")\" -xzf \"$out\""
