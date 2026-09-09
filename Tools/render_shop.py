# -*- coding: utf-8 -*-
"""Draws the shop screen at the size a phone draws it, with the real sprites.

    python Tools/render_shop.py                 # the gem shelf
    python Tools/render_shop.py --shelf coins   # any shelf in progression.json
    python Tools/render_shop.py --all           # every shelf, side by side

**Why this exists.** Nothing in this project can open a PNG on the way to a build, and the
Editor cannot photograph a `ScreenSpaceOverlay` canvas — so a storefront's *look* is judged
the way every board here is judged, by a Python mirror that reads the same sprites and the
same numbers the screen does. `render_siege.py` earned its place six times over; this is the
same bargain for the one screen that takes money.

**A mirror only shows what it mirrors, and that cost a round.** This drew cards with no
coloured seat and no fan of rays behind the picture, because it was written after those were
already on the way out — so the shop it drew was *cleaner than the one on the phone*, and the
decoration the owner then asked to have removed was invisible here. A render is a diagnostic
for the things it draws and says nothing at all about the things it does not; when a report
from a device disagrees with this picture, this picture is the one that is wrong.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it
copies (`ShopScreen.HeaderHeight`, `ProductCard`'s offsets, `ProductCardBadges`), so a
change on one side is findable on the other. It is a diagnostic and not a rendering: what it
proves is that a frame stretches cleanly, that a price bar is legible on its card and that a
row of five tabs fits — none of which any numeric gate here can see.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
UI = REPO / "Assets" / "Game" / "Art" / "Ui"
KIT = UI / "Kit"
FONT = REPO / "Assets" / "Game" / "Fonts" / "GameFont.ttf"

# Boot.RefWidth / RefHeight — the canvas every screen is laid out against.
W, H = 1080, 1920

# ShopSkins
GROUND = (14, 30, 71)
TROUGH = (8, 19, 46)
MUTED = (158, 168, 184)

# ShopScreen
HEADER, TABROW = 300.0, 132.0
NAVBAR, RESTORE = 206.0, 92.0
COLUMNS, CELLW, CELLH = 2, 508.0, 560.0

# ProductCard / ProductCardBadges
PLATE_X, PLATE_Y = 34.0, 40.0
PLATEW, PLATEH = CELLW - PLATE_X, CELLH - PLATE_Y
ART, ART_DROP = 300.0, 168.0
AMOUNT_RISE, SUB_RISE, FACE_RISE = 196.0, 150.0, 74.0
FACEW, FACEH = CELLW - 110.0, 96.0
SEAL, SEAL_INSET, SEAL_DROP = 164.0, 76.5, 40.5
RIBBONW, RIBBONH, RIBBON_INSET, RIBBON_DROP, RIBBON_TILT = 240.0, 82.0, 96.0, 34.0, -8.0

SHELF_FRAME = {"coins": "frame_yellow", "bundles": "frame_green",
               "utilities": "frame_green"}

def border_of(name):
    """The nine-slice border, read out of the sprite's own `.meta`.

    Read rather than restated: `make_ui_kit_art.py` measures it off the alpha and writes it
    into the importer, so a table here would be a second copy that goes stale the first time
    a sprite is re-cut — which is exactly what happened when the keylines were thinned.
    """
    meta = (KIT / f"{name}.png.meta").read_text(encoding="utf8")
    for line in meta.splitlines():
        if line.strip().startswith("spriteBorder:"):
            return int(line.split("x:")[1].split(",")[0])
    return 0


# --------------------------------------------------------------------------- helpers
def load(path):
    return Image.open(path).convert("RGBA")


def nine(im, w, h, b):
    """Unity's `Image.Type.Sliced`: corners kept, edges and middle stretched."""
    w, h = max(1, int(round(w))), max(1, int(round(h)))
    if not b:
        return im.resize((w, h), Image.LANCZOS)

    W0, H0 = im.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    xs = [(0, b, 0, b), (b, W0 - b, b, w - b), (W0 - b, W0, w - b, w)]
    ys = [(0, b, 0, b), (b, H0 - b, b, h - b), (H0 - b, H0, h - b, h)]

    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            piece = im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS)
            out.paste(piece, (dx0, dy0), piece)
    return out


def kit(name, w, h):
    return nine(load(KIT / f"{name}.png"), w, h, border_of(name))


def fit(im, box):
    """A sprite drawn with `preserveAspect` inside a box."""
    s = min(box[0] / im.width, box[1] / im.height)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)


def paste(sheet, im, cx, cy):
    sheet.alpha_composite(im, (int(cx - im.width / 2), int(cy - im.height / 2)))


def font(size):
    return ImageFont.truetype(str(FONT), int(size))


def text(sheet, s, cx, cy, size, fill=(255, 250, 240), outline=3):
    """`UIKit.Titled` — centred, with the outline every caption here carries."""
    draw = ImageDraw.Draw(sheet)
    f = font(size)
    w = draw.textlength(s, font=f)
    x, y = cx - w / 2, cy - size * 0.62

    if outline:
        for dx in range(-outline, outline + 1):
            for dy in range(-outline, outline + 1):
                if dx or dy:
                    draw.text((x + dx, y + dy), s, font=f, fill=(12, 16, 28, 235))
    draw.text((x, y), s, font=f, fill=fill)


# --------------------------------------------------------------------------- the card
def card(sheet, x, top, shelf, picture, amount, sub, price, badge, bonus=None, live=True):
    """One product card, laid out the way `ProductCard`'s constructor lays one out."""
    frame = SHELF_FRAME.get(shelf, "frame_purple")

    plate_cx, plate_cy = x, top + CELLH / 2
    plate = kit(frame, PLATEW, PLATEH)
    paste(sheet, plate, plate_cx, plate_cy)

    ptop = plate_cy - PLATEH / 2
    pbot = plate_cy + PLATEH / 2

    spot = Image.new("RGBA", (int(370), int(370)), (0, 0, 0, 0))
    sd = ImageDraw.Draw(spot)
    for r in range(185, 0, -1):                                   # Art.Glow(160, 1.6)
        k = (1 - r / 185.0) ** 1.6
        sd.ellipse([185 - r, 185 - r, 185 + r, 185 + r],
                   fill=(255, 255, 255, int(255 * .17 * k)))
    paste(sheet, spot, plate_cx, ptop + ART_DROP)

    art = load(REPO / "Assets" / "Game" / "Art" / "Ui" / "Shop" / f"{picture}.png")
    paste(sheet, fit(art, (ART, ART)), plate_cx, ptop + ART_DROP)

    text(sheet, amount, plate_cx, pbot - AMOUNT_RISE, 46, outline=4)
    text(sheet, sub, plate_cx, pbot - SUB_RISE, 26, fill=(255, 245, 225, 210))

    buy = "btn_blue" if shelf == "coins" else "btn_yellow"   # ShopSkins.Buy
    face = kit(buy, FACEW, FACEH)
    if not live:
        face = Image.merge("RGBA", (*[c.point(lambda v: int(v * m / 255))
                                      for c, m in zip(face.split()[:3], MUTED)],
                                    face.split()[3]))
    paste(sheet, face, plate_cx, pbot - FACE_RISE)
    text(sheet, price, plate_cx, pbot - FACE_RISE - 4, 34, outline=3)

    if bonus:
        rib = fit(load(KIT / "ribbon.png"), (RIBBONW, RIBBONH))
        rib = rib.rotate(-RIBBON_TILT, Image.BICUBIC, expand=True)
        paste(sheet, rib, plate_cx - PLATEW / 2 + RIBBON_INSET, ptop + RIBBON_DROP)
        text(sheet, bonus, plate_cx - PLATEW / 2 + RIBBON_INSET,
             ptop + RIBBON_DROP - 2, 25, outline=3)

    if badge:
        seal = fit(load(KIT / "badge_red.png"), (SEAL, SEAL)).rotate(-11, Image.BICUBIC, expand=True)
        paste(sheet, seal, plate_cx + PLATEW / 2 - SEAL_INSET, ptop + SEAL_DROP)
        text(sheet, badge, plate_cx + PLATEW / 2 - SEAL_INSET,
             ptop + SEAL_DROP, 22, outline=2)


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
    sheet = Image.new("RGBA", (W, H), GROUND + (255,))

    # ---- header: the banner, the balances, the tabs
    paste(sheet, kit("tab_on", 480, 116), W / 2, 108)
    text(sheet, "SHOP", W / 2, 108, 42)

    money = [("Coin/f0", "12,480"), ("ic_gem", "1,240"), ("ic_heart", "5/5")]
    for i, (glyph, value) in enumerate(money):
        cx = W / 2 + (i - 1) * (228 + 14)
        paste(sheet, kit("pill", 228, 74), cx, 228)
        try:
            g = fit(load(UI / f"{glyph}.png"), (50, 50))
            paste(sheet, g, cx - 228 / 2 + 38, 228)
        except FileNotFoundError:
            pass
        text(sheet, value, cx + 4, 228, 30, outline=3)
        paste(sheet, fit(load(KIT / "add_yellow.png"), (46, 46)), cx + 228 / 2 - 6, 228)

    step = min(230.0, 1020.0 / len(SHELVES))
    for i, name in enumerate(SHELVES):
        cx = W / 2 + (i - (len(SHELVES) - 1) * .5) * step
        live = name == shelf
        paste(sheet, kit("tab_on" if live else "tab_off", step - 24, TABROW - 22),
              cx, HEADER + TABROW / 2)
        try:
            mark = fit(load(UI / f"{TAB_GLYPH[name]}.png"), (TABROW - 58, TABROW - 58))
            if not live:
                mark.putalpha(mark.split()[3].point(lambda v: int(v * .55)))
            paste(sheet, mark, cx, HEADER + TABROW / 2)
        except FileNotFoundError:
            pass

    text(sheet, shelf.upper(), W / 2, HEADER + TABROW + 22, 26,
         fill=(255, 245, 225, 190), outline=2)

    # ---- the grid
    top = HEADER + TABROW + 44
    rows = products(shelf)
    rungs = LADDER.get(shelf, 3)

    for i, p in enumerate(rows):
        col, row = i % COLUMNS, i // COLUMNS
        x = W / 2 + (col - (COLUMNS - 1) * .5) * CELLW
        y = top + row * CELLH
        if y + CELLH > H - NAVBAR - RESTORE:
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

    # ---- the foot
    draw = ImageDraw.Draw(sheet)
    draw.rectangle([0, H - NAVBAR, W, H], fill=(8, 18, 42, 255))
    text(sheet, "RESTORE PURCHASES", W / 2, H - NAVBAR - RESTORE / 2, 26,
         fill=(200, 210, 230, 200), outline=2)

    return sheet.convert("RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--shelf", default="gems", choices=SHELVES)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--out", type=Path,
                    default=Path("shop.png"))
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
