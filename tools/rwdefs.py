#!/usr/bin/env python3
"""XML Def helpers (stdlib only; run with `python3 -I`).

  rwdefs.py def  <rw_dir> <defName|Name>   print a vanilla def and its ParentName chain
  rwdefs.py lint <rw_dir> <repo>           check our Defs/textures before spending a game restart
"""
import re
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Elements whose <li> children (or child tag names, for costList) must name an existing def.
REF_LISTS = {"thingDefs", "recipes", "researchPrerequisites", "prerequisites", "recipeUsers"}
REF_TAG_LISTS = {"costList", "products"}
REF_VALUES = {"researchPrerequisite", "thingDef", "hediff", "designationCategory", "thingCategory"}
TEX_TAGS = {"texPath", "iconPath", "uiIconPath"}
OUR_TEX_PREFIX = "Quadrunner/"  # vanilla textures live in asset bundles and can't be checked on disk


def def_files(root):
    return sorted(Path(root).rglob("*.xml"))


def index_names(files):
    """defName and abstract Name values across files (regex: fast and tolerant of odd vanilla XML)."""
    names = set()
    for f in files:
        text = f.read_text(encoding="utf-8", errors="replace")
        names.update(re.findall(r"<defName>\s*([^<\s]+)\s*</defName>", text))
        names.update(re.findall(r'\bName="([^"]+)"', text))
    return names


def cmd_def(rw_dir, wanted):
    core = def_files(Path(rw_dir) / "Data/Core/Defs")
    by_key = {}
    for f in core:
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError:
            continue
        for el in root:
            for key in (el.findtext("defName"), el.get("Name")):
                if key:
                    by_key.setdefault(key.strip(), (f, el))
    key = wanted
    if key not in by_key:
        sys.exit(f"not found in Core Defs: {wanted}")
    while key:
        f, el = by_key[key]
        print(f"<!-- {f.relative_to(rw_dir)} -->")
        ET.indent(el)
        print(ET.tostring(el, encoding="unicode"))
        key = el.get("ParentName")
        if key and key not in by_key:
            print(f"<!-- parent {key} not in Core -->")
            break


def png_size(path):
    with open(path, "rb") as fh:
        head = fh.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def texture_exists(tex_root, path):
    p = tex_root / path
    return (p.with_suffix(".png").is_file()
            or (tex_root / f"{path}_north.png").is_file()
            or (p.is_dir() and any(p.glob("*.png"))))


def cmd_lint(rw_dir, repo):
    repo = Path(repo)
    errors = []
    ours = def_files(repo / "Defs")
    known = index_names(def_files(Path(rw_dir) / "Data/Core/Defs")) | index_names(ours)
    tex_root = repo / "Textures"

    for f in [*ours, *def_files(repo / "About")]:
        rel = f.relative_to(repo)
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError as e:
            errors.append(f"{rel}: XML parse error: {e}")
            continue
        for el in root.iter():
            parent = el.get("ParentName")
            if parent and parent not in known:
                errors.append(f"{rel}: ParentName '{parent}' is not a known def/Name")
            if el.tag in REF_LISTS:
                refs = [li.text.strip() for li in el.findall("li") if li.text and li.text.strip()]
            elif el.tag in REF_TAG_LISTS:
                refs = [child.tag for child in el]
            elif el.tag in REF_VALUES and el.text and el.text.strip() and len(el) == 0:
                refs = [el.text.strip()]
            else:
                refs = []
            errors += [f"{rel}: <{el.tag}> references unknown def '{r}'" for r in refs if r not in known]
            if el.tag in TEX_TAGS and el.text and el.text.strip().startswith(OUR_TEX_PREFIX):
                if not texture_exists(tex_root, el.text.strip()):
                    errors.append(f"{rel}: <{el.tag}> '{el.text.strip()}' has no PNG under Textures/ (case-sensitive)")

    for cs in (repo / "Source").rglob("*.cs"):
        for path in re.findall(r'(?:ContentFinder<Texture2D>\.Get|MaterialPool\.MatFrom)\("([^"]+)"', cs.read_text(encoding="utf-8")):
            if path.startswith(OUR_TEX_PREFIX) and not texture_exists(tex_root, path):
                errors.append(f"{cs.relative_to(repo)}: texture '{path}' has no PNG under Textures/ (case-sensitive)")

    for png in [*tex_root.rglob("*.png"), *(repo / "About").glob("*.png")]:
        size = png_size(png)
        rel = png.relative_to(repo)
        if size is None:
            errors.append(f"{rel}: not a PNG")
        elif size[0] % 4 or size[1] % 4:
            errors.append(f"{rel}: {size[0]}x{size[1]} is not a multiple of 4")
    preview = repo / "About/Preview.png"
    if preview.is_file() and preview.stat().st_size >= 1024 * 1024:
        errors.append("About/Preview.png: must be under 1 MB for Steam")

    for e in errors:
        print(f"lint: {e}")
    print(f"lint: {len(ours)} def file(s), {len(errors)} problem(s)")
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    if len(sys.argv) != 4 or sys.argv[1] not in ("def", "lint"):
        sys.exit(__doc__)
    {"def": cmd_def, "lint": cmd_lint}[sys.argv[1]](sys.argv[2], sys.argv[3])
