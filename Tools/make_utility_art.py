# -*- coding: utf-8 -*-
"""Draws the three utility icons the action bar carries.

    python Tools/make_utility_art.py --check     # prove the shipped PNGs are what this writes
    python Tools/make_utility_art.py --write
    python Tools/make_utility_art.py --contact   # a sheet, at the size a phone draws them

**Drawn, not cut, and that is the whole argument.** Five goals in this project have now been
approximated out of a licensed pack and every one of them had to be re-done after somebody looked
at it: the Iron Quarry's cage read as firewood, Hollowmarch's road disappeared into the ground,
Emberforge's cage was a tiny lock over a mess, Kindlewake's husk and Prismvale's lantern read as a
crosshair (invariants 32b, 33h, 34e, 36g). A utility icon is the same class of object as those —
something the player has to find at a glance, at 78 points, on a dark slot, among two others — so
its size, its contrast and its silhouette are decisions rather than accidents. There is no pack
dependency here at all, which also means this gate runs on every checkout rather than passing
silently when the zips are absent.

**Three silhouettes and never three tints.** A circle with a fuse, a flask, a bolt: the whole
point of the bar is telling three things apart in the corner of an eye while a hill is walking at
you, so a player who cannot separate warm from cool has to be able to separate a round thing from
a pointed one. Emberforge's rule about its four shards (invariant 34f), asked of a HUD.

**And what `--check` proves is reproducibility, not quality.** `make_shop_art.py` shipped four
broken cards under a green check, which is why `--contact` exists and why the honest instruction
is: look at it.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path

try:
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow and numpy:  python -m pip install pillow numpy")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Utility"

#: What the file is written at. The buy panel draws one at 250 points and the bar at about 78, so
#: 256 is the honest ceiling: anything larger is a texture nothing can use (`ArtImportRules` caps
#: the Ui folder at 1024 and would keep every wasted pixel).
SIZE = 256

#: Everything is drawn at this multiple and reduced once at the end. Four is what turns a polygon
#: edge from a staircase into a line at this size; eight costs four times the memory and is not
#: distinguishable in a 256-pixel PNG.
SS = 4

#: A hair of air, so a silhouette never touches the sprite's edge and `preserveAspect` has
#: something to breathe in.
PAD = 0.055


# --------------------------------------------------------------------------- palette
# The bar's slot is `sq_dark`, a near-black navy, so every one of these is a bright saturated
# body inside a *darker* outline: an outline lighter than its ground reads as a halo and loses
# the shape. The colours are `Pal`'s own, written out because a Python tool cannot read C#, and
# they are the one thing here that has to be kept in step by hand.
INK = (24, 34, 46, 255)          # Pal.Ink, the outline every icon carries
CREAM = (255, 243, 220, 255)     # Pal.Cream
SUN = (255, 201, 60, 255)        # Pal.Sun
EMBER = (255, 107, 87, 255)      # Pal.Ember
MINT = (123, 216, 106, 255)      # Pal.Mint
AZURE = (79, 193, 255, 255)      # Pal.Azure
IRON = (108, 130, 156, 255)       # the pot's body: cool, so the flame reads warm against it
ROPE = (217, 195, 154, 255)      # Pal.Rope, the fuse

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

#: How many cells the shelf holds, whatever the catalog currently fills.
#:
#: **Five, and three of them have something in them today.** A bar that resized itself with the
#: catalog would move every slot under a player's thumb the day a fourth utility shipped, and a
#: row of three on a full-width shelf reads as a tray built for more than it holds - which it is.
#: Drawing the empty ones says how many there will be.
SLOTS = 5
CELL = 184


def lift(colour, amount):
    """Toward white, for a highlight. The one place a colour is derived rather than named."""
    r, g, b, a = colour
    return (int(r + (255 - r) * amount), int(g + (255 - g) * amount),
            int(b + (255 - b) * amount), a)


def deep(colour, amount):
    """Toward the ink, for a shaded underside."""
    r, g, b, a = colour
    return (int(r * (1 - amount)), int(g * (1 - amount)), int(b * (1 - amount)), a)


# --------------------------------------------------------------------------- drawing
class Pen:
    """A canvas in unit coordinates, so every shape below reads as a proportion.

    Nothing here is written in pixels. That is not tidiness: `SIZE` is a decision about texture
    memory and the shapes are decisions about legibility, and a tool that mixed the two would have
    to be re-tuned every time the first one moved.
    """

    def __init__(self):
        self.n = SIZE * SS
        self.img = Image.new("RGBA", (self.n, self.n), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def px(self, u):
        return u * self.n

    def at(self, x, y):
        """Unit space is 0..1 with y down, inset by `PAD` on every side."""
        span = 1.0 - PAD * 2
        return (self.px(PAD + x * span), self.px(PAD + y * span))

    def ellipse(self, cx, cy, rx, ry, fill, outline=None, width=0.0):
        x, y = self.at(cx, cy)
        rx, ry = self.px(rx), self.px(ry)
        self.d.ellipse([x - rx, y - ry, x + rx, y + ry], fill=fill,
                       outline=outline, width=int(self.px(width)) or None)

    def poly(self, points, fill, outline=None, width=0.0):
        pts = [self.at(x, y) for x, y in points]
        self.d.polygon(pts, fill=fill, outline=outline, width=int(self.px(width)) or None)

    def line(self, points, fill, width):
        pts = [self.at(x, y) for x, y in points]
        self.d.line(pts, fill=fill, width=int(self.px(width)), joint="curve")

    def round_rect(self, x0, y0, x1, y1, r, fill, outline=None, width=0.0):
        a, b = self.at(x0, y0)
        c, e = self.at(x1, y1)
        self.d.rounded_rectangle([a, b, c, e], radius=self.px(r), fill=fill,
                                 outline=outline, width=int(self.px(width)) or None)

    def finish(self):
        """Reduces to `SIZE`, which is where every edge above becomes smooth."""
        return self.img.resize((SIZE, SIZE), Image.LANCZOS)


def outlined(draw_body, thickness=0.030):
    """Draws a shape twice: a fattened ink silhouette, then the body over it.

    <p>A stroked outline would follow each shape separately and leave seams where two of them
    meet — a fuse crossing a pot, a cork sitting on a flask. Dilating the whole silhouette once
    and painting the body back over it gives one continuous rim round the *icon*, which is what
    the eye reads as the edge of an object.</p>
    """
    body = Pen()
    draw_body(body)

    alpha = body.img.split()[3]

    # A blur followed by a hard threshold is a dilation with a round kernel, and it is what makes
    # the rim the same weight on a straight edge and on a corner. Doing it with MaxFilter would
    # be square, which shows as flat corners at this size.
    grown = alpha.filter(ImageFilter.GaussianBlur(SIZE * SS * thickness * 0.5))
    grown = grown.point(lambda v: 255 if v > 26 else 0)

    rim = Image.new("RGBA", body.img.size, INK)
    rim.putalpha(grown)

    out = Image.alpha_composite(rim, body.img)
    return out.resize((SIZE, SIZE), Image.LANCZOS)


# --------------------------------------------------------------------------- the three
# Every one of these was drawn, rendered by `--contact` and then re-drawn, which is the point of
# having a contact sheet at all. What the first cut got wrong, in case a fourth utility repeats it:
# a flame built out of diamonds reads as a kite on a stick; a body assembled from a polygon and an
# ellipse that nearly agree leaves a notch at the seam, so a silhouette is one shape or a stack of
# shapes that plainly overlap; and a "lit quarter" laid over a ring reads as a torn flag rather
# than as light. None of that is visible in the source.
def flame(p, cx, cy, r, colour):
    """A teardrop: a ball with a point on top. Two shapes that plainly overlap, never a diamond."""
    p.ellipse(cx, cy, r, r * 0.92, colour)
    p.poly([(cx, cy - r * 2.30), (cx + r * 0.94, cy + r * 0.10),
            (cx - r * 0.94, cy + r * 0.10)], colour)


def firepot(p):
    """A round iron pot with a lit fuse: this mode's own bomb.

    Round, because it is the one of the three the player throws *at the hill* and a circle is what
    the aiming ring will be. The flame is drawn in the mode's own ember over a cool body, so the
    eye finds the lit end before it has read the shape.
    """
    cx, cy, r = 0.470, 0.640, 0.330

    p.ellipse(cx, cy, r, r, IRON)

    # One highlight and no shading. A dark ellipse low on the ball was tried and reads as a mouth
    # at 78 points — a ball this size has room for the light or the shadow, not both.
    p.ellipse(cx - 0.100, cy - 0.105, 0.115, 0.092, lift(IRON, 0.34))
    p.ellipse(cx - 0.128, cy - 0.132, 0.048, 0.037, lift(IRON, 0.66))

    # The neck, and the fuse curling clear of the ball so the flame is never read as being on it.
    p.round_rect(cx - 0.072, 0.278, cx + 0.072, 0.372, 0.028, deep(IRON, 0.22))
    p.line([(cx, 0.310), (cx + 0.075, 0.222), (cx + 0.170, 0.196), (cx + 0.222, 0.140)],
           ROPE, 0.050)

    flame(p, cx + 0.240, 0.106, 0.088, EMBER)
    flame(p, cx + 0.240, 0.116, 0.055, SUN)
    flame(p, cx + 0.240, 0.124, 0.026, CREAM)


def mending(p):
    """A stoppered flask with a cross on it: the one that heals.

    <p><b>A flask and never a heart.</b> A heart already means something exact in this game — the
    gate that decides whether a run may begin at all — so an icon that borrowed it would promise
    the wrong resource on the one bar where a wrong tap costs a consumable.</p>

    <p>Built out of three rounded rectangles that plainly overlap rather than out of a polygon
    meeting an ellipse. The first cut did the second and left a notch where the two nearly agreed,
    which read as a torn corner at slot size and as a sock at panel size.</p>
    """
    # The body: wide and low, so it fills the slot rather than sitting in the middle of it.
    p.round_rect(0.255, 0.430, 0.745, 0.900, 0.185, MINT)

    # The neck, overlapping the body by enough that the join is never a seam.
    p.round_rect(0.395, 0.235, 0.605, 0.500, 0.055, MINT)

    # The lit edge, one stroke down the left, where every other sprite in this game is lit.
    p.round_rect(0.318, 0.505, 0.392, 0.815, 0.037, lift(MINT, 0.42))

    # The cork.
    p.round_rect(0.360, 0.120, 0.640, 0.262, 0.052, ROPE)
    p.round_rect(0.392, 0.140, 0.470, 0.244, 0.032, lift(ROPE, 0.34))

    # The cross: thick, cream, and large enough to survive being drawn at 78 points. Cream rather
    # than white so it sits in the same family as every other glyph in this UI.
    p.round_rect(0.443, 0.545, 0.557, 0.800, 0.030, CREAM)
    p.round_rect(0.363, 0.615, 0.637, 0.730, 0.030, CREAM)


def surge(p):
    """A bolt in a ring: the one that fuels a ward.

    <p>The ring is what says "a ward" rather than "the sky": a bare bolt is weather, and this mode
    already draws one of those every time a blue ward fires. Pointed, so its silhouette is the
    opposite of the firepot's circle at any size.</p>

    <p>No highlight on the ring. One was drawn as a lit quarter over the top of it and read as a
    torn flag stuck to the side — a ring is a stroke, and a stroke has no lit face.</p>
    """
    p.ellipse(0.500, 0.520, 0.402, 0.402, AZURE)
    p.ellipse(0.500, 0.520, 0.310, 0.310, (0, 0, 0, 0))

    # The bolt, drawn over the ring so the two read as one object rather than as a badge.
    p.poly([(0.575, 0.145), (0.335, 0.545), (0.480, 0.545), (0.425, 0.885),
            (0.672, 0.470), (0.525, 0.470), (0.612, 0.145)], SUN)

    # Its lit edge, strictly inside the upper limb.
    p.poly([(0.552, 0.215), (0.408, 0.520), (0.455, 0.520), (0.582, 0.240)], CREAM)


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


ICONS = {
    "firepot": firepot,
    "mending": mending,
    "surge": surge,
}


# --------------------------------------------------------------------------- io
def render(name):
    if name in FURNITURE:
        return FURNITURE[name]()

    return outlined(ICONS[name])


def everything():
    """Every file this writes, icons and furniture alike."""
    return sorted(ICONS) + sorted(FURNITURE)


def encode(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def contact(sheet):
    """The bar as it is really drawn, and the icons alone at both sizes they are drawn at.

    <p>Three pictures, because they answer three different questions. The assembled shelf says
    whether the thing reads as furniture; the cell-sized row says whether an item can be found at
    a glance; the panel-sized row says whether it is a picture rather than a smudge. The first of
    those is the one that matters and the one a per-file check can never see.</p>
    """
    from PIL import ImageFont

    pad, big = 28, 250
    wide = max(TRAY_W, len(ICONS) * (big + pad) + pad)
    tall = pad + TRAY_H + pad + CELL + pad + big + pad

    ground = Image.new("RGBA", (wide, tall), (16, 26, 36, 255))
    draw = ImageDraw.Draw(ground)

    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 34)
    except Exception:                                       # pragma: no cover
        font = ImageFont.load_default()

    # 1. the shelf, assembled: five cells, three filled, one of those with nothing left in it -
    #    which is every state a slot has.
    ground.alpha_composite(tray(), ((wide - TRAY_W) // 2, pad))
    cell = slot()
    icon = int(CELL * 0.74)

    for i in range(SLOTS):
        cx = (wide - TRAY_W) // 2 + TRAY_W * (2 * i + 1) // (2 * SLOTS)
        cy = pad + (TRAY_H - CELL) // 2 + CELL // 2

        ground.alpha_composite(cell, (cx - CELL // 2, cy - CELL // 2))

        if i >= len(ICONS):
            continue

        name = sorted(ICONS)[i]
        art = render(name).resize((icon, icon), Image.LANCZOS)

        held = i != 1
        if not held:
            art.putalpha(art.split()[3].point(lambda v: int(v * 0.36)))

        ground.alpha_composite(art, (cx - icon // 2, cy - icon // 2))

        if held:
            bx, by = cx + CELL // 2 - 30, cy - CELL // 2 + 30
            draw.ellipse([bx - 30, by - 30, bx + 30, by + 30], fill=(12, 20, 28, 255))
            draw.text((bx, by), "3", font=font, fill=(255, 243, 220, 255), anchor="mm")

    # 2 and 3. the icons alone, at the two sizes they are drawn at
    y = pad + TRAY_H + pad
    for i, name in enumerate(sorted(ICONS)):
        art = render(name)
        x = pad + i * (big + pad)

        small = art.resize((int(CELL * 0.74), int(CELL * 0.74)), Image.LANCZOS)
        ground.alpha_composite(small, (x + (big - small.width) // 2, y))
        ground.alpha_composite(art.resize((big, big), Image.LANCZOS), (x, y + CELL + pad))

    sheet.parent.mkdir(parents=True, exist_ok=True)
    ground.convert("RGB").save(sheet, quality=94)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true",
                    help="prove the shipped PNGs are byte-for-byte what this writes")
    ap.add_argument("--write", action="store_true", help="write them")
    ap.add_argument("--contact", type=Path, metavar="PNG",
                    help="lay them out at the size a phone draws them, and look at it")
    args = ap.parse_args()

    if args.contact:
        contact(args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if not args.check and not args.write and not args.contact:
        ap.error("pass --check, --write or --contact")

    if not args.check and not args.write:
        return

    OUT.mkdir(parents=True, exist_ok=True)
    stale = []

    for name in everything():
        want = encode(render(name))
        path = OUT / f"{name}.png"

        if args.check:
            if not path.exists() or path.read_bytes() != want:
                stale.append(name)
            continue

        path.write_bytes(want)
        print(f"  wrote {path.relative_to(REPO)}  ({len(want) / 1024:.1f} kB)")

    if args.check:
        if stale:
            sys.exit("stale, re-run with --write: " + ", ".join(stale))
        print(f"  {len(everything())} utility sprites match")


if __name__ == "__main__":
    main()
