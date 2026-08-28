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

**The background is keyed by chroma, not by brightness, and that is the whole tool.**
Both sheets are painted on a flat ground with a soft coloured glow behind each object, and
the glow overlaps the objects in brightness completely — the gem piles' own violet sits
inside the range their halo covers, and the coin sack's body is in places *darker* than
the ground it stands on. What separates them is direction: the glow is the ground's own
hue scaled up, so its residual against the ground lies almost exactly along one axis in
RGB, while every painted thing on these sheets carries some colour off that axis. So the
residual is split into a component along the glow's axis and one across it, and a pixel is
part of an object when it is far enough across (`--perp`), or extremely far along it *and*
not perfectly on-axis. That last clause is what keeps a violet gem and its own violet halo
apart, and it is the one an earlier version got wrong: without it every gem card kept a
hard-edged crescent of leftover halo above the pile.

A drop shadow is the same problem upside down — painted *darker* than the ground, hanging
off the outside of the silhouette where a hole fill cannot judge it — so it is removed by
the same two numbers, and the object's own dark interiors are put back by a fill afterwards.

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
    """

    def __init__(self, folder, name, flat, rows, cols, perp, par):
        self.folder, self.name, self.flat = folder, name, flat
        self.rows, self.cols = rows, cols
        self.perp, self.par = perp, par


COINS = Sheet("freepik-shop-coins", "2303.w054.n005.336B.p1.336.jpg",
              flat=False, rows=1, cols=6, perp=45, par=300)

GEMS = Sheet("freepik-shop-gems", "2211.w030.n003.515B.p1.515.jpg",
             flat=True, rows=2, cols=4, perp=45, par=205)

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


def biggest(mask, frac=0.004):
    """Drops speckle — the painted sparkles scattered over both grounds."""
    lab, n = ndi.label(mask)
    if not n:
        return mask
    sizes = ndi.sum(mask, lab, range(1, n + 1))
    return np.isin(lab, 1 + np.flatnonzero(sizes > frac * mask.size))


def ground(a, flat):
    """The colour of the empty ground: one value, or one per column."""
    if flat:
        return np.median(a[:5].reshape(-1, 3), 0)[None, None, :]
    return np.median(np.concatenate([a[:30], a[-30:]], 0), 0)[None, :, :]


def outlines(sub, threshold=10, bridge=22):
    """Everything enclosed by a painted edge.

    A closing before the fill is what makes this work on flat interiors: a chest's wooden
    front has no internal detail at all, so its own outline has to be bridged into a ring
    before a hole fill has anything to fill.

    **The threshold is low on purpose, and it has to be.** A silhouette that is open
    anywhere is not a silhouette — the fill leaks out through the gap and the whole interior
    is lost, which is a far worse failure than admitting a little noise. The coin sack is the
    case that proves it: it is dark brown standing on a dark purple ground with its own
    shadow pooling under it, so at a threshold of 20 its *bottom* edge went undetected, the
    fill drained out of the hole and the card shipped showing the sack's white outline
    wrapped around nothing. Nothing but looking at it could have caught that — the sprite was
    the right size, correctly centred, and 24% opaque.

    What makes a low threshold safe here is that the halo these sheets are painted on is
    *smooth*: it carries a lot of brightness and almost no gradient, so it contributes no
    edges at any threshold worth using. Speckle from the painted sparkles and from JPEG
    ringing is real and is what `biggest` is for.
    """
    lum = sub.mean(2)
    mag = np.hypot(ndi.sobel(lum, 1), ndi.sobel(lum, 0)) / 4.0

    m = mag > threshold
    m = ndi.binary_closing(m, disc(bridge))
    m = ndi.binary_fill_holes(m)
    m = ndi.binary_opening(m, disc(3))
    m = ndi.binary_fill_holes(m)
    return biggest(m)


def glow_axis(sub, bg, seed):
    """The direction in RGB the halo runs in, measured from the halo itself.

    Taken from a ring just outside the outlines rather than written down, because the two
    sheets glow in different colours and a third pack would glow in a third.
    """
    band = ndi.binary_dilation(seed, disc(25)) & ~ndi.binary_dilation(seed, disc(6))
    res = (sub - bg)[band]
    res = res[np.linalg.norm(res, axis=1) > 25]
    if len(res) < 500:
        return np.array([0.0, 0.0, 1.0])
    axis = res.mean(0)
    return axis / np.linalg.norm(axis)


def keyed(sub, bg, perp_t, par_t):
    """The alpha for one item: painted thing 1, ground and halo and shadow 0."""
    seed = outlines(sub)
    axis = glow_axis(sub, bg, seed)

    res = sub - bg
    par = res @ axis
    perp = np.linalg.norm(res - par[..., None] * axis, axis=2)

    # Across the halo's axis, or so far along it that no halo reaches — and the second
    # clause is gated on being at least slightly off-axis, or a violet gem's own violet
    # halo is admitted with it.
    m = seed | (perp > perp_t) | ((par > par_t) & (perp > 22))
    m = ndi.binary_closing(m, disc(9))
    m = ndi.binary_fill_holes(m)
    m = ndi.binary_opening(m, disc(4))
    m = biggest(m)
    m = ndi.binary_fill_holes(m)

    # A shadow is the ground made darker and a halo is the ground made brighter; both are
    # on-axis and neither is the object. They hang off the outside of the silhouette, which
    # is where a hole fill has no opinion, so they are cut by value and the object's own
    # dark interiors are put back by the fill that follows.
    m &= ~((par < 14) & (perp < 26))
    m = ndi.binary_closing(m, disc(6))
    m = biggest(m)
    m = ndi.binary_fill_holes(m)

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
            made[f"{prefix}_{rung}"] = square(sub, keyed(sub, bg, sheet.perp, sheet.par))
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
