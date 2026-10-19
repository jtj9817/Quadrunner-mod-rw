#!/usr/bin/env bash
# Look up vanilla RimWorld: decompiled Assembly-CSharp (once, cached) and Core XML Defs.
set -euo pipefail
source "$(dirname "$0")/env.sh"

DECOMP="$LOCAL_TOOLS/decomp"
ILSPY="$LOCAL_TOOLS/ilspy/ilspycmd"
MANAGED="$RW_DIR/RimWorldLinux_Data/Managed"
export DOTNET_ROLL_FORWARD=Major   # ilspycmd targets an older runtime than the installed SDK

usage() {
  cat <<'EOF'
usage: vanilla.sh <command>
  init            decompile Assembly-CSharp.dll into .tools/decomp/ (reruns when the game version changes)
  grep <regex>    search decompiled source + Data/Core/Defs (extended regex)
  def <defName>   print a Core def and its ParentName chain, e.g. vanilla.sh def Turret_MiniTurret
EOF
  exit "${1:-0}"
}

decomp_current() { [[ -f "$DECOMP/.version" ]] && cmp -s "$DECOMP/.version" "$RW_DIR/Version.txt"; }

case "${1:-}" in
  init)
    if decomp_current; then echo "decomp is current ($(cat "$DECOMP/.version"))"; exit 0; fi
    [[ -x "$ILSPY" ]] || dotnet tool install ilspycmd --tool-path "$LOCAL_TOOLS/ilspy"
    rm -rf "$DECOMP" && mkdir -p "$DECOMP"
    "$ILSPY" -p -o "$DECOMP" -r "$MANAGED" "$MANAGED/Assembly-CSharp.dll" > /dev/null
    cp "$RW_DIR/Version.txt" "$DECOMP/.version"
    echo "decompiled $(find "$DECOMP" -name '*.cs' | wc -l) files → $DECOMP"
    ;;
  grep)
    [[ -n "${2:-}" ]] || usage 1
    decomp_current || echo "warning: decomp missing or stale; run: vanilla.sh init" >&2
    grep -rnE --include='*.cs' "$2" "$DECOMP" | sed "s|^$DECOMP/|decomp/|" || true
    grep -rnE "$2" "$RW_DIR/Data/Core/Defs" | sed "s|^$RW_DIR/||" || true
    ;;
  def)
    [[ -n "${2:-}" ]] || usage 1
    python3 -I "$HERE/rwdefs.py" def "$RW_DIR" "$2"
    ;;
  -h | --help) usage 0 ;;
  *) usage 1 ;;
esac
