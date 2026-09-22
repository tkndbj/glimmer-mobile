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
AMBER = (255, 138, 43)           # Pal.Amber #FF8A2B
ROSE = (232, 97, 90)
MINT = (123, 216, 106)
BLOOM = (255, 116, 212)
AQUA = (59, 233, 216)
INK = (32, 48, 63)

# Scenery
RAIL_TOP_H, RAIL_FOOT_H = 65.0, 77.0

# NavBar. Every one of these is named after the field it mirrors, and the button width is
# **derived** exactly as `NavBar.Widths` derives it - a typed one would answer the wrong
# question the next time a tab is added or held.
NAV_HEIGHT = 236.0
NAV_CELL_H = 208.0
NAV_BTN_H = 172.0
NAV_GUTTER = 16.0
NAV_MAX_BTN_W = 240.0
NAV_GROW = 1.06
NAV_ICON = 136.0
NAV_ICON_Y = 40.0
NAV_PLATE = (8, 31, 69)          # Skins.Plate  #081F45
NAV_LIT = (255, 200, 61)         # Skins.PlateEdge  #FFC83D


def nav_button(slot):
    """`NavBar.Widths` - the cap fills its slot up to a ceiling, leaving `NAV_GUTTER` of air."""
    return min(NAV_MAX_BTN_W, slot - NAV_GUTTER), NAV_BTN_H

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


def round_rect(w, h, radius, colour, alpha=1.0, width=0):
    """`Art.Round(r)` filled, or `Art.RoundOutline(r, width)` when `width` is given.

    The game generates these rather than cutting them, so the mirror generates them too: a
    nine-slice of a bought sprite would answer a different question about the corner.
    """
    w, h = max(1, int(round(w))), max(1, int(round(h)))
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    fill = (*colour, int(255 * alpha))

    if width <= 0:
        d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=fill)
    else:
        d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, outline=fill, width=int(width))
    return im


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


def one_line(sheet, s, cx, cy, room, size, floor, fill=CREAM, what=None, outline=2):
    """`UIKit.OneLineLabel` - the largest size between `floor` and `size` at which the string
    fits on ONE line in `room`.

    **Not `shrunk`, and the difference is the whole point.** `shrunk` mirrors Unity's Best Fit,
    which is allowed to *wrap* - so it takes a two-word caption onto two lines and keeps the
    type big, where a label fitted by `OneLineLabel` stays on one line and gives up size
    instead. A mirror that wrapped where the screen does not would report room that does not
    exist, which is the comfortable lie a render may never tell (invariant 44d).

    **It always measures from `size`, never from the last result**, because the thing it is
    mirroring does too: `OneLineLabel` takes the design size as an argument precisely so a
    caption that gets shorter grows back, and a mirror that ratcheted would be drawing a screen
    the game does not.

    `what` names the widget in the TIGHT report, which fires when a line lands on the floor -
    "23 against a floor of 14" and "14 against a floor of 14" are the difference between a
    caption that fits and one Unity will not clip (invariants 19n, 37n).
    """
    while size > floor and font(size).getlength(s) > room:
        size -= 1

    if what and font(size).getlength(s) > room:
        print("  TIGHT  %s needs %.0f units and has %.0f: '%s'"
              % (what, font(size).getlength(s), room, s))

    text(sheet, s, cx, cy, size, fill=fill, outline=outline)
    return size


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


def shrunk_left(sheet, s, left, top, box_w, box_h, size, floor, fill=CREAM, outline=3):
    """`UIKit.Shrinkable` over a **`TextAnchor.UpperLeft`, wrapped** label.

    The same Best Fit as `shrunk` and the same greedy wrap; what differs is where the block
    is put, which is the whole reason it is a second function rather than a flag. A
    `PanelStack` paragraph is left-aligned and hangs from the top of its box, so a mirror
    that centred it would draw a ragged column down the middle of a panel whose real text is
    a flush-left block — a picture of a screen the game does not draw (invariant 44d), on the
    one question these panels are ever asked.

    `left` and `top` are the box's own edges, not a centre: `UIKit.Box` pivots at centre, so
    a caller working in Unity's coordinates converts once, at the point of placement.
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
            for i, ln in enumerate(lines):
                text(sheet, ln, left, top + px * .6 + i * px * 1.2, px,
                     fill=fill, outline=outline, anchor="l")
            return px

    return int(floor)


# --------------------------------------------------------------------------- furniture
def room(sheet):
    """`Scenery.Room` — the world, enveloped to the canvas, lightly shaded, vignetted.

    **`Bg/hub_room`, not `Hud/room`.** The interface kit ships a room of its own and this drew
    that one for as long as it existed, which is a picture of a screen the game does not draw
    (invariant 44d) — and it drew it on the one screen whose whole backdrop is the thing being
    judged. `Scenery.Room` names `Bg/hub_room` and has since the kit landed.

    The 1.04 is `Scenery.Room`'s own `localScale`, which is there so the parallax has somewhere
    to travel; it is not a crop margin, and a mirror that rounds it to 1.06 reports clearance at
    the edges that the screen does not have.
    """
    im = Image.open(REPO / "Assets" / "Game" / "Art" / "Bg" / "hub_room.png").convert("RGBA")
    s = max(W / im.width, H / im.height) * 1.04
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


# `NavBar.Order`. The Grovement tab was held on 2026-09-15 and the feature was removed on
# 2026-09-21, so `Tab.Grove` is out of the enum as well as out of the order — nothing here
# changed at either date, because this list has always mirrored the *order*. A mirror still
# drawing a cap the game does not would answer the wrong question about the spacing of every
# other cap, because the slot width is derived from the length (44d).
NAV_TABS = [("home", "ic_home"), ("shop", "ic_chest"),
            ("ranks", "ic_trophy"), ("profile", "ic_profile")]
#: `NavBar.LabelKey` through `loc/en.json`. Written out rather than looked up because
#: `hudkit` is the kit and loads no content - so it has to be kept in step by hand, and the
#: profile tab said YOU here for as long as the game has said PROFILE (invariant 44d).
NAV_WORDS = {"home": "HOME", "shop": "SHOP",
             "ranks": "BOARDS", "profile": "PROFILE"}


def navbar(sheet, active):
    """`NavBar.Build` and `NavBar.Item`, drawn the way the game draws them.

    **Not the kit's cap sprite.** `NavBar.Item` builds `Art.Round(26)` tinted `Skins.Plate`,
    with a dark seat rim under it and the kit's gold rim on the live tab; `NavBar.CapSkin`
    still looks up `Hud/cap_on` and nothing calls it. This mirror drew that unused sprite,
    square-fitted, for as long as it has existed — so every render of the hub showed a row
    of round caps against a device drawing rounded rectangles (invariant 44d).
    """
    rail(sheet, top=False)

    # `NavBar.Build`: the bar is `NAV_HEIGHT` tall against the foot, the cell sits 4 above
    # its middle, and the slot is derived from the tab count rather than typed.
    base = H - NAV_HEIGHT / 2 - 4
    slot = W / float(len(NAV_TABS))
    btn_w, btn_h = nav_button(slot)

    for i, (tab, icon) in enumerate(NAV_TABS):
        x = W / 2 + (i - (len(NAV_TABS) - 1) * .5) * slot
        live = tab == active
        grow = 1.06 if live else 1.0

        pw, ph = btn_w * grow, btn_h * grow

        if live:
            paste(sheet, glow(btn_w * 1.7, 2.1, SUN, .30), x, base - 6)

        # `Skins.Plate`, lifted toward white on the live tab; the rest at 92% alpha.
        face = (62, 80, 110) if live else NAV_PLATE
        paste(sheet, round_rect(pw, ph, 26, face, 1.0 if live else .92), x, base)

        # Two rims: the dark seat under everything, then the kit's gold on the live one.
        paste(sheet, round_rect(pw, ph, 26, (5, 15, 33), .85, width=4), x, base)
        if live:
            paste(sheet, round_rect(pw - 6, ph - 6, 26, NAV_LIT, 1.0, width=6), x, base)

        # The glyph, hanging over the plate's top edge, and never tinted.
        try:
            mark = fit(Image.open(UI / f"{icon}.png").convert("RGBA")
                       if "/" not in icon else
                       Image.open(REPO / "Assets" / "Game" / "Art" / f"{icon}.png").convert("RGBA"),
                       (NAV_ICON * grow, NAV_ICON * grow))
            paste(sheet, mark, x, base - NAV_ICON_Y * grow)
        except FileNotFoundError:
            pass

        # Inside the plate, measured from its own foot — which is what makes the button one
        # object rather than a square with a caption parked under it.
        shrunk(sheet, NAV_WORDS[tab], x, base + ph / 2 - 28, btn_w - 16, 34,
               27 if live else 25, 17,
               fill=SUN if live else (255, 247, 230), outline=4)
