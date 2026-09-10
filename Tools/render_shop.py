# -*- coding: utf-8 -*-
"""Draws the storefront at the size a phone draws it, with the real sprites.

    python Tools/render_shop.py                 # the gem shelf
    python Tools/render_shop.py --shelf coins   # any shelf in progression.json
    python Tools/render_shop.py --all           # every shelf, side by side

**Why this exists.** Nothing in this project can open a PNG on the way to a build, and the
Editor cannot photograph a `ScreenSpaceOverlay` canvas — so a storefront's *look* is judged
the way every board here is judged, by a Python mirror that reads the same sprites and the
same numbers the screen does. `render_siege.py` earned its place six times over; this is the
same bargain for the one screen that takes money, and it has now earned it twice — once on a
yellow price bar drawn on a yellow card, and once on a ribbon whose words ran onto its tails.

**A mirror only shows what it mirrors, and that cost a round.** This drew cards with no
coloured seat and no fan of rays behind the picture, because it was written after those were
already on the way out — so the shop it drew was *cleaner than the one on the phone*, and the
decoration the owner then asked to have removed was invisible here. A render is a diagnostic
for the things it draws and says nothing at all about the things it does not; when a report
from a device disagrees with this picture, this picture is the one that is wrong.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it
copies (`ShopScreen.HeaderHeight`, `ProductCard`'s offsets, `ProductCardBadges`), so a change
on one side is findable on the other.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H
REPO = K.REPO
UI = K.UI

# ShopScreen
HEADER, TABROW = 300.0, 132.0
RESTORE = 92.0
COLUMNS, CELLW, CELLH = 2, 508.0, 560.0

# ProductCard / ProductCardBadges
PLATE_X, PLATE_Y = 34.0, 40.0
PLATEW, PLATEH = CELLW - PLATE_X, CELLH - PLATE_Y
ART, ART_DROP = 300.0, 168.0
AMOUNT_RISE, SUB_RISE, FACE_RISE = 196.0, 150.0, 74.0
FACEW, FACEH = CELLW - 110.0, 96.0
SEAL, SEAL_DISC, SEAL_TILT = 164.0, .86, -11.0
RIBBONW, RIBBONH, RIBBON_INSET, RIBBON_DROP = 268.0, 76.0, 96.0, 34.0
RIBBON_TILT = 6.0
CLEARANCE, MARGIN = 10.0, 6.0

# `ProductCardBadges.SealInset` / `.SealDrop`, derived rather than copied: the badge walks in
# by itself when the bonus plate beside it grows, and a typed number here would go stale the
# first time it did.
SEAL_REACH = SEAL * SEAL_DISC * .5
RIBBON_REACH = (RIBBONW * .5 * math.cos(math.radians(RIBBON_TILT))
                + RIBBONH * .5 * math.sin(math.radians(RIBBON_TILT)))
NEIGHBOUR_RIBBON = CELLW - PLATEW * .5 + RIBBON_INSET - RIBBON_REACH
SEAL_INSET = PLATEW * .5 - min(PLATEW * .5 - MARGIN - SEAL_REACH,
                               NEIGHBOUR_RIBBON - CLEARANCE - SEAL_REACH)
SEAL_DROP = SEAL_REACH - (PLATE_Y - CLEARANCE)

# Skins.Accent — what tells one shelf from another now the plate is one teal on all of them.
ACCENT = {"gems": K.BLOOM, "coins": K.GOLD, "bundles": K.MINT,
          "supplies": K.ROSE, "utilities": K.SUN}

SPOT_ALPHA = .22


# --------------------------------------------------------------------------- the card
def card(sheet, x, top, shelf, picture, amount, sub, price, badge, bonus=None, live=True):
    """One product card, laid out the way `ProductCard`'s constructor lays one out."""
    plate_cx, plate_cy = x, top + CELLH / 2
    K.paste(sheet, K.skin("Hud/card", PLATEW, PLATEH), plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    # The shelf's own light under the goods, which is the job the coloured frame used to do.
    K.paste(sheet, K.glow(370, 1.6, ACCENT.get(shelf, K.BLOOM), SPOT_ALPHA),
            plate_cx, ptop + ART_DROP)

    art = Image.open(UI / "Shop" / f"{picture}.png").convert("RGBA")
    K.paste(sheet, K.fit(art, (ART, ART)), plate_cx, ptop + ART_DROP)

    K.text(sheet, amount, plate_cx, pbot - AMOUNT_RISE, 46, outline=4)
    K.text(sheet, sub, plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225), outline=2)

    face = K.skin("btn_orange", FACEW, FACEH)               # Skins.Buy
    if not live:
        face = K.tint(face, K.MUTED)
    K.paste(sheet, face, plate_cx, pbot - FACE_RISE)
    K.text(sheet, price, plate_cx, pbot - FACE_RISE, 34, outline=3)

    if bonus:
        # `Skins.Title` is a real cloth ribbon under this kit, so it hangs again — the tilt
        # went to nought only while the mark was a machined plate. Drawn rather than sliced,
        # for the reason `Skins.Title` gives: the tails are the silhouette.
        rib = K.fit(K.load("Hud/title")[0], (RIBBONW, RIBBONH))
        rib = rib.rotate(RIBBON_TILT, Image.BICUBIC, expand=True)
        K.paste(sheet, rib, plate_cx - PLATEW / 2 + RIBBON_INSET, ptop + RIBBON_DROP)
        K.text(sheet, bonus, plate_cx - PLATEW / 2 + RIBBON_INSET,
               ptop + RIBBON_DROP - 4, 25, fill=K.SUN, outline=3)


    if badge:
        seal = K.tint(K.fit(K.load("Hud/burst")[0], (SEAL, SEAL)), K.ROSE)
        seal = seal.rotate(-SEAL_TILT, Image.BICUBIC, expand=True)
        K.paste(sheet, seal, plate_cx + PLATEW / 2 - SEAL_INSET, ptop + SEAL_DROP)
        K.text(sheet, badge, plate_cx + PLATEW / 2 - SEAL_INSET, ptop + SEAL_DROP, 22, outline=2)


# --------------------------------------------------------------------------- the screen
SHELVES = ["gems", "coins", "bundles", "supplies", "utilities"]
TAB_GLYPH = {"gems": "ic_gem", "coins": "Shop/pouch", "bundles": "ic_gift",
             "supplies": "ic_heart", "utilities": "Utility/firepot"}


def products(shelf):
    store = json.loads((REPO / "Assets" / "StreamingAssets" / "Content" / "progression.json")
                       .read_text(encoding="utf8"))["store"]
    on = [p for p in store["products"] if p.get("shelf") == shelf]
    on.sort(key=lambda p: p.get("referenceUsdCents", 0))
    return on


def rung(tier, size, rungs):
    if rungs <= 1:
        return 0
    if size <= 1 or tier <= 0:
        return rungs - 1
    steps = size - 1
    r = ((tier - 1) * (rungs - 1) * 2 + steps) // (steps * 2)
    return max(0, min(rungs - 1, r))


LADDER = {"gems": 6, "coins": 4, "bundles": 3}


def screen(shelf):
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.room(sheet)

    # ---- the header band, then the rail over it
    fade = Image.new("RGBA", (W, int(HEADER + TABROW)), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fade)
    for y in range(fade.height):
        a = int(255 * .62 * (1 - y / float(fade.height)) ** .8)
        fd.line([(0, y), (W, y)], fill=(5, 13, 20, a))
    sheet.alpha_composite(fade, (0, 0))

    K.rail(sheet, top=True)

    # the back key
    K.paste(sheet, K.skin("sq_blue", 112, 112), 94, 128)
    K.paste(sheet, K.tint(K.fit(Image.open(UI / "ic_left.png").convert("RGBA"), (56, 56)), K.CREAM),
            94, 128)

    K.paste(sheet, K.skin("Hud/title", 440, 104), W / 2, 112)
    K.text(sheet, "SHOP", W / 2, 112, 40, fill=K.SUN, outline=3)

    money = [("Coin/f0", "12,480"), ("ic_gem", "1,240"), ("ic_heart", "5/5")]
    for i, (glyph, value) in enumerate(money):
        cx = W / 2 + (i - 1) * (228 + 14)
        K.paste(sheet, K.skin("Hud/trough", 228, 74), cx, 228)
        try:
            K.paste(sheet, K.fit(Image.open(UI / f"{glyph}.png").convert("RGBA"), (48, 48)),
                    cx - 228 / 2 + 62, 228)
        except FileNotFoundError:
            pass
        K.text(sheet, value, cx + 14, 228, 30, outline=3)
        K.paste(sheet, K.fit(K.load("Hud/add")[0], (50, 50)), cx + 228 / 2 - 2, 228)

    # ---- the tabs, which are the nav bar's own caps one level down
    step = min(230.0, 1020.0 / len(SHELVES))
    for i, name in enumerate(SHELVES):
        cx = W / 2 + (i - (len(SHELVES) - 1) * .5) * step
        live = name == shelf
        cap = K.fit(K.load("Hud/cap_on" if live else "Hud/cap_off")[0], (TABROW - 6, TABROW - 6))
        K.paste(sheet, cap, cx, HEADER + TABROW / 2)
        try:
            mark = K.fit(Image.open(UI / f"{TAB_GLYPH[name]}.png").convert("RGBA"),
                         (TABROW - 74, TABROW - 74))
            if not live:
                mark.putalpha(mark.split()[3].point(lambda v: int(v * .55)))
            K.paste(sheet, mark, cx, HEADER + TABROW / 2)
        except FileNotFoundError:
            pass


    K.text(sheet, shelf.upper(), W / 2, HEADER + TABROW + 22, 26,
           fill=(255, 245, 225), outline=2)

    # ---- the grid
    top = HEADER + TABROW + 44
    rows = products(shelf)
    rungs = LADDER.get(shelf, 3)

    for i, p in enumerate(rows):
        col, row = i % COLUMNS, i // COLUMNS
        x = W / 2 + (col - (COLUMNS - 1) * .5) * CELLW
        y = top + row * CELLH
        if y + CELLH > H - K.NAV_HEIGHT - RESTORE:
            break

        at = rungs - 1 if p.get("kind") == "nonconsumable" else rung(i + 1, len(rows), rungs)
        picture = f"{'bundles' if shelf == 'bundles' else shelf}_{at + 1}"

        grant = p.get("gems") or p.get("credits") or p.get("heartCapacity") or 0
        unit = "GEMS" if p.get("gems") else "COINS" if p.get("credits") else "HEARTS"
        badge = {"best_value": "BEST", "popular": "POPULAR", "starter": "STARTER"}.get(p.get("badge"))

        # `ProductCard.PaintRibbon` shows one at 5% or better; the figure is arithmetic over
        # the ladder, so the widest string on a shelf is what has to fit.
        bonus = f"+{12 + i * 14}% EXTRA" if i else None

        card(sheet, x, y, shelf, picture, f"{grant:,}", unit,
             f"${p['referenceUsdCents'] / 100:.2f}", badge, bonus)

    # `ShopScreen.BuildRestore` builds a 420x72 `TextButton` in `Skins.Resting`, not a bare
    # caption. Drawn as text alone this read as a loose line floating on the world — which is
    # a fault in the mirror rather than in the screen, and the kind that sends you off to fix
    # something that was never broken.
    ry = H - K.NAV_HEIGHT - RESTORE / 2
    K.paste(sheet, K.skin("sq_dark", 420, 72), W / 2, ry)
    K.text(sheet, "RESTORE PURCHASES", W / 2, ry, 26, fill=(205, 215, 232), outline=2)

    K.navbar(sheet, "shop")
    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--shelf", default="gems", choices=SHELVES)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--out", type=Path, default=Path("shop.png"))
    args = ap.parse_args()

    if args.all:
        shelves = [s for s in SHELVES if s in LADDER]
        sheets = [screen(s) for s in shelves]
        out = Image.new("RGB", (W * len(sheets), H))
        for i, one in enumerate(sheets):
            out.paste(one, (i * W, 0))
    else:
        out = screen(args.shelf)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print(f"  wrote {args.out}  {out.width}x{out.height}  - look at it")


if __name__ == "__main__":
    main()
