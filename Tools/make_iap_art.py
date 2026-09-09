# -*- coding: utf-8 -*-
"""Draws one 1024x1024 store image per in-app product, for the two consoles.

    python Tools/make_iap_art.py                 # write them
    python Tools/make_iap_art.py --check         # prove the written ones are what this draws
    python Tools/make_iap_art.py --contact <png> # lay them out to be looked at

One PNG per product, **named for the product id**, so a console entry and its picture cannot
be paired up wrongly. They are **RGB with no alpha channel**, because App Store Connect
refuses a promotional image carrying transparency, and they carry no text, no price and no
badge, because it refuses those too — the ladder is said by the picture and by nothing else.

**A store image draws what the card draws, and that is the whole reason this is a tool.**
`ShopArt` picks a product's picture by tier through `ShopLadder`, so the pairing is arithmetic
rather than a list somebody keeps up to date; typing sixteen file names here would be a second
copy of that arithmetic, kept by hand, whose failure is a customer buying the pack above the
one whose picture they tapped. So the ranking and the rung are mirrored from
`StoreCatalog.RankShelves` and `ShopLadder.Rung`, the products are read from
`progression.json`, and the pictures are re-cut from the same pack `make_shop_art.py` cuts.

**Re-cut from the pack rather than upscaled from the shipped 512s.** A store image is the
largest this art is ever drawn, and the pack's own sprites are 830-1160 px, so cutting again
costs nothing and upscaling would throw away detail the pack already has.

**The ladder survives, with its floor raised.** A console lists these one under another rather
than side by side, so a rung drawn at 55% of its frame reads as a mistake rather than as the
smallest pack; at 72% the growth still reads and nothing looks lost. That is the one number
here that is a judgement rather than a mirror.

**The three heart vessels are the weak ones and are marked as such.** Their in-game picture is
composed at run time from `ic_heart` and a potion bottle — hand icons about 170 px tall — so a
1024 frame is a four-fold upscale of flat vector art. It is still the right picture, because a
store image showing art the app does not draw is worse than a soft one; what it wants is a
higher-resolution heart and bottle, which is an art order rather than a tool change.
`--contact` is where that is obvious, which is why it exists.
"""
from __future__ import annotations

import argparse
import io
import json
import math
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

try:
    import numpy as np
except ImportError:                                        # pragma: no cover
    sys.exit("This needs numpy:  python -m pip install numpy")

REPO = Path(__file__).resolve().parent.parent
UI = REPO / "Assets" / "Game" / "Art" / "Ui"

DEFAULT_SOURCE = Path(r"C:\Users\Digikey\Downloads\2D ASSETS\_extracted")
DEFAULT_OUT = Path(r"C:\Users\Digikey\Desktop\GG-STORE-ASSETS\IAP - NEW")

PACK = Path("layerlab-shop-pack-2") / "Sprites" / "Icons_Shop" / "2x"

SIZE = 1024         # what both consoles ask for
PAD = 0.10          # air around the object, as a fraction of the frame
FLOOR = 0.72        # the smallest a rung is drawn at; see the module docstring
ALPHA = 8


# The pictures each shelf climbs, smallest first — the same lists `make_shop_art.py` cuts the
# shipped sprites from, because a store image and a card have to be the same picture.
LADDERS = {
    "coins": ["gold_pack_1", "gold_pack_2", "gold_pack_3", "gold_pack_4"],
    "gems": ["gem_diamond_purple",
             "gem_pack_purple_1", "gem_pack_purple_2", "gem_pack_purple_3",
             "gem_pack_purple_4", "gem_pack_purple_5"],
    "bundles": ["chest_gold", "chest_gem", "chest_premium"],
}

# The event pass draws the top bundle chest, exactly as `ShopArt.Paint` does.
PASS_STEM = "chest_premium"

# `ShopArt.Vessels`, and the same three bottles.
VESSELS = ["potion2", "potion4", "potion6"]

# `Pal`, so the light behind a picture is the light behind its card.
TONES = {
    "coins": (0xFF, 0xC2, 0x3C),        # Pal.Gold
    "gems": (0xFF, 0x74, 0xD4),         # Pal.Bloom
    "bundles": (0x3B, 0xE9, 0xD8),      # Pal.Aqua
    "supplies": (0xE8, 0x61, 0x5A),     # Pal.Rose
    "event_pass": (0xFF, 0xC9, 0x3C),   # Pal.Sun
}

PLATE = (26, 44, 59)                    # the card's own plate


# ------------------------------------------------------- mirrors of the shipping rules
def rung(tier, shelf_size, rungs):
    """`ShopLadder.Rung`, whole numbers throughout, rounded half up."""
    if rungs <= 1:
        return 0
    if shelf_size <= 1 or tier <= 0:
        return rungs - 1

    steps = shelf_size - 1
    r = ((tier - 1) * (rungs - 1) * 2 + steps) // (steps * 2)
    return 0 if r < 0 else rungs - 1 if r > rungs - 1 else r


def token_pile(total, token):
    """`TokenPile.Of` — two rows, wider at the bottom, centred, back row first.

    Returns (x, y, tilt) with y positive upward, which is Unity's sense rather than PIL's.
    """
    if total <= 0:
        return []

    spacing, row_step, lean = 0.80, 0.60, 12.0
    front = (total + 2) // 2
    back = total - front
    step = token * row_step if back > 0 else 0.0

    def row(count, y):
        out = []
        for n in range(count):
            i = n // 2 if n % 2 == 0 else count - 1 - n // 2
            frm = i - (count - 1) * 0.5
            reach = (count - 1) * 0.5
            out.append((frm * token * spacing, y,
                        0.0 if reach <= 0 else -(frm / reach) * lean))
        return out

    return row(back, step * 0.5) + row(front, -step * 0.5)


def products():
    """Every in-app product, ranked the way `StoreCatalog.RankShelves` ranks it."""
    store = json.loads(
        (REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json")
        .read_text(encoding="utf8"))["store"]

    shelves = {}
    for p in store["products"]:
        shelves.setdefault(p.get("shelf"), []).append(p)

    out = []
    for shelf, on in shelves.items():
        # Ordered by the reference price rather than by the file, exactly as the game does.
        on.sort(key=lambda p: p.get("referenceUsdCents", 0))
        for i, p in enumerate(on):
            p = dict(p)
            p["_shelf"], p["_tier"], p["_size"] = shelf, i + 1, len(on)
            p["_oneTime"] = p.get("kind") == "nonconsumable"
            out.append(p)
    return out


def picture_of(product):
    """Which source a product's card draws, as (kind, payload). `ShopArt.Paint`'s branches."""
    shelf, tier, size = product["_shelf"], product["_tier"], product["_size"]

    if product.get("eventPassId"):
        return "stem", PASS_STEM

    # Ranked rather than shortcut to the top: `ShopArt.PaintContainer`'s reason - every
    # vessel is a non-consumable, so the one-time rule would draw all three the same.
    if product.get("heartCapacity", 0) > 0:
        return "vessel", rung(tier, size, len(VESSELS))

    ladder = LADDERS.get(shelf)
    if ladder is None:
        sys.exit(f"{product['id']}: shelf '{shelf}' has no ladder here")

    at = len(ladder) - 1 if product["_oneTime"] else rung(tier, size, len(ladder))
    return "ladder", (shelf, at)


# --------------------------------------------------------------------------- drawing
def trimmed(path):
    """One source, cropped to what is painted."""
    if not path.exists():
        sys.exit(f"source missing: {path}\n"
                 "pass --source, or see the art-source-packs note for where the packs live")

    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)[..., 3]
    ys, xs = np.nonzero(a > ALPHA)
    if not len(xs):
        sys.exit(f"source is empty: {path}")
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))


def ground(tone, size=SIZE):
    """The plate, with a soft round light behind where the object stands.

    The card draws a `UIKit.Halo` in its shelf's colour behind every picture, so a store
    image without one is the same object on a different screen.
    """
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.hypot(x - size * 0.5, y - size * 0.52) / (size * 0.5)

    glow = np.clip(1.0 - r, 0.0, 1.0) ** 2.2
    plate = np.array(PLATE, np.float32)
    lit = plate[None, None, :] + np.array(tone, np.float32)[None, None, :] * glow[..., None] * 0.30

    # A touch darker at the very edge, so the frame reads as a plate rather than as a card
    # somebody forgot to crop.
    lit *= np.clip(1.15 - r * 0.35, 0.0, 1.0)[..., None]
    return Image.fromarray(np.clip(lit, 0, 255).astype(np.uint8), "RGB")


def shadow(layer, size=SIZE):
    """A soft drop shadow, so an object sits on the plate rather than floats over it."""
    a = layer.split()[3].filter(ImageFilter.GaussianBlur(size * 0.022))
    dark = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dark.putalpha(a.point(lambda v: int(v * 0.42)))
    return dark.transform(dark.size, Image.AFFINE, (1, 0, 0, 0, 1, -size * 0.018))


def flattened(layer, tone):
    """One finished store image: plate, light, shadow, object, no alpha channel."""
    frame = ground(tone).convert("RGBA")
    frame.alpha_composite(shadow(layer))
    frame.alpha_composite(layer)
    return frame.convert("RGB")


def on_frame(im, scale, size=SIZE, pad=PAD):
    """One picture, drawn at `scale` of the frame's drawable room and centred in it."""
    room = size * (1 - pad * 2) * scale
    s = min(room / im.width, room / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)

    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    layer.paste(im, ((size - im.width) // 2, (size - im.height) // 2))
    return layer


def crisp(im, box):
    """Resamples a small flat-shaded icon up, and puts its edges back.

    The heart and the bottles are hand icons about 170 px tall drawn as flat regions with a
    hard outline, so a four-fold LANCZOS is soft everywhere the art is actually crisp: the
    silhouette becomes a gradient over four pixels and every internal boundary blurs with it.
    Steepening the alpha around its own half-way point restores the silhouette exactly,
    because the true edge really is a step; an unsharp pass does as much as is honest for the
    interior. Neither invents detail — they undo an interpolation over art that has none.
    """
    s = min(box[0] / im.width, box[1] / im.height)
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)

    r, g, b, a = im.split()
    a = a.point(lambda v: max(0, min(255, int((v - 128) * 3.2 + 128))))
    rgb = Image.merge("RGB", (r, g, b)).filter(
        ImageFilter.UnsharpMask(radius=max(1, im.width // 90), percent=80, threshold=2))
    return Image.merge("RGBA", (*rgb.split(), a))


def vessel_layer(at, size=SIZE):
    """`ShopArt.PaintContainer` — a bottle with hearts riding its lip."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    room = size * (1 - PAD * 2)

    tall = room * (0.62 + at * 0.07)
    bottle = crisp(Image.open(UI / f"{VESSELS[at]}.png").convert("RGBA"),
                   (tall * 0.72, tall))
    layer.paste(bottle,
                ((size - bottle.width) // 2,
                 int((size - bottle.height) / 2 + room * 0.10)),   # Unity's -y is PIL's +y
                bottle)

    token = room * 0.26
    heart = crisp(Image.open(UI / "ic_heart.png").convert("RGBA"), (token, token))

    for x, y, tilt in token_pile(3 + at, token):
        one = heart.rotate(tilt, Image.BICUBIC, expand=True)
        layer.alpha_composite(
            one,
            (int(size / 2 + x - one.width / 2),
             int(size / 2 - y - room * 0.20 - one.height / 2)))
    return layer


def build(source):
    folder = source / PACK
    made, soft = {}, []

    for product in products():
        kind, payload = picture_of(product)
        tone = TONES.get(product["_shelf"], TONES["coins"])

        if kind == "vessel":
            layer = vessel_layer(payload)
            soft.append(product["id"])
        else:
            if kind == "stem":
                stem, scale = payload, 1.0
            else:
                shelf, at = payload
                stems = LADDERS[shelf]
                cut = [trimmed(folder / f"{s}.png") for s in stems]
                widest = max(math.hypot(c.width, c.height) for c in cut)
                here = math.hypot(cut[at].width, cut[at].height)
                stem, scale = stems[at], max(FLOOR, here / widest)

            layer = on_frame(trimmed(folder / f"{stem}.png"), scale)

        made[product["id"]] = flattened(layer, tone)

    return made, soft


# --------------------------------------------------------------------------- looking
def contact(made, path, cols=4, cell=256):
    names = list(made)
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * cell), (12, 14, 18))
    for i, name in enumerate(names):
        sheet.paste(made[name].resize((cell, cell), Image.LANCZOS),
                    ((i % cols) * cell, (i // cols) * cell))
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


# --------------------------------------------------------------------------- entry
def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", type=Path, default=DEFAULT_SOURCE,
                    help="folder holding the extracted art packs")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT,
                    help="where the store images go")
    ap.add_argument("--check", action="store_true",
                    help="fail if the written PNGs differ from what this would draw")
    ap.add_argument("--contact", type=Path, metavar="PNG",
                    help="also write a contact sheet; look at it")
    args = ap.parse_args()

    made, soft = build(args.source)
    if not args.check:
        args.out.mkdir(parents=True, exist_ok=True)

    stale = []
    for name, img in sorted(made.items()):
        path = args.out / f"{name}.png"

        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        data = buf.getvalue()

        if args.check:
            if not path.exists() or path.read_bytes() != data:
                stale.append(name)
            continue

        path.write_bytes(data)
        print(f"  wrote {path.name}  {img.width}x{img.height}  {len(data) // 1024} KB")

    if args.contact:
        contact(made, args.contact)
        print(f"  wrote {args.contact}  - look at it; --check cannot")

    if args.check:
        if stale:
            sys.exit("stale, re-run without --check: " + ", ".join(stale))
        print(f"store images are what the tool would draw ({len(made)})")
        return

    print(f"\n{len(made)} product image(s), 1024x1024, RGB with no alpha.")
    if soft:
        print("upscaled from ~170px hand icons and visibly softer than the rest: "
              + ", ".join(sorted(soft)))


if __name__ == "__main__":
    main()
