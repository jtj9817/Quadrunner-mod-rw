#!/usr/bin/env bash
# Export Art/**/*.svg to PNG at the mirrored path (Art/Textures/X.svg → Textures/X.png), sized by the SVG's width/height.
# Each element with id="part-<name>" is also exported alone, on the full page, as X_<Name>.png (layers the code
# draws separately, e.g. a bee's wings, body and light). A facing sprite keeps the facing last, so Graphic_Multi finds the
# part: X_east.svg#part-frontNear → X_FrontNear_east.png. Art/About/ goes last: its SVGs embed the exported sprites.
set -euo pipefail
source "$(dirname "$0")/env.sh"

if [[ "${1:-}" == -h* || "${1:-}" == --help ]]; then
  echo "usage: export-textures.sh [file.svg ...]    (default: every SVG under Art/)"
  exit 0
fi

command -v inkscape >/dev/null || die "inkscape not found"

if (( $# )); then
  svgs=("$@")
else
  mapfile -t svgs < <(find "$REPO/Art" -name '*.svg' -not -path "$REPO/Art/About/*" | sort; find "$REPO/Art/About" -name '*.svg' | sort)
fi
(( ${#svgs[@]} )) || die "no SVGs under Art/"

png_size() {
  python3 -I -c 'import struct,sys; d=open(sys.argv[1],"rb").read(24); print("%dx%d" % struct.unpack(">II", d[16:24]))' "$1"
}

for svg in "${svgs[@]}"; do
  svg="$(realpath "$svg")"
  rel="${svg#$REPO/Art/}"
  [[ "$rel" != "$svg" ]] || die "$svg is not under Art/"
  png="$REPO/${rel%.svg}.png"
  mkdir -p "$(dirname "$png")"
  inkscape --export-type=png --export-filename="$png" "$svg" 2>/dev/null
  echo "export: Art/$rel → ${png#$REPO/} ($(png_size "$png"))"
  for part in $(grep -o 'id="part-[A-Za-z]*"' "$svg" | sed 's/id="part-\(.*\)"/\1/'); do
    stem="${png%.png}" facing=""
    [[ "$stem" =~ ^(.*)(_north|_east|_south|_west)$ ]] && stem="${BASH_REMATCH[1]}" facing="${BASH_REMATCH[2]}"
    part_png="${stem}_${part^}${facing}.png"
    inkscape --export-type=png --export-filename="$part_png" --export-id="part-$part" --export-id-only \
      --export-area-page "$svg" 2>/dev/null
    echo "export: Art/$rel#part-$part → ${part_png#$REPO/} ($(png_size "$part_png"))"
  done
done
