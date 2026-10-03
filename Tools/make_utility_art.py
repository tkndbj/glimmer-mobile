# -*- coding: utf-8 -*-
"""Cuts the utility icons from the owner's artwork, and draws the action bar's furniture.

    python Tools/make_utility_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_utility_art.py --write     # re-cut (the artwork is where --source says)
    python Tools/make_utility_art.py --contact sheet.png   # the shelf as a phone draws it

**The four icons are cut, not drawn** (since 2026-10-03): a bomb, a potion, a battery and a bolt,
supplied by the owner and cut exactly as the gravity hole's icon is (`make_gravity_fx.icon`) -
trimmed to the ink, fitted to the one square every utility icon is written at. Nothing is
recoloured or graded. The artwork lives outside the repo and the cut PNG is the committed
artifact (`make_ad_art.py`'s arrangement), so `--check` proves the cut when the artwork is on
this machine and proves the files are present and the right size when it is not.

**The addresses did not move.** `Ui/Utility/{id}` is what the bar, the loadout, the shop, the buy
panel, a chest's drop, a task's mark and the bomb on the hill all ask for (`UtilityItem.Art`), so
re-cutting a picture in place reaches every one of them and costs no Addressables work.

**Four silhouettes and never four tints.** A round thing, a flask, a cylinder and a jagged
streak: the bar is read in the corner of an eye while a hill is walking at you.

**And what `--check` proves is reproducibility, not quality.** `--contact` lays every shipped
icon on the shelf and at the buy panel's size; the honest instruction is: look at it.
"""
from __future__ import annotations

import argparse
import io
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility"

#: What the file is written at. The buy panel draws one at 250 points and the bar at about 78, so
#: 256 is the honest ceiling: anything larger is a texture nothing can use (`ArtImportRules` caps
#: the Ui folder at 1024 and would keep every wasted pixel).
SIZE = 256

#: A hair of air, so a silhouette never touches the sprite's edge. The gravity hole's own figure
#: (`make_gravity_fx.ICON_PAD`), so all five sit in a cell alike.
PAD = 0.03

#: Below this the alpha is the export's outer glow rather than the drawing (`make_ad_art`'s
#: threshold, and measured the same way: every bounding box here is stable from 8 up to 32).
TRIM_ALPHA = 8

#: Where the owner's artwork is looked for when `--source` is not given.
ARTWORK = Path.home() / "Downloads"

#: Utility id -> the artwork it is cut from. The id is the file name and the address, and is
#: content's (`progression.json`); only the picture is decided here.
ICONS = {
    "firepot": "Cartoon Bomb with Sparking Fuse",
    "mending": "Chibi Green Healing Potion",
    "surge": "Glowing Golden Energy Battery",
    "stormcall": "Neon Gold Lightning Bolt",
}

#: The shelf's cells in `UtilityItem.Order`, for the contact sheet. The fifth is cut by
#: `make_gravity_fx.py` and is drawn here from disk, because the question a sheet answers is
#: whether the five read as one set.
BAR = ["firepot", "mending", "surge", "stormcall", "gravityhole"]


# ------------------------------------------------------------------------ the tray
# The bar's furniture, in the board's own colours.
#
# **Dark, and the first cut was not.** It was drawn in the source kit's steel greys, which is what
# that kit's own screens sit on; here it sat under a near-black board of dark tiles and dark gem
# sockets, and a light panel under a dark one reads as a different screen rather than as the same
# one continued. These are `Pal.Board` and `Pal.Slate`'s family, so the shelf, the field's plate
# and the hill's ground are one column.
#
# **And no hazard rail.** The kit stripes the top of its tray in black and yellow; borrowed here it
# was the brightest thing on a screen whose whole job is telling four gem colours apart, drawing
# the eye to a decoration. What separates the shelf from the board is that the board's plate has
# rounded corners and this does not.
FRAME = (12, 20, 28, 255)           # the rim, near-black, as dark as the board's own ground
FACE = (26, 39, 52, 255)            # the shelf a cell sits on
LIP = (18, 28, 38, 255)             # its shaded underside
CELL_RIM = (46, 63, 80, 255)        # a cell's moulded edge, the one thing here that is lit
CELL_LIP = (30, 42, 55, 255)        # the shadow that rim casts into the well
WELL = (16, 26, 35, 255)            # the dark ground an item is seen against

#: The tray, in points at the canvas's reference width.
#:
#: **Authored at 1024 rather than at the canvas's 1080** so the importer's Ui cap
#: (`ArtImportRules.Caps`) never resamples it. The view stretches it the last 5%, which a flat
#: panel does not mind.
#:
#: Kept in step with `UtilityBar` by hand, exactly as `render_siege.py`'s numbers are.
TRAY_W, TRAY_H = 1024, 228

#: How many cells the shelf holds, whatever the catalog currently fills. A bar that resized
#: itself with the catalog would move every slot under a player's thumb the day a utility
#: shipped.
SLOTS = 5
CELL = 184


# --------------------------------------------------------------------------- the icons
def icon(painting):
    """The artwork trimmed to its ink and fitted to the icon square. Nothing else."""
    art = Image.open(painting).convert("RGBA")
    box = art.split()[3].point(lambda v: 255 if v >= TRIM_ALPHA else 0).getbbox()
    if box is None:
        sys.exit(f"{painting.name} is entirely transparent")
    art = art.crop(box)

    room = int(round(SIZE * (1.0 - 2.0 * PAD)))
    scale = room / max(art.width, art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))),
                     Image.LANCZOS)

    sheet = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    sheet.alpha_composite(art, ((SIZE - art.width) // 2, (SIZE - art.height) // 2))
    return sheet


# --------------------------------------------------------------------------- the furniture
def tray():
    """The bar's shelf: a square-cornered dark plate with a lit top edge.

    <p><b>Square corners, deliberately.</b> The board's plate above it is a rounded panel and this
    is not, which is the whole of what separates the two: the shelf runs to the edges of the
    screen and to the bottom of it, so a rounded corner would be a gap with nothing behind it.</p>
    """
    img = Image.new("RGBA", (TRAY_W, TRAY_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rectangle([0, 0, TRAY_W - 1, TRAY_H - 1], fill=FRAME)

    # The face, inset, with a shaded lip along its bottom so the shelf reads as a surface rather
    # than as a hole.
    d.rectangle([0, 6, TRAY_W - 1, TRAY_H - 1], fill=LIP)
    d.rectangle([0, 6, TRAY_W - 1, TRAY_H - 9], fill=FACE)

    # One lit line along the top edge - the only bright thing on it, and what says the shelf is
    # in front of the board rather than behind it.
    d.rectangle([0, 0, TRAY_W - 1, 5], fill=CELL_RIM)

    return img


def slot():
    """One cell: a moulded rim with a dark well in it, which is what an item sits in.

    <p><b>The well is darker than the shelf.</b> Drawn level with it a cell reads as a sticker on
    a panel - there is nothing for the eye to read as depth, and an icon has nothing behind it. It
    is the ground the items are seen against, so it is the darkest thing on the bar.</p>
    """
    img = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    r = 24
    d.rounded_rectangle([0, 0, CELL - 1, CELL - 1], radius=r, fill=CELL_LIP)
    d.rounded_rectangle([0, 0, CELL - 1, CELL - 7], radius=r, fill=CELL_RIM)

    d.rounded_rectangle([9, 9, CELL - 10, CELL - 14], radius=r - 7, fill=CELL_LIP)
    d.rounded_rectangle([9, 13, CELL - 10, CELL - 14], radius=r - 7, fill=WELL)

    return img


FURNITURE = {"tray": tray, "slot": slot}


# --------------------------------------------------------------------------- io
def encode(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def contact(sheet):
    """The bar as it is really drawn, and the icons alone at both sizes they are drawn at.

    <p>Drawn from the shipped files, so it is a picture of what a build carries. The assembled
    shelf says whether the five read as one set on the well; the cell-sized row says whether an
    item can be found at a glance; the panel-sized row says whether it is a picture rather than
    a smudge.</p>
    """
    from PIL import ImageFont

    art = {name: Image.open(OUT / f"{name}.png").convert("RGBA") for name in BAR}

    pad, big = 28, 250
    wide = max(TRAY_W, len(BAR) * (big + pad) + pad)
    tall = pad + TRAY_H + pad + CELL + pad + big + pad

    ground = Image.new("RGBA", (wide, tall), (16, 26, 36, 255))
    draw = ImageDraw.Draw(ground)

    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 34)
    except Exception:                                       # pragma: no cover
        font = ImageFont.load_default()

    # 1. the shelf, assembled, one cell with nothing left in it - every state a slot has.
    ground.alpha_composite(tray(), ((wide - TRAY_W) // 2, pad))
    cell = slot()
    small = int(CELL * 0.74)

    for i, name in enumerate(BAR):
        cx = (wide - TRAY_W) // 2 + TRAY_W * (2 * i + 1) // (2 * SLOTS)
        cy = pad + (TRAY_H - CELL) // 2 + CELL // 2

        ground.alpha_composite(cell, (cx - CELL // 2, cy - CELL // 2))

        face = art[name].resize((small, small), Image.LANCZOS)

        held = i != 1
        if not held:
            face.putalpha(face.split()[3].point(lambda v: int(v * 0.36)))

        ground.alpha_composite(face, (cx - small // 2, cy - small // 2))

        if held:
            bx, by = cx + CELL // 2 - 30, cy - CELL // 2 + 30
            draw.ellipse([bx - 30, by - 30, bx + 30, by + 30], fill=(12, 20, 28, 255))
            draw.text((bx, by), "3", font=font, fill=(255, 243, 220, 255), anchor="mm")

    # 2 and 3. the icons alone, at the two sizes they are drawn at
    y = pad + TRAY_H + pad
    for i, name in enumerate(BAR):
        x = pad + i * (big + pad)

        face = art[name].resize((small, small), Image.LANCZOS)
        ground.alpha_composite(face, (x + (big - small) // 2, y))
        ground.alpha_composite(art[name].resize((big, big), Image.LANCZOS),
                               (x, y + CELL + pad))

    sheet.parent.mkdir(parents=True, exist_ok=True)
    ground.convert("RGB").save(sheet, quality=94)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNGs are byte-for-byte what this writes")
    ap.add_argument("--write", action="store_true", help="write them")
    ap.add_argument("--source", type=Path, default=ARTWORK, metavar="DIR",
                    help="the folder the owner's artwork is in (default: Downloads)")
    ap.add_argument("--contact", type=Path, metavar="PNG",
                    help="lay the shipped icons out at the size a phone draws them")
    args = ap.parse_args()

    if not args.check and not args.write and not args.contact:
        ap.error("pass --check, --write or --contact")

    OUT.mkdir(parents=True, exist_ok=True)
    stale, uncut = [], []

    def settle(name, img):
        want = encode(img)
        path = OUT / f"{name}.png"

        if args.check:
            if not path.exists() or path.read_bytes() != want:
                stale.append(name)
        else:
            path.write_bytes(want)
            print(f"  wrote {path.relative_to(REPO)}  ({len(want) / 1024:.1f} kB)")

    if args.check or args.write:
        for name in sorted(FURNITURE):
            settle(name, FURNITURE[name]())

        for name, stem in sorted(ICONS.items()):
            painting = args.source / f"{stem}.png"

            if painting.exists():
                settle(name, icon(painting))
                continue

            # The artwork is not on this machine. A write has nothing to cut from; a check can
            # still prove the picture ships and is the size every surface assumes.
            if args.write:
                sys.exit(f"no artwork at {painting} (pass --source)")

            uncut.append(name)
            path = OUT / f"{name}.png"
            if not path.exists() or Image.open(path).size != (SIZE, SIZE):
                stale.append(name)

    if args.check:
        if stale:
            sys.exit("stale, re-run with --write: " + ", ".join(stale))
        print(f"  {len(FURNITURE) + len(ICONS)} utility sprites match")
        if uncut:
            print("  cut not re-proved (artwork not found, pass --source): " + ", ".join(uncut))

    if args.contact:
        contact(args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")


if __name__ == "__main__":
    main()
