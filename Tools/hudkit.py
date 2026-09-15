# -*- coding: utf-8 -*-
"""Drawing primitives shared by the screen renderers, mirroring `UIKit` and `Skins`.

`render_home.py` and `render_shop.py` draw two screens that are made of the same furniture,
so the nine-slice, the aspect fit, the outlined caption and the nav bar live here rather than
twice over. That is the same argument `ProductCard` makes about the two shops: two copies of
one layout are two answers to questions this project has already settled once.

**A mirror only shows what it mirrors.** Every constant below is named after the field it
copies, so a change on one side is findable on the other — and when a report from a device
disagrees with one of these pictures, the picture is the one that is wrong.
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:                                        # pragma: no cover
    sys.exit("This needs Pillow:  python -m pip install pillow")

REPO = Path(__file__).resolve().parent.parent
UI = REPO / "Assets" / "Game" / "Art" / "Ui"
HUD = UI / "Hud"
FONT = REPO / "Assets" / "Game" / "Fonts" / "GameFont.ttf"

# Boot.RefWidth / RefHeight — the canvas every screen is laid out against.
W, H = 1080, 1920

# Skins
GROUND = (6, 24, 56)
BAND = (14, 38, 81)
WELL = (10, 30, 72)
MUTED = (158, 168, 184)
CREAM = (255, 243, 220)
SUN = (255, 201, 60)
GOLD = (255, 194, 60)
ROSE = (232, 97, 90)
MINT = (123, 216, 106)
BLOOM = (255, 116, 212)
AQUA = (59, 233, 216)
INK = (32, 48, 63)

# Scenery
RAIL_TOP_H, RAIL_FOOT_H = 65.0, 77.0

# NavBar
NAV_HEIGHT = 206.0
NAV_CELL_W, NAV_CELL_H = 200.0, 176.0
NAV_CAP_LIVE, NAV_CAP_REST = 150.0, 122.0
NAV_GROVE_SCALE = 1.16

_cache = {}


# --------------------------------------------------------------------------- sprites
def border_of(path):
    """The nine-slice border, read out of the sprite's own `.meta`.

    Read rather than restated: the cutting tool measures it off the alpha and writes it into
    the importer, so a table here would be a second copy that goes stale the first time a
    sprite is re-cut.
    """
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        return (0, 0, 0, 0)
    for line in meta.read_text(encoding="utf8").splitlines():
        if line.strip().startswith("spriteBorder:"):
            body = line.split("{", 1)[1].rstrip("}")
            out = {}
            for part in body.split(","):
                k, v = part.split(":")
                out[k.strip()] = int(float(v))
            return (out["x"], out["y"], out["z"], out["w"])
    return (0, 0, 0, 0)


def load(name):
    """A sprite by the address a screen names it with, minus the `Ui/` the code prepends."""
    if name in _cache:
        return _cache[name]
    path = UI / (name + ".png")
    im = Image.open(path).convert("RGBA")
    _cache[name] = (im, border_of(path))
    return _cache[name]


def nine(im, b, w, h):
    """Unity's `Image.Type.Sliced`: corners kept, edges and middle stretched."""
    w, h = max(1, int(round(w))), max(1, int(round(h)))
    left, bottom, right, top = b
    if not any(b):
        return im.resize((w, h), Image.LANCZOS)

    W0, H0 = im.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))

    # Unity's border is (left, bottom, right, top) in sprite space, where y counts up; the
    # image counts down, so top and bottom swap on the way in.
    xs = [(0, left, 0, left), (left, W0 - right, left, w - right), (W0 - right, W0, w - right, w)]
    ys = [(0, top, 0, top), (top, H0 - bottom, top, h - bottom), (H0 - bottom, H0, h - bottom, h)]

    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0:
                continue
            piece = im.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS)
            out.paste(piece, (dx0, dy0), piece)
    return out


def skin(name, w, h):
    im, b = load(name)
    return nine(im, b, w, h)


def fit(im, box):
    """A sprite drawn with `preserveAspect` inside a box."""
    s = min(box[0] / im.width, box[1] / im.height)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)


def tint(im, colour, alpha=1.0):
    """`Image.color` — a multiply, which is why it can only ever darken (invariant 37l)."""
    r, g, b, a = im.split()
    bands = [c.point(lambda v, m=m: int(v * m / 255)) for c, m in zip((r, g, b), colour)]
    if alpha < 1.0:
        a = a.point(lambda v: int(v * alpha))
    return Image.merge("RGBA", (*bands, a))


def paste(sheet, im, cx, cy):
    sheet.alpha_composite(im, (int(cx - im.width / 2), int(cy - im.height / 2)))


def glow(size, power, colour, alpha):
    """`Art.Glow(size, power)` tinted — a soft radial falloff."""
    r = int(size / 2)
    im = Image.new("RGBA", (r * 2, r * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    for i in range(r, 0, -1):
        k = (1 - i / float(r)) ** power
        d.ellipse([r - i, r - i, r + i, r + i],
                  fill=(*colour, int(255 * alpha * k)))
    return im


def rays(size, count):
    """`Art.Rays(size, count)` — the fan of wedges, out of the same arithmetic.

    Here rather than in a screen's own mirror because two of them draw a chest pack over one
    (invariant 44d: two screens made of the same furniture get one mirror). It is the whole of
    what makes a row of pictures read as treasure, so a render that leaves it out says the
    plate is emptier than it is.
    """
    im = Image.new("L", (size, size), 0)
    px = im.load()
    h = size * .5

    def smooth(t):
        t = min(1.0, max(0.0, t))
        return t * t * (3 - 2 * t)

    for y in range(size):
        dy = (y - h) / h
        for x in range(size):
            dx = (x - h) / h
            d = math.hypot(dx, dy)
            if d >= 1.0:
                continue
            wedge = smooth((.5 + .5 * math.cos(math.atan2(dy, dx) * count) - .42) / .38)
            px[x, y] = int(255 * wedge * smooth((d - .16) / .22) * smooth((1 - d) / .42))
    return im


# --------------------------------------------------------------------------- text
def font(size):
    return ImageFont.truetype(str(FONT), int(size))


def text(sheet, s, cx, cy, size, fill=CREAM, outline=3, anchor="c"):
    """`UIKit.Titled` — with the dark outline every caption in this game carries."""
    draw = ImageDraw.Draw(sheet)
    f = font(size)
    w = draw.textlength(s, font=f)
    x = cx - w / 2 if anchor == "c" else (cx if anchor == "l" else cx - w)
    y = cy - size * 0.62

    if outline:
        for dx in range(-outline, outline + 1):
            for dy in range(-outline, outline + 1):
                if dx or dy:
                    draw.text((x + dx, y + dy), s, font=f, fill=(23, 36, 51, 240))
    draw.text((x, y), s, font=f, fill=fill)
    return w


def shrunk(sheet, s, cx, cy, box_w, box_h, size, floor, fill=CREAM, outline=3):
    """`UIKit.Shrinkable` over `UIKit.Titled` - Unity's Best Fit, mirrored.

    Best Fit picks the **largest** size between `floor` and `size` at which the wrapped
    string fits the box in both directions, which is a different picture from "draw it at
    34 and let it spill": a caption that shrinks is legible and a caption that overflows is
    not clipped by Unity at all (invariant 37n). A mirror that could only draw the maximum
    would answer the wrong question about every plate in this game whose text can grow.

    The wrap is greedy on spaces and the line box is 1.2x the size, which is close enough
    for the only question this is ever asked - *can you still read it* - and is stated here
    rather than pretended away.

    Returns the size it settled on, so a caller can print it: "22" against a floor of 22 is
    the tell that a string has outgrown its plate and the plate is what has to move.
    """
    draw = ImageDraw.Draw(sheet)

    for px in range(int(size), int(floor) - 1, -1):
        f = font(px)
        lines, line = [], ""

        for word in s.split(" "):
            trial = word if not line else line + " " + word
            if draw.textlength(trial, font=f) <= box_w or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        if line:
            lines.append(line)

        widest = max((draw.textlength(ln, font=f) for ln in lines), default=0)
        if (widest <= box_w and len(lines) * px * 1.2 <= box_h) or px == int(floor):
            top = cy - (len(lines) - 1) * px * 1.2 / 2
            for i, ln in enumerate(lines):
                text(sheet, ln, cx, top + i * px * 1.2, px, fill=fill, outline=outline)
            return px

    return int(floor)


# --------------------------------------------------------------------------- furniture
def room(sheet):
    """`Scenery.Room` — the world, enveloped to the canvas, lightly shaded, vignetted."""
    im = Image.open(HUD / "room.png").convert("RGBA")
    s = max(W / im.width, H / im.height) * 1.06
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    sheet.alpha_composite(im, ((W - im.width) // 2, (H - im.height) // 2))

    shade = Image.new("RGBA", (W, H), (*GROUND, int(255 * .10)))
    sheet.alpha_composite(shade)

    # Art.Vignette — dark at the corners, clear in the middle.
    vig = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(vig)
    steps = 90
    for i in range(steps):
        k = i / float(steps)
        inset = int(min(W, H) * .52 * (1 - k) * .5)
        d.ellipse([-W * .25 + inset, -H * .12 + inset, W * 1.25 - inset, H * 1.12 - inset],
                  outline=(*GROUND, int(255 * .52 / steps * 3)), width=max(2, int(H / steps)))
    sheet.alpha_composite(vig)


def plain(sheet):
    """`Scenery.Plain` — one uniform pattern, enveloped, with no shade and no vignette.

    Deliberately neither: the pattern is uniform, so drifting it says nothing and darkening its
    corners darkens a colour somebody chose.
    """
    im = Image.open(REPO / "Assets" / "Game" / "Art" / "Bg" / "plain.png").convert("RGBA")
    s = max(W / im.width, H / im.height)
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    sheet.alpha_composite(im, ((W - im.width) // 2, (H - im.height) // 2))


def rail(sheet, top):
    """`Scenery.Rail` — stretched to the full width, never sliced, so the notch survives."""
    name = "Hud/rail_top" if top else "Hud/rail_bottom"
    h = RAIL_TOP_H if top else RAIL_FOOT_H
    im, _ = load(name)
    im = im.resize((W, int(h)), Image.LANCZOS)
    sheet.alpha_composite(im, (0, 0 if top else int(H - h)))


NAV_TABS = [("home", "ic_home"), ("shop", "ic_chest"), ("grovement", "Map/rock_grass"),
            ("ranks", "ic_trophy"), ("profile", "ic_profile")]
NAV_WORDS = {"home": "HOME", "shop": "SHOP", "grovement": "GROVE",
             "ranks": "RANKS", "profile": "YOU"}


def navbar(sheet, active):
    """`NavBar.Build` — the rail, then five caps overhanging it."""
    rail(sheet, top=False)

    base = H - NAV_HEIGHT / 2
    slot = W / float(len(NAV_TABS))

    for i, (tab, icon) in enumerate(NAV_TABS):
        x = W / 2 + (i - (len(NAV_TABS) - 1) * .5) * slot
        live = tab == active
        cap = (NAV_CAP_LIVE if live else NAV_CAP_REST) * (NAV_GROVE_SCALE if tab == "grovement" else 1.0)
        cy = base - (26 if live else 16) - 8

        if live:
            paste(sheet, glow(cap * 1.55, 2.1, SUN, .26), x, cy)

        face = fit(load("Hud/cap_on" if live else "Hud/cap_off")[0], (cap, cap))
        paste(sheet, face, x, cy)

        try:
            mark = fit(Image.open(UI / f"{icon}.png").convert("RGBA")
                       if "/" not in icon else
                       Image.open(REPO / "Assets" / "Game" / "Art" / f"{icon}.png").convert("RGBA"),
                       (cap * (.40 if live else .38), cap * (.40 if live else .38)))
            paste(sheet, tint(mark, INK if live else CREAM), x, cy)
        except FileNotFoundError:
            pass

        text(sheet, NAV_WORDS[tab], x, base + 64 - 8, 26 if live else 24,
             fill=SUN if live else (255, 245, 224), outline=4)
