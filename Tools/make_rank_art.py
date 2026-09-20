#!/usr/bin/env python3
"""Cut the rank ladder's seven badges from the owner's supplied artwork.

Seven sprites, `Assets/Game/Art/Ui/Rank/{id}.png`, one per rung of `RankLadder` — the badge a
keeper wears on the map and the seven a player reads down the ranks page.

**The address is derived from the rung's id and never authored** (`RankDefinition.Icon`), which
is `ChestTier.Icon`'s rule and for its reason: anything holding a rank can draw it without
reading the ladder. The consequence is that `artnames.py` cannot see a single one of these — a
built address is invisible to a call-site scan — so the gate that holds them to disk is
`content.py`'s `check_ranks`, which walks the authored ladder. Rename a rung and the picture has
to move with it.

**One scale for all seven, taken across the whole set.** The badges arrive as separate files
with their own canvases, and cut to their own bounding boxes they would land on the page at
seven different sizes — a ladder whose third rung is visibly larger than its fourth reads as a
mistake rather than as a rank. So the bounding boxes are measured first, one factor is derived
from the widest and the tallest of them, and every badge is scaled by that one number and
centred in a shared frame. The artist's relative sizing survives exactly; nothing is normalised
away.

**Cut at 256 against a folder rule of 256** (`ArtImportRules.Caps`). The badge is drawn at 96 on
the map and at 132 on the page, so 256 is already a comfortable upscale margin and the rule is
what stops a re-cut at source size quietly shipping seven megabyte textures (invariant 7d).

**It passes with no source folder on disk**, which is `make_boost_icon.py`'s bargain: the PNGs
are committed, so a fresh clone runs every gate without the owner's `Downloads` folder.

    python Tools/make_rank_art.py            # cut them
    python Tools/make_rank_art.py --check    # prove the shipped PNGs are what this writes
    python Tools/make_rank_art.py --contact  # the sheet, on the ground the page draws them on

**The `.meta` carries a derived guid**, for `make_boost_icon.py`'s reason: Addressables keys
every entry on the guid, so a re-cut minting a fresh one would orphan the address and ship a
white rectangle (invariant 7b).
"""

from __future__ import annotations

import argparse
import hashlib
import io
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets" / "Game" / "Art" / "Ui" / "Rank"

#: The rungs, humblest first, paired with the source file each was drawn as. **The order is the
#: ladder's and the ids are `progression.json`'s** — `check_ranks` holds the two together, so a
#: rung added to content with no picture here is an error rather than a white rectangle.
RUNGS = [
    ("cinderling", "rank1.png"),
    ("silverwatch", "rank2.png"),
    ("goldbrand", "rank3.png"),
    ("duskcrown", "rank4.png"),
    ("frostheart", "rank5.png"),
    ("auroracrest", "rank6.png"),
    ("gemfire", "rank7.png"),
]

#: Both roots the art tools read, in order — `make_boost_icon.SOURCES`, plus the folder the
#: owner delivered this set in.
SOURCES = [Path(r"C:\Users\Digikey\Downloads\ranks"),
           Path(r"C:\Users\Digikey\Downloads\2D ASSETS\ranks")]

SIZE = 256

#: Air inside the frame, in pixels a side. A badge that touched the edge would have its
#: outermost spike clipped by the sprite's own border the moment anything drew it with a
#: rounded mask, and rank seven is nothing but outermost spikes.
MARGIN = 6

TEMPLATE = ROOT / "Assets" / "Game" / "Art" / "Ui" / "ic_update.png.meta"


def source():
    """The folder the badges were delivered in, or None when this machine never had it."""
    for folder in SOURCES:
        if folder.is_dir() and all((folder / name).exists() for _, name in RUNGS):
            return folder
    return None


def cut(folder):
    """Every badge, trimmed, scaled by one shared factor and centred in a shared frame."""
    from PIL import Image

    crops = []
    for _, name in RUNGS:
        image = Image.open(folder / name).convert("RGBA")
        box = image.getbbox()
        if box is None:
            sys.exit(f"{name} is empty")
        crops.append(image.crop(box))

    # One factor, taken across the set rather than per badge. See the module docstring.
    room = SIZE - MARGIN * 2
    widest = max(c.width for c in crops)
    tallest = max(c.height for c in crops)
    scale = min(room / widest, room / tallest)

    frames = []
    for crop in crops:
        w = max(1, int(round(crop.width * scale)))
        h = max(1, int(round(crop.height * scale)))
        small = crop.resize((w, h), Image.LANCZOS)

        frame = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        frame.alpha_composite(small, ((SIZE - w) // 2, (SIZE - h) // 2))
        frames.append(frame)

    return frames


def png_bytes(image):
    """Deterministic PNG bytes, so `--check` compares a cut against a cut."""
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def meta(rid):
    """This project's own importer settings, with a guid derived from the rung's id."""
    text = TEMPLATE.read_text(encoding="utf8")
    guid = hashlib.md5(("glimmer.ui.rank." + rid).encode("utf8")).hexdigest()

    out = []
    for line in text.splitlines(keepends=True):
        if line.startswith("guid: "):
            out.append(f"guid: {guid}\n")
        elif line.strip().startswith("spriteBorder:"):
            out.append("  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n")
        else:
            out.append(line)
    return "".join(out)


def contact(frames, path):
    """The sheet: all seven in a row on the page's own plate colour, named underneath.

    The one question `--check` cannot answer is whether the ladder *reads* as a ladder — seven
    badges that each look fine alone and do not climb together is the fault this sheet exists
    to catch (invariant 44b's "measured, not typed", asked of a set rather than of a lift).
    """
    from PIL import Image, ImageDraw

    cell, pad = SIZE, 18
    sheet = Image.new("RGBA", (cell * len(frames) + pad * 2, cell + 58), (26, 30, 44, 255))
    draw = ImageDraw.Draw(sheet)

    for i, frame in enumerate(frames):
        sheet.alpha_composite(frame, (pad + i * cell, 12))
        label = RUNGS[i][0]
        draw.text((pad + i * cell + cell // 2 - len(label) * 3, cell + 22), label,
                  fill=(232, 236, 248, 255))

    sheet.save(path)
    print(f"wrote {path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="prove the shipped PNGs are what this tool cuts")
    parser.add_argument("--contact", action="store_true", help="write the contact sheet")
    args = parser.parse_args()

    folder = source()

    if folder is None:
        missing = [rid for rid, _ in RUNGS if not (OUT / f"{rid}.png").exists()]
        if missing:
            sys.exit("no source folder on this machine and no committed PNG for: "
                     + ", ".join(missing))

        print(f"ranks: no source folder on this machine; the {len(RUNGS)} committed PNGs stand")
        return 0

    frames = cut(folder)

    if args.contact:
        contact(frames, ROOT / "rank_badges.png")
        return 0

    if args.check:
        bad = []
        for (rid, _), frame in zip(RUNGS, frames):
            png = OUT / f"{rid}.png"
            if not png.exists():
                bad.append(f"{rid}.png is missing")
            elif png.read_bytes() != png_bytes(frame):
                bad.append(f"{rid}.png is not what this tool cuts")

        if bad:
            for line in bad:
                print(line, file=sys.stderr)
            print("re-run without --check", file=sys.stderr)
            return 1

        print(f"ranks: {len(RUNGS)} badge(s) at {SIZE}x{SIZE}, reproducible")
        return 0

    OUT.mkdir(parents=True, exist_ok=True)
    for (rid, _), frame in zip(RUNGS, frames):
        (OUT / f"{rid}.png").write_bytes(png_bytes(frame))
        (OUT / f"{rid}.png.meta").write_text(meta(rid), encoding="utf8", newline="\n")

    print(f"ranks: wrote {len(RUNGS)} badge(s) to {OUT} at {SIZE}x{SIZE}, each with its .meta")
    return 0


if __name__ == "__main__":
    sys.exit(main())
