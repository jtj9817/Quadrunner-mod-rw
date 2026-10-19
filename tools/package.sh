#!/usr/bin/env bash
# Build a clean release copy in dist/Quadrunner: only the folders the game reads, built from a clean git tree, linted.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: package.sh    (build + copy About/Defs/Textures/1.6 to dist/Quadrunner)"
  exit 0
fi

[[ -z "$(git_ status --porcelain)" ]] || die "uncommitted changes: commit first, so the build id names a real commit"
"$HERE/build.sh"

DIST="$REPO/dist/Quadrunner"
NEW="$REPO/dist/.Quadrunner.new"
rm -rf "$NEW"
mkdir -p "$NEW/1.6/Assemblies"
for dir in About Defs Textures Patches Languages Sounds; do
  [[ ! -d "$REPO/$dir" ]] || cp -r "$REPO/$dir" "$NEW/"
done
cp "$REPO/1.6/Assemblies/Quadrunner.dll" "$NEW/1.6/Assemblies/"
# The game ignores files at the mod root.
cp "$DEVLOG/last-build" "$NEW/BUILD"

python3 -I "$HERE/rwdefs.py" lint "$RW_DIR" "$NEW"
rm -rf "$DIST"
mv "$NEW" "$DIST"
(cd "$DIST" && find . -type f | sort | sed 's|^\./|  |')
echo "package: dist/Quadrunner, $(du -sh "$DIST" | cut -f1), build $(cat "$DIST/BUILD")"
