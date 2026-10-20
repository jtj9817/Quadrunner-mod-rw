#!/usr/bin/env bash
# Exercise tools/autotest.py against a fake game (tools/selftest/fake_game.py) that speaks AutoTest.cs's file protocol:
# watchdog, stop, packaged source/build isolation and clean-removal failures. Never touches the real game:
# everything lives in .devlog/selftest/, wiped on each run.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: selftest.sh    (exit 0 = every case behaved; needs Xvfb and a running Steam, like the real harness)"
  exit 0
fi

SELF="$DEVLOG/selftest"
rm -rf "$SELF"
mkdir -p "$SELF/devlog" "$SELF/game/Mods" "$SELF/harmony/About" "$SELF/harmony/1.6"
echo "1.6.4871 rev598" > "$SELF/game/Version.txt"
ln -s "$HERE/selftest/RimWorldLinux" "$SELF/game/RimWorldLinux"
export QR_DEVLOG="$SELF/devlog" RW_DIR="$SELF/game" HARMONY_DIR="$SELF/harmony" FAKE_EXE="$SELF/game/RimWorldLinux"
unset RW_DATA FAKE_FAIL FAKE_STARTUP_ERROR FAKE_REMOVAL_ERRORS FAKE_LOAD_FAIL FAKE_WRONG_MOD_SOURCE
echo "selftest.1" > "$QR_DEVLOG/last-build"

# The user's real game may be open; the fake instance doesn't care.
at() { python3 -I "$HERE/autotest.py" --ready-timeout 60 --timeout 60 "$@"; }
failures=0
# expect <exit code> <text the output must contain> <command...>
expect() {
  local want="$1" text="$2" got out
  shift 2
  set +e; out="$("$@" 2>&1)"; got=$?; set -e
  if [[ "$got" == "$want" ]] && grep -qF -- "$text" <<<"$out"; then
    echo "ok    $* → $got, \"$text\""
  else
    echo "FAIL  $* → exit $got (want $want), wanted \"$text\" in:"; sed 's/^/        /' <<<"$out"
    failures=$((failures + 1))
  fi
}

expect 0 "1 error signatures" at baseline --min-seconds 2 --quiet-seconds 3
expect 0 "2/2 PASS, 0 new error(s)" at run --no-build
expect 0 "1/1 PASS" at run --no-build smoke
expect 1 "no scenario matched" at run --no-build nothing-matches-this
echo "selftest.2" > "$QR_DEVLOG/last-build"
expect 0 "restarted in game" at run --no-build
expect 0 "build selftest.2" at run --no-build
expect 0 "stopped Xvfb" at stop
# A package has a different stamp from the dev build. pkg-run must cold-launch it, never build the dev source,
# and restore the development symlink on both success and failure.
export QR_PKG="$SELF/package"
mkdir -p "$QR_PKG/About" "$QR_PKG/1.6/Assemblies"
cp "$REPO/About/About.xml" "$QR_PKG/About/"
echo "fake assembly" > "$QR_PKG/1.6/Assemblies/Quadrunner.dll"
echo "selftest.package" > "$QR_PKG/BUILD"
expect 0 "build selftest.package" at pkg-run
[[ "$(readlink -f "$RW_DIR/Mods/Quadrunner")" == "$REPO" ]] \
  || { echo "FAIL  pkg-run did not restore the dev link"; failures=$((failures + 1)); }
export FAKE_WRONG_MOD_SOURCE=1
expect 1 "WRONG MOD SOURCE" at pkg-run
unset FAKE_WRONG_MOD_SOURCE
[[ "$(readlink -f "$RW_DIR/Mods/Quadrunner")" == "$REPO" ]] \
  || { echo "FAIL  failed pkg-run did not restore the dev link"; failures=$((failures + 1)); }
# The independent observer's verdict is insufficient: missing-def/ticking errors must still fail removal.
mkdir -p "$QR_DEVLOG/rwdata/Saves"
echo "fake removal save" > "$QR_DEVLOG/rwdata/Saves/QR_removal.rws"
echo "fake colonist" > "$QR_DEVLOG/autotest/removal-expected.txt"
expect 0 "LOADED CLEAN" at removal-check
export FAKE_REMOVAL_ERRORS=1
expect 1 "3 new errors" at removal-check
unset FAKE_REMOVAL_ERRORS
export FAKE_LOAD_FAIL=1
expect 1 "REMOVAL FAILED" at removal-check
unset FAKE_LOAD_FAIL
[[ ! -e "$RW_DIR/Mods/QuadrunnerRemovalProbe" && ! -e "$QR_DEVLOG/rwdata/Saves/autostart.rws" ]] \
  || { echo "FAIL  removal-check left a probe install or autostart save"; failures=$((failures + 1)); }
expect 0 "stopped Xvfb" at stop
# Fresh launch with a failing scenario and a startup error the baseline knows (hidden) ...
export FAKE_FAIL="Save and reload" FAKE_STARTUP_ERROR=1
expect 1 "1/2 PASS, 0 new error(s)" at run --no-build
expect 0 "stopped Xvfb" at stop
# ... and the same error without the baseline (counted).
rm "$QR_DEVLOG/baseline-errors.txt"
unset FAKE_FAIL
expect 1 "2/2 PASS, 1 new error(s)" at run --no-build
unset FAKE_STARTUP_ERROR
expect 2 "watchdog: OK" at --hang-after 5 hang
expect 0 "2/2 PASS" at run --no-build
expect 0 "stopped Xvfb" at stop
[[ -n "$(ls "$QR_DEVLOG/runs")" ]] && echo "ok    runs archived: $(ls "$QR_DEVLOG/runs" | tr '\n' ' ')" \
  || { echo "FAIL  no archived runs"; failures=$((failures + 1)); }

(( failures == 0 )) && echo "selftest: all cases ok" || { echo "selftest: $failures case(s) failed"; exit 1; }
