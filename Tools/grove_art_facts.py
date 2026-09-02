#!/usr/bin/env python3
"""
Write down what each grove piece's picture *is*: its size, and where inside its rectangle
the paint actually lands. For the pieces in homestead.json and for the companions in
manifest.json, because a resident is a companion and the grove draws its art too.

    python Tools/grove_art_facts.py            # rewrite w / h / hit for every piece and companion
    python Tools/grove_art_facts.py --check    # prove the shipped values are what it would write

## Why these are content

Two facts about a sprite used to be read off the loaded texture at runtime, and both went
wrong in ways nothing offline could see.

  * **Size.** A tile bound while its art was still loading was laid out around a 140px
    placeholder square; if the real picture then landed without a rebind, it drew *inside*
    that square, a third of its size. Authoring `w`/`h` makes the box exact before, during
    and after the load.

  * **Shape.** A tap was tested against the sprite's box, and an oak's box is nine tiles
    of air around a forty-pixel trunk, so the grass beside it could not be tapped at all.
    `hit` is a grid of bits over the rectangle saying where the paint is, and
    `GroveHitMask` tests a touch against it.

A number describing a picture is only true until somebody re-cuts the picture
(`GroveFloor.TileFaceRatio`'s lesson), so this tool has a `--check` that
`Tools/verify/content.py` runs, and `import_grove_art.py` calls the same function on every
import.

## The mask, exactly

The grid is `ceil(w / CELL)` columns by `ceil(h / CELL)` rows, one cell every `CELL` art
pixels, so a cell is the same square on a ladder as on a house and a tap's tolerance can be
a distance rather than a count of cells. The bits run row-major from the **top** row, each
row left to right, written as hexadecimal with the first bit of each character its most
significant and the last character padded with zeros. A cell is set when at least
`CELL_FILL` of its pixels are more opaque than `ALPHA` — chosen so a thin post sets its
column and a wisp of leaf does not.

`GroveHitMask.TryParse` in C# is the other half of this contract and refuses any string
that is not exactly the length its picture implies.
"""

from __future__ import annotations

import argparse
import collections
import io
import json
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "Assets", "Game", "Art")
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CATALOG = os.path.join(CONTENT, "homestead.json")
MANIFEST = os.path.join(CONTENT, "manifest.json")

CELL = 16         # GroveHitMask.CellPx
ALPHA = 0.4       # a pixel counts as paint above this alpha
CELL_FILL = 0.06  # a cell is set when this fraction of its pixels are paint

# Where a companion's grove art lives - GroveResidents.CritterFolder / PortraitFolder.
CRITTERS = "Critters/"
PORTRAITS = "Companions/"


def side(pixels: int) -> int:
    """Cells along one side of a picture this many pixels long - GroveHitMask.SideFor."""
    return 0 if pixels <= 0 else (pixels + CELL - 1) // CELL


def hex_length(w: int, h: int) -> int:
    """The length of a mask for a picture this size - GroveHitMask.HexLengthFor."""
    return (side(w) * side(h) + 3) // 4


def art_path(art: str, animated: bool) -> str | None:
    """The PNG for an art key, or the first frame of an animated folder."""
    if animated:
        folder = os.path.join(ART, art)
        if not os.path.isdir(folder):
            return None
        frames = sorted(f for f in os.listdir(folder) if f.lower().endswith(".png"))
        return os.path.join(folder, frames[0]) if frames else None

    path = os.path.join(ART, art + ".png")
    return path if os.path.isfile(path) else None


def mask_of(img: Image.Image) -> str:
    """The hit mask for a picture. See the module docstring for the encoding."""
    w, h = img.size
    cols, rows = side(w), side(h)
    alpha = img.convert("RGBA").getchannel("A")
    px = alpha.load()
    threshold = int(ALPHA * 255)

    bits = []
    for cy in range(rows):
        y0, y1 = cy * CELL, min(h, (cy + 1) * CELL)
        for cx in range(cols):
            x0, x1 = cx * CELL, min(w, (cx + 1) * CELL)

            total = (x1 - x0) * (y1 - y0)
            paint = 0
            for y in range(y0, y1):
                for x in range(x0, x1):
                    if px[x, y] > threshold:
                        paint += 1

            bits.append(1 if paint >= max(1, CELL_FILL * total) else 0)

    while len(bits) % 4:
        bits.append(0)

    out = []
    for k in range(0, len(bits), 4):
        nibble = (bits[k] << 3) | (bits[k + 1] << 2) | (bits[k + 2] << 1) | bits[k + 3]
        out.append("0123456789abcdef"[nibble])

    return "".join(out)


def facts_for(art: str, animated: bool):
    """(w, h, hit) for an art key, or None when there is no picture to read."""
    path = art_path(art, animated)
    if path is None:
        return None

    img = Image.open(path)
    w, h = img.size
    return w, h, mask_of(img)


def piece_art(piece: dict) -> tuple[str, bool]:
    return piece.get("art") or "Homestead/" + piece.get("id", ""), bool(piece.get("animated"))


def companion_art(companion: dict) -> tuple[str, bool]:
    """The art the grove draws for a companion - GroveResidents.From's choice."""
    animated = companion.get("animated") or ""
    if animated:
        return CRITTERS + animated, True
    return PORTRAITS + (companion.get("portrait") or companion.get("id", "")), False


def apply(row: dict, art: str, animated: bool, fields=("w", "h", "hit")) -> tuple[bool, str | None]:
    """Write the facts into one catalog row under the given field names. Returns (changed, problem)."""
    facts = facts_for(art, animated)
    if facts is None:
        return False, f"{row.get('id')}: no art at Art/{art}"

    changed = any(row.get(name) != value for name, value in zip(fields, facts))
    for name, value in zip(fields, facts):
        row[name] = value
    return changed, None


COMPANION_FIELDS = ("groveW", "groveH", "groveHit")


def run(check: bool) -> int:
    catalog = json.load(io.open(CATALOG, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)
    manifest = json.load(io.open(MANIFEST, encoding="utf-8"), object_pairs_hook=collections.OrderedDict)

    drift, problems = [], []

    for piece in catalog["pieces"]:
        art, animated = piece_art(piece)
        changed, problem = apply(piece, art, animated)
        if problem:
            problems.append(problem)
        elif changed:
            drift.append("piece %s" % piece["id"])

    for companion in manifest.get("companions") or []:
        if companion.get("disabled"):
            continue
        art, animated = companion_art(companion)
        changed, problem = apply(companion, art, animated, COMPANION_FIELDS)
        if problem:
            problems.append(problem)
        elif changed:
            drift.append("companion %s" % companion["id"])

    for p in problems:
        print("MISSING  " + p)

    if check:
        for d in drift:
            print("DRIFT    " + d)
        if drift or problems:
            print("%d entries out of step with their art; run Tools/grove_art_facts.py"
                  % (len(drift) + len(problems)))
            return 1
        print("grove art facts: %d piece(s) and %d companion(s) agree with their art"
              % (len(catalog["pieces"]), len(manifest.get("companions") or [])))
        return 0

    io.open(CATALOG, "w", encoding="utf-8", newline="\n").write(
        json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")
    io.open(MANIFEST, "w", encoding="utf-8", newline="\n").write(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
    print("grove art facts: %d rewritten" % len(drift))
    return 1 if problems else 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true",
                    help="report drift between the content and the art; write nothing")
    return run(ap.parse_args().check)


if __name__ == "__main__":
    sys.exit(main())
