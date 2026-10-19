# -*- coding: utf-8 -*-
"""Cuts the personal offers' pictures (invariant 64): a coin pile and a gem pile together, one per rung.

    python Tools/make_offer_art.py            # write them
    python Tools/make_offer_art.py --check    # prove the shipped PNGs are what this writes
    python Tools/make_offer_art.py --contact <png>   # lay them out at card size; look at it

Seven outputs, `Assets/Game/Art/Ui/Shop/offer_1..7.png`, one per product on the `offers` shelf of
`progression.json`. `ShopArt.Offers` reads them at the product's rung (`ShopLadder.Rung`), so the
ladder must be exactly as long as the shelf (18e) - this tool counts the shelf and refuses
otherwise.

**Composed from the shop's own pictures, not cut from a pack.** An offer grants gems and coins
together and no pack paints a mixed pile, so each rung lays one of the shop's coin piles behind one
of its gem piles (`RUNGS`), both already cut by `make_shop_art.py` and committed. That makes this
tool reproducible with no licensed source on the machine, and an offer can never look like a
different currency from the packs the shop sells. Not a chest: a chest is what the game's earned
chests look like, and one sold for money reads as a loot box (10b).

**The ladder grows.** Each rung's pair is a step up from the one below (`RUNGS` never repeats a
pair, and the whole composition is drawn larger rung by rung from `FLOOR`), because a player is
only ever shown one offer at a time and must still read a bigger one as bigger.

**`--check` proves reproducibility, not quality** - `--contact` is the look.
"""

import argparse
import io
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[1]
SHOP = REPO / "Assets" / "Game" / "Art" / "Ui" / "Shop"
OUT = SHOP
SIZE = 512
ALPHA = 8

#: rung -> (the coin pile behind, the gem pile in front). Every pair differs from the one below.
RUNGS = [
    ("coins_1", "gems_2"),
    ("coins_1", "gems_3"),
    ("coins_2", "gems_3"),
    ("coins_2", "gems_4"),
    ("coins_3", "gems_5"),
    ("coins_3", "gems_6"),
    ("coins_4", "gems_6"),
]

#: the smallest rung's composition as a share of the largest's
FLOOR = 0.78

#: each pile's long edge as a share of the canvas at the top rung, and its centre (x, y) as a share
COINS = (0.64, (0.33, 0.36))
GEMS = (0.70, (0.64, 0.63))


def trimmed(name):
    path = SHOP / f"{name}.png"
    if not path.exists():
        sys.exit(f"shop picture missing: {path} - run Tools/make_shop_art.py first")
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)[..., 3]
    ys, xs = np.nonzero(a > ALPHA)
    if not len(xs):
        sys.exit(f"shop picture is empty: {path}")
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))


def shelf_size():
    """How many products the shipped offers shelf sells (18e)."""
    store = json.loads((REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json")
                       .read_text(encoding="utf8"))["store"]
    return sum(1 for p in store["products"] if p.get("shelf") == "offers")


def lay(canvas, im, edge, centre, size=SIZE):
    s = size * edge / max(im.size)
    piece = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    x = round(size * centre[0] - piece.width / 2)
    y = round(size * centre[1] - piece.height / 2)
    canvas.alpha_composite(piece, (max(0, min(size - piece.width, x)), max(0, min(size - piece.height, y))))


def rung(i, size=SIZE, load=None):
    """Rung `i`'s picture on a `size` square. `load` turns a shop picture's name into a trimmed image;
    the shipped 512s by default. `make_iap_art.py` passes the pack's own full-size sprites, so the
    store image is this same composition drawn sharp at 1024."""
    load = load or trimmed
    coins, gems = RUNGS[i]
    k = FLOOR + (1 - FLOOR) * (i / (len(RUNGS) - 1))

    # Each pile drawn at its rung's scale about the canvas centre, so a small rung shrinks inward.
    def at(centre):
        return (0.5 + (centre[0] - 0.5) * k, 0.5 + (centre[1] - 0.5) * k)

    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    lay(canvas, load(coins), COINS[0] * k, at(COINS[1]), size)
    lay(canvas, load(gems), GEMS[0] * k, at(GEMS[1]), size)
    return canvas


def build():
    want = shelf_size()
    if want != len(RUNGS):
        sys.exit(f"offers: {len(RUNGS)} pictures against {want} products on the offers shelf - a ladder "
                 "longer than its shelf ships art no card draws, and one shorter draws two offers the "
                 "same (invariant 18e)")

    for a, b in zip(RUNGS, RUNGS[1:]):
        if a == b:
            sys.exit(f"offers: two rungs draw the same pair {a}; a ladder that does not grow reads as one offer")

    return {f"offer_{i + 1}": rung(i) for i in range(len(RUNGS))}


PLATE = (26, 44, 59)
CELL = 236


def contact(made, path):
    names = sorted(made, key=lambda n: int(n.split("_")[1]))
    sheet = Image.new("RGB", (len(names) * CELL, CELL), PLATE)
    for x, name in enumerate(names):
        cell = made[name].resize((CELL, CELL), Image.LANCZOS)
        sheet.paste(cell, (x * CELL, 0), cell)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="fail if the shipped PNGs differ from what this would write")
    ap.add_argument("--contact", type=Path, metavar="PNG", help="also write a contact sheet at card size")
    args = ap.parse_args()

    made = build()
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
        print(f"offer art is what the tool would write ({len(made)} sprites)")


if __name__ == "__main__":
    main()
