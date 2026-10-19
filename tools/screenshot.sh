#!/usr/bin/env bash
# Capture only the TEST INSTANCE's window (never the user's game or desktop) to .devlog/screens/<timestamp>[-label].png.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: screenshot.sh [label]    (prints the PNG path; fails if the test instance isn't running)"
  exit 0
fi

pid="$(test_pid)"
[[ -n "$pid" ]] || die "the test instance is not running (tools/autotest.py start)"
# The instance's own display (a private Xvfb), read from its environment.
DISPLAY="$(tr '\0' '\n' < "/proc/$pid/environ" | sed -n 's/^DISPLAY=//p')"
export DISPLAY
[[ -n "$DISPLAY" ]] || die "no DISPLAY in the test instance's environment"

# Match the exact window class: a title match also hits browser tabs about RimWorld.
mapfile -t wins < <(xdotool search --classname '^RimWorldLinux$' 2>/dev/null || true)
(( ${#wins[@]} == 1 )) || die "expected exactly one RimWorldLinux window on $DISPLAY, found ${#wins[@]}"

mkdir -p "$DEVLOG/screens"
out="$DEVLOG/screens/$(date +%Y%m%d-%H%M%S)${1:+-$1}.png"
import -window "${wins[0]}" "$out"
echo "$out"
