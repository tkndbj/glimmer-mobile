# -*- coding: utf-8 -*-
"""Cuts the shop's coin and gem ladders out of the two licensed sheets.

    python Tools/make_shop_art.py            # write them
    python Tools/make_shop_art.py --check    # prove the shipped PNGs are what this writes

Twelve outputs under `Assets/Game/Art/Ui/Shop/` — `coins_1..6` and `gems_1..6` — one
painted picture per rung of a money shelf. `ShopArt` picks one by
`StoreProduct.TierFraction`, so a rung inserted in the middle of a shelf re-draws every
card above it with no art order and no edit anywhere else, exactly as the composed
pictures these replace did.

**Why one picture per rung rather than a composed pile.** The storefront used to build a
card out of a container sprite plus a heap of the game's own coin and gem, and the
argument for that was a good one: thirteen near-identical piles of coins is a texture
budget spent on the difference between four coins and six, and a composed card cannot
drift from the ladder. What it could not do is *look* like money. A shelf where every
rung is the same two tokens in slightly different quantities reads as one product listed
six times, which is the one thing a shop must not read as. These are painted, so a rung
is legible as bigger before a word is read — and the ladder is still derived, because
which of the six is drawn is still a function of the tier and nothing else.

**The bundles reuse the coin sheet rather than owning art of their own.** Three of the
six coin pictures are painted with gems in among the coins, and those three are exactly
what a bundle sells. Cutting them a second time under a bundle name would put identical
pixels at two addresses in the same global bundle, which is memory spent to avoid sharing
a string — see `ShopArt.Bundles`.

**The background is cut by where the ink stops, not by what colour it is, and that is the
whole tool.** Both sheets are painted on a flat ground with a soft coloured glow behind
each object, and the glow overlaps the objects in brightness *and* in hue completely — the
gem piles' own violet sits inside the range their halo covers, and the coin sack's body is
in places darker than the ground it stands on. So no threshold on colour separates them:
the version that tried shipped a sack with its bottom keyed away, a chest with its lid
sliced off flat, and crescents of leftover halo on three cards, all of which passed every
check in this repo.

What does separate them is that a painted thing has an **edge** and a glow does not. So the
ground is found by flooding inward from the tile's border and stopping wherever the picture
turns sharply; whatever the flood cannot reach is the object. That inverts which mistake is
cheap. An edge threshold set too low only means interior detail becomes a barrier too, and
an interior barrier is invisible — it is inside the silhouette, so the hole fill puts it
back. Where the old test had to be right about every pixel, this one has to be right about
one closed curve.

**The reading it floods over is `perp`, and that is the half that makes it work on a glow.**
The halo is the ground's own hue scaled up, so its residual against the ground lies almost
exactly along one axis in RGB, while every painted thing carries some colour off that axis.
Splitting the residual into a component along that axis and one across it and then flooding
over the one *across* means the halo, its rays, the sparkles' bloom and the drop shadow are
all flat — they raise no edge at any threshold, so none of them can wall the flood off, and
none of them is admitted. Run on raw colour instead, the rays fence off pockets of empty
ground between them and every gem pile keeps a hard-edged blue blob it never had.

The axis is measured, not written down, which needs a silhouette to measure around — hence
a first rough flood on raw colour whose only job is to be roughly right.

**The source is outside the repo** (see the art-source-packs note): two Freepik EPS packs,
of which only the shipped JPG preview is used. That is not a compromise — the sheets are
9600 and 6161 pixels wide, so a single item arrives at about 1500 px against a 512 px
target, and rendering the EPS would need a PostScript interpreter this project does not
otherwise depend on.

**`--check` proves reproducibility, not quality, and the difference cost a shipped card.**
It compares bytes, so it is silent about whether a cut-out is any *good* — and the coin sack
shipped with its whole shaded left side keyed away, wearing its own white outline wrapped
around nothing, having passed every check in this repo. Two numeric gates were tried after the
fact (how much of a sprite is outline, and whether its silhouette encloses anything) and
**neither separates a broken cut from a healthy one**, because a bite out of one side of an
object is not distinguishable by any global statistic from an object that legitimately has a
thin part. So framing and completeness are judged by *looking*, and `--contact` makes that one
command: it lays all twelve out at the size a card actually draws them, on the card's own plate
colour. That is `render_wheel.py`'s bargain — everything provable is proved, and the thing that
is only visible is made cheap to see.

**The tile boxes are derived, never typed.** Both sheets are laid out as a grid of items
on empty ground, so the boxes come from a projection of the keyed mask and the tool
asserts how many it found. A typed box that silently slid half an item off the edge is
exactly the failure a re-downloaded source would cause, and it is invisible in every
other check.
"""
from __future__ import annotations

import argparse
import io
import sys
from pathlib import Path

try:
    from PIL import Image
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

try:
    import numpy as np
    from scipy import ndimage as ndi
except ImportError:                                        # pragma: no cover
    sys.exit("This needs numpy and scipy:  python -m pip install numpy scipy")

Image.MAX_IMAGE_PIXELS = None

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Shop"

DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\2D ASSETS\_extracted")

SIZE = 512          # the card draws this at about 236 points; 512 is the honest ceiling
PAD = 0.03          # a hair of air so a silhouette never touches the sprite's edge


# --------------------------------------------------------------------------- the sheets
class Sheet:
    """One source sheet, and how its items are laid out on it.

    `flat` says whether the ground is one colour or a gradient. The gem sheet is a single
    value everywhere; the coin sheet is a horizontal wash, so its ground has to be measured
    per column or the items at the ends key differently from the ones in the middle.

    `edge` is how steeply the off-axis colour has to turn to count as a painted outline.
    Both sheets want the same number, which is the point rather than a coincidence: the
    reading it is applied to has the ground's own hue divided out of it, so what is left is
    a fact about ink meeting ink. It stays a per-sheet knob because a third pack painted on
    a busier ground would need its own.
    """

    def __init__(self, folder, name, flat, rows, cols, edge):
        self.folder, self.name, self.flat = folder, name, flat
        self.rows, self.cols = rows, cols
        self.edge = edge


COINS = Sheet("freepik-shop-coins", "2303.w054.n005.336B.p1.336.jpg",
              flat=False, rows=1, cols=6, edge=3.0)

GEMS = Sheet("freepik-shop-gems", "2211.w030.n003.515B.p1.515.jpg",
             flat=True, rows=2, cols=4, edge=3.0)

# Which tile of which sheet becomes which rung, smallest first. A tile is (row, column).
#
# The coin sheet is already painted as a ladder, so it is taken in order. The gem sheet is
# two rows of four and is not: its rungs are the three loose piles by how much is in them,
# then the sack, then the chest, then the chest with gems spilling out of it. A vessel
# arriving at rung four is what makes the top half of that shelf read as a step up rather
# than as more of the same, which is the whole reason these pictures replaced composed ones.
LADDERS = {
    "coins": (COINS, [(0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (0, 5)]),
    "gems":  (GEMS,  [(1, 0), (0, 0), (0, 1), (1, 2), (0, 2), (0, 3)]),
}


# --------------------------------------------------------------------------- keying
def disc(r):
    y, x = np.ogrid[-r:r + 1, -r:r + 1]
    return (x * x + y * y) <= r * r


def belongs(mask, frac=0.01):
    """Keeps the body of the item and the pieces spilled from it, and nothing else.

    Both sheets scatter sparkles, four-pointed stars and bokeh dots over the ground, and a
    keyed one is a real closed shape — it cannot be told from a spilled coin by looking at
    it alone. Two readings together do tell them apart, and neither carries a length in
    pixels, so a re-cut source or a third pack needs no re-tuning:

    * **how big it is, against the body** — a piece that belongs is at least a hundredth of
      what it fell off (a spilled coin 1.1%, the gems heaped beside a chest 10%), and every
      sparkle on either sheet is under 0.6%;
    * **how far it lies, against its own width** — something spilled is touching or nearly
      touching (8 and 64 pixels for pieces 160 and 338 across), where the one sparkle big
      enough to pass the first reading sits 127 pixels off a body it is 44 across.

    The failure this replaced was a fraction of the *tile*, which is a different bar for
    every rung because a tile is as wide as the sheet's widest item: it threw away the coins
    spilling off one chest and kept a star beside another.
    """
    lab, n = ndi.label(mask)
    if n < 2:
        return mask

    sizes = ndi.sum(mask, lab, range(1, n + 1))
    body = 1 + int(np.argmax(sizes))
    away = ndi.distance_transform_edt(lab != body)

    keep = [body]
    for k in range(1, n + 1):
        if k == body or sizes[k - 1] < frac * sizes[body - 1]:
            continue
        width = 2.0 * np.sqrt(sizes[k - 1] / np.pi)
        if away[lab == k].min() <= width:
            keep.append(k)

    return np.isin(lab, keep)


def ground(a, flat):
    """The colour of the empty ground: one value, or one per column."""
    if flat:
        return np.median(a[:5].reshape(-1, 3), 0)[None, None, :]
    return np.median(np.concatenate([a[:30], a[-30:]], 0), 0)[None, :, :]


def steepness(plane, blur=1.2):
    """How fast one plane changes, with the JPEG's own grain smoothed off first.

    The blur is what makes the threshold below meaningful: without it the ringing around
    every painted edge is a few levels everywhere, which is the same size as the signal a
    smooth ground is being judged by.
    """
    plane = ndi.gaussian_filter(plane, blur)
    return np.hypot(ndi.sobel(plane, 1), ndi.sobel(plane, 0)) / 4.0


def colour_steepness(sub, blur=1.2):
    """The same reading over RGB — the steepest of the three channels."""
    sm = ndi.gaussian_filter(sub, (blur, blur, 0))
    out = np.zeros(sub.shape[:2])
    for c in range(3):
        out = np.maximum(out, np.hypot(ndi.sobel(sm[..., c], 1),
                                       ndi.sobel(sm[..., c], 0)) / 4.0)
    return out


def enclosed(barrier):
    """Everything a flood from the tile's border cannot reach without crossing an edge.

    This is the whole tool. See the module docstring for why it replaced a colour test.
    """
    lab, n = ndi.label(~barrier)
    if not n:
        return np.zeros(barrier.shape, bool)

    rim = set(lab[0]) | set(lab[-1]) | set(lab[:, 0]) | set(lab[:, -1])
    rim.discard(0)
    return ndi.binary_fill_holes(~np.isin(lab, sorted(rim)))


def glow_axis(sub, bg, seed):
    """The direction in RGB the halo runs in, measured from the halo itself.

    Taken from a ring just outside a first rough silhouette rather than written down,
    because the two sheets glow in different colours and a third pack would glow in a
    third.
    """
    band = ndi.binary_dilation(seed, disc(25)) & ~ndi.binary_dilation(seed, disc(6))
    res = (sub - bg)[band]
    res = res[np.linalg.norm(res, axis=1) > 25]
    if len(res) < 500:
        return np.array([0.0, 0.0, 1.0])
    axis = res.mean(0)
    return axis / np.linalg.norm(axis)


def keyed(sub, bg, edge_t):
    """The alpha for one item: painted thing 1, ground and halo and shadow 0."""
    # A first flood on raw colour, run only so the glow has a silhouette to be measured
    # around. It is allowed to be wrong in exactly the way the second one is not — a ray
    # admitted here moves the axis by nothing, because the axis is an average over the
    # whole ring and a ray points along it anyway.
    seed = enclosed(colour_steepness(sub) > 4.0)
    axis = glow_axis(sub, bg, seed)

    res = sub - bg
    par = res @ axis
    perp = np.linalg.norm(res - par[..., None] * axis, axis=2)

    # The real pass, and it reads `perp` rather than the picture. Everything the ground
    # does — the halo, its rays, the sparkles' bloom, the drop shadow — is the ground's own
    # hue scaled up or down, so it is flat in `perp` and raises no edge at any threshold.
    # Every painted thing carries some colour off that axis, so its outline does.
    m = enclosed(steepness(perp) > edge_t)

    # A ray's *core* is bright enough to raise an edge of its own, and a sparkle is a
    # painted star with real corners; both are thin, and a body is not.
    m = ndi.binary_opening(m, disc(3))
    m = belongs(m)
    m = ndi.binary_fill_holes(m)

    # The flood halts on the far side of the edge it met, so the mask carries the outer
    # half of that edge — a couple of pixels of ground wrapped round the silhouette, which
    # on the card's plate is a dark rim.
    m = ndi.binary_erosion(m, disc(2))

    alpha = ndi.gaussian_filter(m.astype(np.float32), 1.4)
    return np.clip((alpha - 0.52) / 0.30, 0, 1)


def square(sub, alpha, size=SIZE, pad=PAD):
    """Trims to the silhouette, squares it off and resamples to the shipped size."""
    im = Image.fromarray(
        np.dstack([np.clip(sub, 0, 255), alpha * 255]).astype(np.uint8), "RGBA")

    ys, xs = np.nonzero(alpha > 0.03)
    im = im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))

    w, h = im.size
    side = int(max(w, h) * (1 + pad * 2))
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - w) // 2, (side - h) // 2))
    return canvas.resize((size, size), Image.LANCZOS)


# --------------------------------------------------------------------------- the grid
def spans(profile, expected, what):
    """Runs of occupied lines in a projection, with a hair of air trimmed either side."""
    on = profile > 6
    runs, start = [], None
    for i, live in enumerate(on):
        if live and start is None:
            start = i
        elif not live and start is not None:
            if i - start > 40:
                runs.append((start, i))
            start = None
    if start is not None and len(on) - start > 40:
        runs.append((start, len(on)))

    if len(runs) != expected:
        sys.exit(f"{what}: found {len(runs)} bands, expected {expected} — "
                 "the source sheet is not the one this tool was cut against")
    return runs


def tiles(sheet, source):
    """Every item on one sheet, as (row, column) -> (image array, ground)."""
    path = source / sheet.folder / sheet.name
    if not path.exists():
        sys.exit(f"source sheet missing: {path}\n"
                 "pass --source, or see the art-source-packs note for where the packs live")

    a = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    bg = ground(a, sheet.flat)

    rough = (np.abs(a - bg).max(2) > 40)
    xs = spans(rough.sum(0), sheet.cols, f"{sheet.folder} columns")
    ys = spans(rough.sum(1), sheet.rows, f"{sheet.folder} rows")

    # A little room round each band, because the projection stops at the last strongly
    # keyed pixel and the soft edge of a silhouette reaches a touch past it.
    def pad(lo, hi, limit, room=24):
        return max(0, lo - room), min(limit, hi + room)

    out = {}
    for r, (y0, y1) in enumerate(ys):
        y0, y1 = pad(y0, y1, a.shape[0])
        for c, (x0, x1) in enumerate(xs):
            x0, x1 = pad(x0, x1, a.shape[1])
            sub = a[y0:y1, x0:x1]
            out[(r, c)] = (sub, bg[:, x0:x1] if bg.shape[1] > 1 else bg)
    return out


def build(source):
    made = {}
    for prefix, (sheet, rungs) in LADDERS.items():
        grid = tiles(sheet, source)
        for rung, cell in enumerate(rungs, start=1):
            sub, bg = grid[cell]
            made[f"{prefix}_{rung}"] = square(sub, keyed(sub, bg, sheet.edge))
    return made


# --------------------------------------------------------------------------- looking
# The card's own plate, so a cut-out is judged against the ground it will really be seen on
# rather than against a checkerboard — a dark fringe is invisible on one and obvious on the
# other. Kept in step with `ProductCard.Draw` by hand; it is a diagnostic, not a rendering.
PLATE = (26, 44, 59)
CELL = 236          # what a shelf card draws its picture at, in reference units


def contact(made, path):
    """Lays every rung out at card size, in ladder order, on the card's plate."""
    names = sorted(made, key=lambda n: (n.split("_")[0], int(n.split("_")[1])))
    cols = max(len(v[1]) for v in LADDERS.values())
    rows = (len(names) + cols - 1) // cols

    sheet = Image.new("RGB", (cols * CELL, rows * CELL), PLATE)
    for i, name in enumerate(names):
        cell = made[name].resize((CELL, CELL), Image.LANCZOS)
        sheet.paste(cell, ((i % cols) * CELL, (i // cols) * CELL), cell)

    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


# --------------------------------------------------------------------------- entry
def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE,
                    help="folder holding the extracted art packs")
    ap.add_argument("--check", action="store_true",
                    help="fail if the shipped PNGs differ from what this would write")
    ap.add_argument("--contact", type=Path, metavar="PNG",
                    help="also write a contact sheet, drawn at card size on the card's plate")
    args = ap.parse_args()

    made = build(args.source)
    OUT.mkdir(parents=True, exist_ok=True)

    stale = []
    for name, img in sorted(made.items()):
        path = OUT / f"{name}.png"

        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data = buf.getvalue()

        if args.check:
            if not path.exists() or path.read_bytes() != data:
                stale.append(name)
            continue

        path.write_bytes(data)
        print(f"  wrote {path.relative_to(REPO)}  {img.width}x{img.height}")

    if args.contact:
        contact(made, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run without --check: " + ", ".join(stale))
        print(f"shop art is what the tool would write ({len(made)} sprites)")


if __name__ == "__main__":
    main()
