# -*- coding: utf-8 -*-
"""Draws the launch screen — the publisher card, then the cover and its bar.

    python Tools/render_splash.py                     # the card and the loading screen, on a phone
    python Tools/render_splash.py --contact           # every canvas shape the game can produce
    python Tools/render_splash.py --ident             # the card's animation, as a filmstrip
    python Tools/render_splash.py --progress .35      # the bar part-filled
    python Tools/render_splash.py --out out/splash.png

**Why this exists.** The launch screen is the one screen with the least to check it anywhere
else. What the loading bar must not collide with is painted *into a texture*: there is no rect
to measure, nothing for a validator to walk, and no compile that can fail. `SplashCoverTests`
proves the arithmetic and cannot open the PNG, so it cannot tell you the bar has landed on a
muzzle flash or that the mark is being cropped through its own burst — both of which are
properties of *this* picture rather than of the numbers.

It earned its place immediately. The cover it was written for put its wordmark in the bottom
tenth, so standing the picture on the canvas floor kept the mark safe by construction; this one
carries the mark across the middle, and bottom-aligned it is cropped through the logo on every
canvas squarer than about 5:4. The fixture could not see that, because its list of canvases had
no widened ones in it.

**It is a mirror, and mirrors drift.** Every constant below is named after the field it copies
(`SplashCover.WordFootUv`, `IdentWordmark.Tracking`), so a change on one side is findable on the
other — and when a device disagrees with this picture, the picture is the one that is wrong. It
draws the furniture; it does not draw the tweens, so nothing here says whether the curtain lifts
well, only what is under it when it does.
"""
from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

REPO = Path(__file__).resolve().parent.parent
COVER = REPO / "Assets" / "Game" / "Art" / "Bg" / "splash_cover.png"
IDENT = REPO / "Assets" / "Game" / "Art" / "Bg" / "ident_word.png"

# ---------------------------------------------------------------- SplashCover mirror
ART_W, ART_H = 941.0, 1672.0
WORD_HEAD_UV, WORD_FOOT_UV = .210, .405
MARK_CENTRE_UV = (WORD_HEAD_UV + WORD_FOOT_UV) * .5
MARK_ON_CANVAS = .30
WORD_LEFT_UV, WORD_RIGHT_UV = .085, .918
WORD_MARGIN = 12.0
BAR_SPAN, BAR_HEIGHT = .68, 28.0
FOOT, MIN_GAP, PAD = 60.0, 14.0, 10.0
SIDE_MARGIN, MIN_BAR_WIDTH = 90.0, 240.0

SKY_JOIN, SKY_MID, SKY_TOP = "#02388F", "#022C72", "#021F52"

# ------------------------------------------------------------- IdentWordmark mirror
WIDTH_FRACTION, MAX_WIDTH, IDENT_MARGIN = .58, 780.0, 112.0
RULE_OVERHANG, RULE_DROP, RULE_HEIGHT = 18.0, 84.0, 2.0
BAND_WIDTH, BAND_OVERRUN = .95, .62
NEON_STOPS = ["#FF2E9A", "#8A5BFF", "#20E0FF", "#4BFFA5", "#FFE24A"]
AMBIENT = "#123E63"


def ident_fit(canvas_width, aspect):
    """Mirror of IdentWordmark.Fit."""
    room = canvas_width - IDENT_MARGIN * 2
    if canvas_width <= 0 or aspect <= 0 or room <= 0:
        return None
    width = min(canvas_width * WIDTH_FRACTION, room, MAX_WIDTH)
    return dict(width=width, height=width / aspect,
                ruleWidth=width + RULE_OVERHANG * 2, ruleY=-RULE_DROP)


# the canvases CanvasFit can actually produce: a phone is 1080 across and never squarer than
# 1.75; anything squarer is handed 2160 tall and a wider canvas.
PHONE_W, PHONE_FLOOR, SHORT_H = 1080.0, 1.75, 2160.0
CANVASES = [(PHONE_W, round(PHONE_W * a)) for a in (1.75, 1.78, 2.0, 2.17, 2.33)]
CANVASES += [(round(SHORT_H / a), SHORT_H) for a in (1.6, 1.33, 1.0)]


def fit(cw, ch, safe_bottom=0.0):
    """Mirror of SplashCover.Fit."""
    fill = cw / ART_W
    cover = max(fill, ch / ART_H)
    clip = (cw - WORD_MARGIN * 2) / (ART_W * (WORD_RIGHT_UV - WORD_LEFT_UV))
    scale = max(fill, min(cover, clip))
    width, height = ART_W * scale, ART_H * scale

    if height >= ch:
        limit = (height - ch) * .5
        wanted = ch * (.5 - MARK_ON_CANVAS) - height * (.5 - MARK_CENTRE_UV)
        picture_y = max(-limit, min(limit, wanted))
    else:
        picture_y = (height - ch) * .5

    sky = max(0.0, ch - height)
    word_foot = picture_y + height * (.5 - WORD_FOOT_UV)

    half = BAR_HEIGHT * .5
    bar_y = min(-ch * .5 + safe_bottom + PAD + FOOT + half, word_foot - MIN_GAP - half)

    span = width * (WORD_RIGHT_UV - WORD_LEFT_UV) * BAR_SPAN
    room = max(MIN_BAR_WIDTH, cw - SIDE_MARGIN * 2)
    bar_w = max(MIN_BAR_WIDTH, min(span, room))
    bar_x = ((WORD_LEFT_UV + WORD_RIGHT_UV) * .5 - .5) * width
    return dict(width=width, height=height, pictureY=picture_y, sky=sky,
                wordFoot=word_foot, barX=bar_x, barY=bar_y, barW=bar_w)


# ------------------------------------------------------------------------- drawing
def _px(cw, ch, x, y):
    """Canvas units (origin centre, y up) to image pixels (origin top-left, y down)."""
    return cw * .5 + x, ch * .5 - y


def _hex(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def _glow(size, power, colour, alpha):
    """Art.Glow: 1 at the centre falling to 0 at the rim, raised to `power`."""
    w, h = max(2, int(size[0])), max(2, int(size[1]))
    n = 96
    mask = Image.new("L", (n, n), 0)
    px = mask.load()
    for y in range(n):
        for x in range(n):
            dx, dy = (x - n / 2) / (n / 2), (y - n / 2) / (n / 2)
            d = math.hypot(dx, dy)
            px[x, y] = int(255 * alpha * max(0.0, 1.0 - d) ** power)
    tile = Image.new("RGB", (n, n), colour)
    out = Image.new("RGBA", (n, n))
    out.paste(tile, (0, 0), mask)
    return out.resize((w, h), Image.LANCZOS)


def draw_loading(cw, ch, progress=.62, safe_bottom=0.0):
    cw, ch = int(cw), int(ch)
    plan = fit(cw, ch, safe_bottom)
    img = Image.new("RGB", (cw, ch), _hex(SKY_TOP))
    d = ImageDraw.Draw(img)

    # the sky band the capped zoom leaves, as a gradient, with the picture's own top mirrored
    if plan["sky"] > .5:
        for row in range(int(plan["sky"]) + 1):
            t = row / max(1.0, plan["sky"])
            a, b = _hex(SKY_JOIN), _hex(SKY_TOP)
            col = tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
            d.line([(0, plan["sky"] - row), (cw, plan["sky"] - row)], fill=col)

    art = Image.open(COVER).convert("RGB").resize(
        (max(1, int(plan["width"])), max(1, int(plan["height"]))), Image.LANCZOS)
    left, top = _px(cw, ch, -plan["width"] * .5, plan["pictureY"] + plan["height"] * .5)
    img.paste(art, (int(round(left)), int(round(top))))

    # the bar's furniture
    bx, by = _px(cw, ch, plan["barX"], plan["barY"])
    bw, bh = plan["barW"], BAR_HEIGHT

    scrim = _glow((bw + 380, 210), 1.15, (0, 5, 15), .58)
    img.paste(scrim, (int(bx - scrim.width / 2), int(by - scrim.height / 2)), scrim)
    halo = _glow((bw + 160, 120), 2.0, (255, 204, 97), .22)
    img.paste(halo, (int(bx - halo.width / 2), int(by - halo.height / 2)), halo)

    box = [bx - bw / 2, by - bh / 2, bx + bw / 2, by + bh / 2]
    d.rounded_rectangle(box, radius=bh / 2, fill=(5, 13, 23), outline=(255, 199, 92), width=3)

    inner = bh - 8
    lit = 0 if progress <= 0 else max(inner, (bw - 8) * progress)
    fx = bx - bw / 2 + 4
    d.rounded_rectangle([fx, by - inner / 2, fx + lit, by + inner / 2], radius=inner / 2,
                        fill=(255, 190, 77))
    head = _glow((52, 52), 1.7, (255, 243, 214), .85)
    img.paste(head, (int(fx + lit - 26), int(by - 26)), head)
    return img


def _neon_band(w, h):
    """Art.Neon, stretched: spectrum across, alpha peaking in the middle and squared."""
    w, h = max(2, int(w)), max(2, int(h))
    strip = Image.new("RGBA", (256, 1))
    px = strip.load()
    stops = [_hex(s) for s in NEON_STOPS]
    for x in range(256):
        u = x / 255 * (len(stops) - 1)
        i = min(int(u), len(stops) - 2)
        f = u - i
        col = tuple(int(stops[i][c] + (stops[i + 1][c] - stops[i][c]) * f) for c in range(3))
        px[x, 0] = col + (int(255 * min(1.0, math.sin(x / 255 * math.pi) * 2.6)),)
    return strip.resize((w, h), Image.LANCZOS)


def _neon_at(t):
    stops = [_hex(s) for s in NEON_STOPS]
    u = max(0.0, min(1.0, t)) * (len(stops) - 1)
    i = min(int(u), len(stops) - 2)
    f = u - i
    return tuple(int(stops[i][c] + (stops[i + 1][c] - stops[i][c]) * f) for c in range(3))


def draw_ident(cw, ch, reveal=1.0, sweep=.5):
    """The card: TEKOWORLD cut out of black, with light behind it.

    `reveal` runs 0..1 as the light comes up; `sweep` is where the light has got to.
    """
    cw, ch = int(cw), int(ch)
    img = Image.new("RGB", (cw, ch), (0, 0, 0))

    mark = Image.open(IDENT).convert("RGBA")
    plan = ident_fit(cw, mark.width / mark.height)
    if plan is None:
        return img

    ease = lambda t: 1 - (1 - max(0.0, min(1.0, t))) ** 3          # noqa: E731  Ease.OutCubic

    d = ImageDraw.Draw(img)
    rule_t = ease(reveal / .62)
    rule_w = plan["ruleWidth"] * rule_t
    if rule_w > 1:
        ry = ch / 2 - plan["ruleY"]
        c = _hex("#2F6E8C")
        d.rectangle([cw / 2 - rule_w / 2, ry - RULE_HEIGHT / 2,
                     cw / 2 + rule_w / 2, ry + RULE_HEIGHT / 2],
                    fill=tuple(int(v * rule_t) for v in c))

    alpha = ease((reveal - .08) / .70)
    if alpha <= 0:
        return img

    mw, mh = max(1, int(plan["width"])), max(1, int(plan["height"]))
    letters = mark.resize((mw, mh), Image.LANCZOS)

    # what is behind the sheet: a low fill, and the spectrum band travelling across it
    behind = Image.new("RGBA", (mw, mh), _hex(AMBIENT) + (255,))
    band_w = plan["width"] * BAND_WIDTH
    travel = plan["width"] * .5 + band_w * BAND_OVERRUN
    band = _neon_band(band_w, mh * 2.2)
    bx = int(mw / 2 + (-travel + 2 * travel * sweep) - band.width / 2)
    behind.paste(band, (bx, int(mh / 2 - band.height / 2)), band)

    # the cut-out: the mark is never painted, it only decides where the light is seen
    behind.putalpha(letters.getchannel("A").point(lambda v: int(v * min(1.0, alpha))))
    img.paste(behind, (int(cw / 2 - mw / 2), int(ch / 2 - mh / 2)), behind)
    return img


# ---------------------------------------------------------------------------- cli
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--contact", action="store_true", help="every canvas shape the game produces")
    ap.add_argument("--ident", action="store_true", help="the card's assembly as a filmstrip")
    ap.add_argument("--progress", type=float, default=.62)
    ap.add_argument("--inset", type=float, default=0.0, help="bottom safe-area inset, canvas units")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    if not COVER.exists():
        print(f"missing cover art: {COVER}", file=sys.stderr)
        return 1

    out = Path(args.out) if args.out else REPO / "Tools" / "out" / "splash.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    scale = .34

    if args.ident:
        frames = [draw_ident(1080, 2340, min(1.0, r * 2.2), r)
                  for r in (0.0, .16, .32, .48, .64, .80, .96)]
        tw, th = int(1080 * .22), int(2340 * .22)
        sheet = Image.new("RGB", (len(frames) * (tw + 8) + 8, th + 16), (26, 26, 28))
        for i, f in enumerate(frames):
            sheet.paste(f.resize((tw, th), Image.LANCZOS), (8 + i * (tw + 8), 8))
        sheet.save(out)
        print(f"wrote {out}  ({len(frames)} frames)")
        return 0

    if args.contact:
        tiles = []
        for cw, ch in CANVASES:
            tiles.append((f"{cw}x{ch}", draw_loading(cw, ch, args.progress, args.inset)))
        tw = int(max(t[1].width for t in tiles) * scale)
        th = int(max(t[1].height for t in tiles) * scale)
        sheet = Image.new("RGB", (len(tiles) * (tw + 10) + 10, th + 34), (26, 26, 28))
        d = ImageDraw.Draw(sheet)
        for i, (name, im) in enumerate(tiles):
            s = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
            x = 10 + i * (tw + 10)
            sheet.paste(s, (x + (tw - s.width) // 2, 10))
            d.text((x + 2, th + 16), name, fill=(225, 225, 230))
        sheet.save(out)
        print(f"wrote {out}  ({len(tiles)} canvases)")
        return 0

    card = draw_ident(1080, 2340, 1.0, .45)
    load = draw_loading(1080, 2340, args.progress, args.inset)
    pair = Image.new("RGB", (int(1080 * scale) * 2 + 30, int(2340 * scale) + 20), (26, 26, 28))
    for i, im in enumerate((card, load)):
        s = im.resize((int(1080 * scale), int(2340 * scale)), Image.LANCZOS)
        pair.paste(s, (10 + i * (s.width + 10), 10))
    pair.save(out)
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
