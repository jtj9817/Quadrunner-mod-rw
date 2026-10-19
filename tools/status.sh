#!/usr/bin/env bash
# Environment check, then the live state of the test instance and the user's game (tools/autotest.py status).
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: status.sh    (exit 1 = environment problem)"
  exit 0
fi

problems=0
ok()   { echo "  ok    $*"; }
warn() { echo "  WARN  $*"; problems=1; }

echo "environment"
version="$(cat "$RW_DIR/Version.txt" 2>/dev/null || echo missing)"
[[ "$version" == "$RW_EXPECTED_VERSION"* ]] && ok "game $version" \
  || warn "game version is '$version', expected $RW_EXPECTED_VERSION* (Steam update? stop and ask the user)"
for bin in dotnet python3 inkscape git Xvfb xdotool import; do
  command -v "$bin" > /dev/null && ok "$bin" || warn "$bin not found"
done
pgrep -x steam > /dev/null && ok "Steam running" || warn "Steam not running (workshop Harmony won't load)"
[[ -d "$HARMONY_DIR/1.6" ]] && ok "Harmony (workshop 2009463077)" || warn "Harmony not found at $HARMONY_DIR"
link="$RW_DIR/Mods/Quadrunner"
if [[ -L "$link" && "$(readlink -f "$link")" == "$REPO" ]]; then ok "Mods/Quadrunner → repo"
elif [[ -e "$link" ]]; then warn "Mods/Quadrunner is not the dev symlink to this repo"
else echo "  info  Mods/Quadrunner not linked yet (autotest.py creates it)"; fi
[[ -f "$LOCAL_TOOLS/decomp/.version" ]] && cmp -s "$LOCAL_TOOLS/decomp/.version" "$RW_DIR/Version.txt" \
  && ok "decompiled source current" || warn "decompiled source missing or stale (tools/vanilla.sh init)"
[[ -f "$DEVLOG/baseline-errors.txt" ]] && ok "error baseline present" || warn "no error baseline (tools/autotest.py baseline)"

echo "live"
python3 -I "$HERE/autotest.py" status | sed 's/^/  /'

(( problems == 0 )) || exit 1
echo "environment ok"
