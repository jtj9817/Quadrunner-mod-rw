# Shared paths and helpers, sourced by tools/*.sh. Override any path via the environment.
# tools/autotest.py reads the same variables with the same defaults; keep the two in step.

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HERE="$REPO/tools"
DEVLOG="${QR_DEVLOG:-$REPO/.devlog}"   # runs, baseline, last build id, test profile (git-ignored)
LOCAL_TOOLS="$REPO/.tools"    # ilspycmd, decompiled source (git-ignored)

RW_DIR="${RW_DIR:-$HOME/.steam/debian-installation/steamapps/common/RimWorld}"
# The TEST INSTANCE's profile (-savedatafolder), never the user's ~/.config/unity3d/... profile.
RW_DATA="${RW_DATA:-$DEVLOG/rwdata}"
RW_LOG="$RW_DATA/Player.log"
RW_EXPECTED_VERSION="1.6."    # Version.txt prefix this project is built against
HARMONY_DIR="${HARMONY_DIR:-$RW_DIR/../../workshop/content/294100/2009463077}"

mkdir -p "$DEVLOG"

git_() { git -c safe.directory="$REPO" -C "$REPO" "$@"; }
# Test instance: the RimWorldLinux started with our -savedatafolder. User's game: any other RimWorldLinux.
test_pid() { pgrep -f -- "-savedatafolder=$RW_DATA" | head -1 || true; }
user_game_pid() { pgrep -x RimWorldLinux | grep -vxF "$(test_pid)" | head -1 || true; }
die() { echo "error: $*" >&2; exit 1; }
