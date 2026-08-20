# -*- coding: utf-8 -*-
"""Builds the grove's animated waterfall from the ruin pack, and points the catalog at it.

    python Tools/make_waterfall.py --source "C:/path/to/village-assets/_extracted" [--dry-run]

**Why this is a tool and not a row in grove_art.tsv.** That importer copies one source PNG
per piece, which is the right shape for the hundred and sixty pieces that are a picture
somebody drew. This piece is a *composition*: a cliff from the pack, water this file draws,
and eight frames of it. Keeping it re-runnable matters for the reason the other pipelines
are — the mapping and the numbers are reviewed in a diff rather than recovered from
somebody's memory of an afternoon in an image editor.

**What was wrong with the old one.** `waterfall` was `Elements/04.png` cut on its own: two
translucent vertical stripes with no rock behind them and nothing at the bottom, because in
the source pack that file is an *overlay* meant to be draped down the face of a stacked
platform. Alone on a grove tile it read as a pale smear. There is no waterfall and no cliff
anywhere in the seventeen packs, and no animated water in any of them, so the piece has to
be composed and the animation has to be generated.

**The three things the composition measures rather than assumes.** Where the grass slab
ends, where the pond's near edge is, and where the rock's silhouette stops — all read out of
the cliff sprite per column, so the water pours over the actual sheared edge and lands on the
actual rock instead of on hand-typed coordinates. That is `UIKit.PillFaceLift`'s lesson for
the fifth time: where a painted shape's features sit inside its rectangle is a fact about the
image, and centring instead of measuring is a mistake this project has already made.

**Why eight frames.** `HomesteadArt.Paint` runs a flipbook at 12fps, so eight frames is a
two-thirds-of-a-second loop — fast enough that falling water reads as continuous, and a third
of the texture memory of a twelve-frame one. Every cycle in here is periodic over the loop
(integer speeds only): a streak that travelled 1.3 sheet-lengths per loop would jump on the
wrap, which is `TweenCycle`'s bug in a different clock.

**Why the frames are 384 wide and not 418.** The field's zoom tops out at 1.0
(`GroveFieldView.MaxZoom`), so the piece never draws wider than about 265 canvas units — a
little over 300 device pixels on a 3x phone. Authoring at the source's full width would ship
half again as many pixels as anything can show. `SCALE` below is derived from that width, so
changing one changes the other.
"""
import argparse, collections, io, json, math, os, shutil, sys

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("this needs Pillow: python -m pip install pillow")

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ART = os.path.join(ROOT, "Assets", "Game", "Art", "Homestead", "Anim", "waterfall")
CONTENT = os.path.join(ROOT, "Assets", "StreamingAssets", "Content")
CATALOG = os.path.join(CONTENT, "homestead.json")
MANIFEST = os.path.join(CONTENT, "manifest.json")

PIECE = "waterfall"
ADDRESS = "Homestead/Anim/waterfall"
CLIFF = "craftpix-net-556799-isometric-ruin-tileset/PNG/Platforms/19.png"

FRAMES = 8
SS = 4                  # supersample; the pack is flat vector, so edges must be crisp
AUTHOR_W = 384          # see the module docstring
SCALE = 0.60            # drawn width = AUTHOR_W * SCALE * HomesteadScreen.PieceScale
LIFT = 0.46             # sinks the island's tapered underside into the tile it stands on

# The pond's palette, so the fall and the water it comes from are the same water.
DEEP = (44, 130, 172, 255)
BODY = (59, 161, 201, 255)
LIGHT = (108, 198, 226, 255)
BRIGHT = (176, 230, 243, 255)
FOAM = (238, 250, 253, 255)

CX = 300                # where the water goes over, in the source sprite's own pixels
HALF_TOP = 29
HALF_BOT = 37


class Cliff(object):
    """The source block, and the three edges the water is hung from."""

    def __init__(self, path):
        self.image = Image.open(path).convert("RGBA")
        self.w, self.h = self.image.size

        a = np.array(self.image).astype(int)
        rgb, alpha = a[..., :3], a[..., 3]

        green = (rgb[..., 1] > rgb[..., 0] + 15) & (rgb[..., 1] > 60) & (alpha > 200)
        blue = (rgb[..., 2] > 150) & (rgb[..., 2] > rgb[..., 0] + 40) & (alpha > 200)
        solid = alpha > 200

        self.lip = self._lowest(green)    # the bottom of the grass slab: where the fall starts
        self.pond = self._lowest(blue)    # the near edge of the pool it comes out of
        self.foot = self._lowest(solid)   # the bottom of the rock: where it lands

        self.y_lip = self.lip[CX]
        self.y_foot = min(self.foot[CX - HALF_BOT], self.foot[CX + HALF_BOT]) - 6

        if self.y_lip is None or self.y_foot is None:
            sys.exit("could not read the cliff's edges; is %s the right sprite?" % CLIFF)

    def _lowest(self, mask):
        out = {}
        for x in range(self.w):
            column = np.nonzero(mask[:, x])[0]
            out[x] = int(column.max()) if len(column) else None
        return out


def frame(cliff, t):
    """One frame of the loop. `t` runs 0..1 and every cycle inside is periodic over it."""
    layer = Image.new("RGBA", (cliff.w * SS, cliff.h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    s = lambda v: int(round(v * SS))
    poly = lambda pts, col: d.polygon([(s(x), s(y)) for x, y in pts], fill=col)
    ell = lambda cx, cy, rx, ry, col: d.ellipse(
        [s(cx - rx), s(cy - ry), s(cx + rx), s(cy + ry)], fill=col)

    y_lip, y_foot = cliff.y_lip, cliff.y_foot

    def half_at(f):
        """How wide the sheet is, as a fraction down it. It flares, never narrows."""
        return HALF_TOP + (HALF_BOT - HALF_TOP) * f * f

    def band(a, b, steps=26):
        """A vertical strip of the sheet between two fractions of its width (-1..1)."""
        left, right = [], []
        for k in range(steps + 1):
            f = k / steps
            h = half_at(f)
            top_a = cliff.lip.get(int(round(CX + a * HALF_TOP)), y_lip)
            top_b = cliff.lip.get(int(round(CX + b * HALF_TOP)), y_lip)
            left.append((CX + a * h, top_a + (y_foot - top_a) * f))
            right.append((CX + b * h, top_b + (y_foot - top_b) * f))
        return left + list(reversed(right))

    # ---- the spillway: the pond overflowing across the grass to the edge. Without it the
    # fall starts in the middle of a lawn and reads as pasted on.
    over = [(x, cliff.pond.get(x, y_lip) - 3) for x in range(CX - 25, CX + 26)]
    poly(over + [(x, cliff.lip.get(x, y_lip) + 1) for x in range(CX + 25, CX - 26, -1)], BODY)
    poly(over + [(x, cliff.pond.get(x, y_lip) + 7) for x in range(CX + 25, CX - 26, -1)], LIGHT)

    # ---- the sheet, in three vertical bands. A single flat shape reads as a pane of glass;
    # a shaded side and a lit one read as a body of water.
    poly(band(-1, 1), BODY)
    poly(band(-1, -.38), DEEP)
    poly(band(.55, 1), LIGHT)

    # ---- the streaks, which are what actually reads as motion. Speeds are whole numbers of
    # cycles per loop so the wrap is seamless.
    span = y_foot - y_lip
    for i, (offset, width, length, speed, reps) in enumerate(
            [(-17, 6, 40, 1, 3), (-4, 4, 28, 1, 2), (11, 7, 46, 1, 3), (21, 4, 24, 2, 2)]):
        for rep in range(reps):
            p = (t * speed + rep / reps + i * .17) % 1.0
            top = y_lip + 16 + p * (span - 30)
            bottom = min(y_foot - 8, top + length)
            if bottom <= top:
                continue

            k = (top - y_lip) / span
            x = CX + offset * (1 + .26 * k * k)
            d.rounded_rectangle([s(x - width / 2), s(top), s(x + width / 2), s(bottom)],
                                radius=s(width / 2), fill=BRIGHT if i % 2 else LIGHT)

    # ---- the crest. Deliberately two lumps of foam rather than a highlight along the whole
    # lip: the lip is sheared, so a full-width one reads as tape laid across the fall.
    crest = .5 + .5 * math.sin(t * 2 * math.pi)
    ell(CX - 9, y_lip - 1, 13 + 2 * crest, 6, FOAM)
    ell(CX + 11, y_lip - 4, 11 + 2 * (1 - crest), 5, FOAM)

    # ---- the splash: lobes churning out of phase, so the pool boils instead of pulsing as
    # one disc.
    for dx, dy, rx, ry, phase in [(-15, 2, 25, 10, 0.0), (14, 3, 23, 9, .33), (0, -1, 29, 11, .66)]:
        w = .5 + .5 * math.sin((t + phase) * 2 * math.pi)
        ell(CX + dx, y_foot + dy, rx + 5 * w, ry + 3 * w, LIGHT)

    for dx, dy, rx, ry, phase in [(-12, -2, 17, 7, .5), (12, -1, 16, 6, .17), (0, -4, 20, 8, .83)]:
        w = .5 + .5 * math.sin((t + phase) * 2 * math.pi)
        ell(CX + dx, y_foot + dy, rx + 4 * w, ry + 2 * w, FOAM)

    composed = Image.alpha_composite(cliff.image, layer.resize((cliff.w, cliff.h), Image.LANCZOS))

    height = int(round(AUTHOR_W * cliff.h / cliff.w))
    return composed.resize((AUTHOR_W, height), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True, help="the _extracted folder of the asset packs")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    path = os.path.join(args.source, CLIFF.replace("/", os.sep))
    if not os.path.exists(path):
        sys.exit("no cliff at %s" % path)

    cliff = Cliff(path)
    frames = [frame(cliff, i / FRAMES) for i in range(FRAMES)]

    print("cliff %dx%d - lip y=%d, foot y=%d" % (cliff.w, cliff.h, cliff.y_lip, cliff.y_foot))
    print("%d frame(s) at %dx%d" % (len(frames), frames[0].width, frames[0].height))

    if args.dry_run:
        print("dry run; nothing written")
        return

    # The frame folder is rewritten whole. A stale frame left behind from a longer loop
    # would still be loaded — LoadAll takes the label, not a count — and the animation would
    # stutter once per cycle with nothing in the project to explain it.
    if os.path.isdir(ART):
        shutil.rmtree(ART)
    os.makedirs(ART)

    for i, im in enumerate(frames):
        im.save(os.path.join(ART, "f%02d.png" % i))

    # The old single-sprite waterfall, which nothing points at once the catalog row moves.
    stale = os.path.join(ROOT, "Assets", "Game", "Art", "Homestead", "waterfall.png")
    for suffix in ("", ".meta"):
        if os.path.exists(stale + suffix):
            os.remove(stale + suffix)
            print("removed %s" % os.path.basename(stale + suffix))

    # -------------------------------------------------------------------- catalog
    catalog = json.load(io.open(CATALOG, encoding="utf-8"),
                        object_pairs_hook=collections.OrderedDict)

    row = next((p for p in catalog["pieces"] if p.get("id") == PIECE), None)
    if row is None:
        sys.exit("no '%s' row in homestead.json; this file re-points an existing piece "
                 "rather than adding one, because the id is in save files already" % PIECE)

    row["art"] = ADDRESS
    row["animated"] = True
    row["scale"] = SCALE
    row["lift"] = LIFT

    # `_imported` would make import_grove_art.py warn about a row it no longer owns. It does
    # not own this one any more, and the marker is how each tool says which rows are its own.
    row.pop("_imported", None)
    row["_generated"] = True

    io.open(CATALOG, "w", encoding="utf-8", newline="\n").write(
        json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")

    manifest = io.open(MANIFEST, encoding="utf-8", newline="").read()
    before = json.loads(manifest).get("groveVersion", 1)
    manifest = manifest.replace('"groveVersion": %d,' % before,
                                '"groveVersion": %d,' % (before + 1), 1)
    io.open(MANIFEST, "w", encoding="utf-8", newline="").write(manifest)

    print("catalog: %s -> %s, animated, scale %.2f, lift %.2f" % (PIECE, ADDRESS, SCALE, LIFT))
    print("groveVersion %d -> %d" % (before, before + 1))
    print()
    print("In the Editor, in this order:")
    print("  1. Glimmer Grove > Addressables > Sync All Assets")
    print("     (the importer hook does not fire for files written while the Editor is shut,")
    print("      and an unaddressed frame loads as nothing at all)")
    print("  2. Glimmer Grove > Addressables > Rebuild Grove Atlases")
    print("  3. Glimmer Grove > Validate Art, then Validate Content")


if __name__ == "__main__":
    main()
