# -*- coding: utf-8 -*-
"""Cuts the shop's money ladders out of the licensed Layer Lab shop-icon pack.

    python Tools/make_shop_art.py            # write them
    python Tools/make_shop_art.py --check    # prove the shipped PNGs are what this writes
    python Tools/make_shop_art.py --contact <png>   # lay them out at card size; look at it

Thirteen outputs under `Assets/Game/Art/Ui/Shop/` — `coins_1..4`, `gems_1..6` and
`bundles_1..3` — one painted picture per rung of a shelf. `ShopArt` picks one by
`ShopLadder.Rung`, so a rung inserted in the middle of a shelf re-draws every card above it
with no art order and no edit anywhere else.

**Why one picture per rung rather than a composed pile.** The storefront used to build a
card out of a container sprite plus a heap of the game's own coin and gem, and the argument
for that was a good one: thirteen near-identical piles of coins is a texture budget spent on
the difference between four coins and six, and a composed card cannot drift from the ladder.
What it could not do is *look* like money. A shelf where every rung is the same two tokens
in slightly different quantities reads as one product listed six times, which is the one
thing a shop must not read as. These are painted, so a rung is legible as bigger before a
word is read — and the ladder is still derived, because which of them is drawn is still a
function of the tier and nothing else.

**A shelf has as many rungs as the pack has pictures for it, and no more.** The coin shelf
sells four products and the pack paints four coin quantities, so the ladder is four rungs
and every card draws a different picture. It was six for as long as the art came off a sheet
that happened to have six coin tiles, and with four products `ShopLadder` then picked rungs
0, 2, 3 and 5 — so two of the four painted quantities were never drawn in the shop at all.
**A ladder longer than its shelf hides art; a ladder shorter than its shelf repeats it.**
Neither is visible in any check here, because both ship a shelf that is individually correct
on every card.

**The pack carries the ladder in its own pixels, so the sprite keeps it.** These sources are
already cut on transparency, so there is nothing to key — what there is to get right is
*size*, and squaring each picture to its own silhouette (which is what the sheet-cutting
tool this replaces had to do) would draw a single coin as large as a vault and throw the
ladder away. A rung is therefore scaled by its own source against the largest in its ladder,
with a floor so that no card's picture is a dot. The consequence to know: **the shipped PNGs
are all the same square, and the growth is baked in as transparent margin**, because
`ShopArt` draws a sprite to the whole box.

**A ladder that does not grow is refused.** `LADDERS` is walked in order and a rung whose
source is no bigger than the one below it fails the run by name, which is the one mistake a
typed list of file names can make and the one nothing downstream could see — three chests
that are all the same size are a shelf that reads as one product listed three times, and
that is the fault these pictures exist to fix. The bundles are the deliberate exception and
say so: they are three chests of one size told apart by colour, so they declare `grows` off.

**The source is outside the repo** (see the art-source-packs note): the Layer Lab
*2D Icons - Shop Pack 2* `.unitypackage`, extracted beside the CraftPix packs. The **2x**
sprites are the ones used — the 1x set arrives at about 450 px against a 512 px target. The
pack is not imported into `Assets/`, because nothing in the game draws it directly: what
ships is what this tool writes.

**`--check` proves reproducibility, not quality, and the difference cost a shipped card.**
It compares bytes, so it is silent about whether a cut-out is any *good* — and the coin sack
this replaces shipped with its whole shaded left side keyed away, having passed every check
in this repo. Two numeric gates were tried after the fact (how much of a sprite is outline,
and whether its silhouette encloses anything) and **neither separates a broken cut from a
healthy one**. So framing and completeness are judged by *looking*, and `--contact` makes
that one command: it lays every rung out at the size a card actually draws it, on the card's
own plate colour, in ladder order. Everything provable is proved, and the thing that is only
visible is made cheap to see.
"""
from __future__ import annotations

import argparse
import io
import math
import sys
import tarfile
from pathlib import Path

try:
    from PIL import Image
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

try:
    import numpy as np
except ImportError:                                        # pragma: no cover
    sys.exit("This needs numpy:  python -m pip install numpy")

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "Assets" / "Game" / "Art" / "Ui" / "Shop"

DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\2D ASSETS\_extracted")

PACK = Path("layerlab-shop-pack-2") / "Sprites" / "Icons_Shop" / "2x"

SIZE = 512          # the card draws this at about 236 points; 512 is the honest ceiling
PAD = 0.03          # a hair of air so a silhouette never touches the sprite's edge
FLOOR = 0.55        # the smallest a rung may be drawn, as a fraction of its box
ALPHA = 8           # what counts as painted when trimming; the pack's shadow is softer


class Ladder:
    """One shelf's pictures, smallest first.

    `grows` says whether the sources must ascend in size. It is on everywhere the picture
    carries the amount and off for the bundles, which are three chests of one size told
    apart by what is painted on them — see the module docstring.
    """

    def __init__(self, stems, grows=True):
        self.stems, self.grows = stems, grows


# Which source becomes which rung. The gem shelf opens on a single cut stone rather than on
# the smallest heap, which is what lets six products have six different pictures: the pack
# paints five gem quantities, and a shelf of six over a ladder of five draws two adjacent
# cards identically.
LADDERS = {
    "coins": Ladder(["gold_pack_1", "gold_pack_2", "gold_pack_3", "gold_pack_4"]),
    "gems": Ladder(["gem_diamond_purple",
                    "gem_pack_purple_1", "gem_pack_purple_2", "gem_pack_purple_3",
                    "gem_pack_purple_4", "gem_pack_purple_5"]),
    # Purple, because the gem the game itself draws is magenta — a blue or green ladder
    # would be a second gem as far as a player is concerned.
    "bundles": Ladder(["chest_gold", "chest_gem", "chest_premium"], grows=False),
    # A bundle grants both currencies and the pack paints no mixed pile, so the shelf is
    # told apart by what a chest is made of rather than by how full it is: gold, then gems,
    # then the one wearing both. They used to borrow the coin ladder, which said only half
    # of what a bundle sells.
}

# The seven-day passes (`StoreProduct.PassDays`): not rungs of their shelves, so not ladders.
# Each is the Casual Icon Pack's blue gift box - "a gift every day" - with the currency it pays
# standing in front of it at the lower right: the shop's own cut gem, and the pack's gold coin.
# Composed here rather than on the card, so the card draws one picture like every other and
# the composition is reproducible (`--check`). Both the box and the coin were unused anywhere
# in the game before; the gem is `gems_1`'s source, so the pass wears the shop's gem.
CASUAL = (Path.home() / "AppData" / "Roaming" / "Unity" / "Asset Store-5.x" / "LAYERLAB" /
          "Textures MaterialsIcons UI" / "2D Icons - Casual Icon Pack.unitypackage")
GIFT = "Assets/Layer Lab/2D Icons-CasualIconPack/Icons/512/Icon_Misc_GiftBox01_Blue.png"

#: name -> (the token in front, its long edge as a fraction of the canvas)
PASSES = {
    "pass_gems": ("gem_diamond_purple", .52),
    "pass_coins": ("coin_gold", .46),
}
GIFT_EDGE = .84          # the box's long edge, as a fraction of the canvas
GIFT_SHIFT = (-26, -10)  # nudged up and left so the token has the corner to itself
TOKEN_INSET = 8          # the token's distance from the lower-right edges, in canvas pixels


# Deliberately not cut: the pack's crowned `chest_event`, which is the obvious picture for
# the event pass and reads at card size as a crowned **owl face** — cream front, two dark
# clasps where eyes would be, teal leaves either side. In a game whose companions are
# woodland critters that is not a chest anybody will see. The pass draws the premium chest
# instead, sharing a string rather than duplicating pixels. Invariant 32b, caught the way
# 32b says it is caught: by looking at it.


# --------------------------------------------------------------------------- cutting
def names_of(prefix, ladder):
    """What one ladder's rungs are called. A shelf of one is named for the shelf."""
    if len(ladder.stems) == 1:
        return [prefix]
    return [f"{prefix}_{i}" for i in range(1, len(ladder.stems) + 1)]


def trimmed(path):
    """One source, cropped to what is painted, with the size of that silhouette."""
    if not path.exists():
        sys.exit(f"source sprite missing: {path}\n"
                 "pass --source, or see the art-source-packs note for where the packs live")

    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)[..., 3]

    ys, xs = np.nonzero(a > ALPHA)
    if not len(xs):
        sys.exit(f"source sprite is empty: {path}")

    box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
    return im.crop(box)


def diagonal(im):
    """How big a picture is, in the one reading that is fair to a tall one and a wide one."""
    return math.hypot(im.width, im.height)


def placed(im, scale, size=SIZE, pad=PAD):
    """One picture, drawn at `scale` of a square canvas and centred in it."""
    room = size * scale * (1 - pad * 2)
    s = min(room / im.width, room / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))),
                   Image.LANCZOS)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, ((size - im.width) // 2, (size - im.height) // 2))
    return canvas


def shelves():
    """How many products each shelf of the shipped store sells.

    Read rather than typed, because the length of a ladder is the one thing about it that
    nothing downstream can check: too long and `ShopLadder` skips rungs, so a painted
    quantity is shipped that no card draws; too short and two adjacent cards draw the same
    picture. Both ship a shelf that is correct on every card. See invariant 18e.
    """
    import json

    store = json.loads(
        (REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json")
        .read_text(encoding="utf8"))["store"]

    counted = {}
    for product in store["products"]:
        # A pass is drawn from its own picture (`PASSES`), not from its shelf's ladder.
        if product.get("passDays"):
            continue
        shelf = product.get("shelf")
        counted[shelf] = counted.get(shelf, 0) + 1
    return counted


def build(source):
    folder = source / PACK
    sold = shelves()
    made = {}

    for prefix, ladder in LADDERS.items():
        want = sold.get(prefix)
        if want is not None and want != len(ladder.stems):
            sys.exit(
                f"{prefix}: {len(ladder.stems)} pictures against {want} products on that "
                "shelf - a ladder longer than its shelf ships art no card draws, and one "
                "shorter draws two cards the same (invariant 18e)")

        cut = [trimmed(folder / f"{stem}.png") for stem in ladder.stems]
        sizes = [diagonal(im) for im in cut]

        if ladder.grows:
            for i in range(1, len(sizes)):
                if sizes[i] <= sizes[i - 1]:
                    sys.exit(
                        f"{prefix}: rung {i + 1} ({ladder.stems[i]}) is no bigger than rung "
                        f"{i} ({ladder.stems[i - 1]}) — a ladder that does not grow reads as "
                        "one product listed twice; reorder it or set grows=False")

        widest = max(sizes)
        for name, im, d in zip(names_of(prefix, ladder), cut, sizes):
            made[name] = placed(im, max(FLOOR, d / widest))

    made.update(passes(folder))
    return made


def gift_box():
    """The Casual Icon Pack's blue gift box, read straight out of its `.unitypackage`."""
    if not CASUAL.exists():
        sys.exit(f"the Casual Icon Pack is not in Unity's download cache: {CASUAL}. "
                 "Download it from the Asset Store window (My Assets); see the art-source-packs note")

    with tarfile.open(CASUAL, "r:gz") as tar:
        members = {}
        for m in tar.getmembers():
            parts = m.name.split("/")
            if len(parts) == 2:
                members.setdefault(parts[0], {})[parts[1]] = m
        for d in members.values():
            if "pathname" not in d or "asset" not in d:
                continue
            name = tar.extractfile(d["pathname"]).read().decode("utf8").splitlines()[0]
            if name == GIFT:
                im = Image.open(io.BytesIO(tar.extractfile(d["asset"]).read())).convert("RGBA")
                a = np.asarray(im)[..., 3]
                ys, xs = np.nonzero(a > ALPHA)
                return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))

    sys.exit(f"the Casual Icon Pack no longer carries {GIFT}")


def pass_picture(folder, name, size=SIZE, box=None):
    """One pass picture on a `size` square: the gift box with the currency it pays in front.
    `make_iap_art.py` draws the console image from this same function, at its own size."""
    stem, edge = PASSES[name]
    box = box or gift_box()
    token = trimmed(folder / f"{stem}.png")
    k_ = size / SIZE                                       # the offsets are written for SIZE
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    s = size * GIFT_EDGE / max(box.size)
    b = box.resize((round(box.width * s), round(box.height * s)), Image.LANCZOS)
    canvas.alpha_composite(b, ((size - b.width) // 2 + round(GIFT_SHIFT[0] * k_),
                               (size - b.height) // 2 + round(GIFT_SHIFT[1] * k_)))

    s = size * edge / max(token.size)
    k = token.resize((round(token.width * s), round(token.height * s)), Image.LANCZOS)
    inset = round(TOKEN_INSET * k_)
    canvas.alpha_composite(k, (size - k.width - inset, size - k.height - inset))
    return canvas


def passes(folder):
    """The two pass pictures, at the shop's sprite size."""
    box = gift_box()
    return {name: pass_picture(folder, name, SIZE, box) for name in PASSES}


# --------------------------------------------------------------------------- looking
# The card's own plate, so a picture is judged against the ground it will really be seen on
# rather than against a checkerboard — a dark fringe is invisible on one and obvious on the
# other. Kept in step with `ProductCard.Draw` by hand; it is a diagnostic, not a rendering.
PLATE = (26, 44, 59)
CELL = 236          # what a shelf card draws its picture at, in reference units


def contact(made, path):
    """Lays every rung out at card size, one ladder to a row, in ladder order."""
    rows = [names_of(prefix, ladder) for prefix, ladder in LADDERS.items()] + [list(PASSES)]
    cols = max(len(r) for r in rows)
    sheet = Image.new("RGB", (cols * CELL, len(rows) * CELL), PLATE)

    for y, row in enumerate(rows):
        for x, name in enumerate(row):
            cell = made[name].resize((CELL, CELL), Image.LANCZOS)
            sheet.paste(cell, (x * CELL, y * CELL), cell)

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
