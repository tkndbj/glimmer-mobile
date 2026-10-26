# -*- coding: utf-8 -*-
"""Draws the turret shelf at the size a phone draws it, with the real sprites.

    python Tools/render_loadout.py                  # the turret shelf, fresh account
    python Tools/render_loadout.py --level 40       # every wall open
    python Tools/render_loadout.py --held bolt,siphon,beacon --stars 3
    python Tools/render_loadout.py --level 60 --held eclipse*2 --scroll 4200
    python Tools/render_loadout.py --shelf kit      # the utility shelf
    python Tools/render_loadout.py --columns 4      # what it used to look like
    python Tools/render_loadout.py --guide          # the first sortie's guide at the foot (53h)
    python Tools/render_loadout.py --battle         # ...and the BATTLE key that replaces him
    python Tools/render_loadout.py --guide --lang de
    python Tools/render_loadout.py --measure        # the guide's sentence in every Latin table

**Why this exists.** A shelf's column count is the one number on this screen that nothing in
this project can check: two across and four across both parse, both compile, both address
their art, and both pass every gate — the difference is entirely in what a player can see.
`render_shop.py` earned its place on a yellow price bar drawn on a yellow card; this is the
same bargain for the screen next to it, and it was written the day the shelf went from four
cells a row to two, because the question that change asks — *is the picture now the thing
being judged* — has no other instrument.

**A mirror only shows what it mirrors.** Every constant below is named after the field it
copies (`LoadoutScreen.CellW`, `WardStarRow.Star`), so a change on one side is findable on
the other; when a report from a device disagrees with this picture, this picture is the one
that is wrong. What it deliberately does **not** draw is the screen's own chrome — the
banner, the four seats and the two tabs are marked out as the space they occupy and nothing
more, because a mirror of a widget nobody asked about is a mirror that can be wrong about it.
The grid starts exactly where the screen starts it, which is the part that matters.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

REPO = K.REPO
UI = K.UI
SIEGE = REPO / "Assets" / "Game" / "Art" / "Siege"

#: A 16:9 phone at the canvas's own width, which is the shortest canvas any phone produces
#: (`CanvasFit.ShortestCanvas`) and therefore the worst case for how much of a shelf is in view.
PHONE_CANVAS = (1080, 1920)

#: A 4:3 tablet, in the canvas units `CanvasFit` gives one. `--tablet` swaps these in.
#:
#: **A squarer display is not handed 1080 units across.** `CanvasFit` widens the canvas until it
#: is `ShortHeight` (2160) units tall so that every layout keeps its sizes in units and is simply
#: drawn smaller — 1620 x 2160 for a 4:3. A mirror that could only draw 1080 would be blind to
#: the one thing this screen now does differently on a tablet, which is exactly the hole
#: `render_siege.py` had when a board a third too big shipped to every iPad (invariant 37cc).
TABLET_CANVAS = (1620, 2160)

#: The squarest tablet, which is the narrowest canvas the widening produces and so the tightest
#: this shelf's four columns are ever drawn: 2160 / 1.6.
TALL_TABLET_CANVAS = (1350, 2160)

W, H = PHONE_CANVAS

# LoadoutScreen — the bands above the grid. In units, so they do not move with the canvas.
HEADER_H, LINE_H, TABS_H = 232.0, 250.0, 104.0
VIEWPORT_FOOT = 40.0

ROW_GUTTER = 38.0
PHONE_COLUMNS, TABLET_COLUMNS = 2, 4
DESIGN_W = 492.0                                            # `LoadoutScreen.DesignW`
CELL_GAP_X, CELL_GAP_Y = 20.0, 22.0

ICON_FRAC = 0.55                                            # IconBox = CellW * .55f

# Everything below is the card's design at DESIGN_W, and is multiplied by `SCALE` — which is
# `PieceCard.ScaleFor`'s idiom and the screen's own. `fit()` derives the drawn values.
ICON_TOP = 30.0
NAME_Y, NAME_SIZE = 336.0, 36
STARS_Y, STAR_SIZE = 395.0, 32.0
FOOT_Y = 57.0
FOOT_H, FOOT_SIZE = 78.0, 44                                # the ward shelf's strip
ITEM_FOOT_H, ITEM_FOOT_SIZE = 58.0, 32                      # the kit shelf's
ITEM_NAME_SIZE = 34
CELLH = 470.0

# The band header belongs to the row rather than to a card, so it keeps its units.
TIER_H, TIER_GAP = 84.0, 26.0
TIER_SIZE, TIER_PAD = 46, 34.0

# `LoadoutScreen.NeonWarm` / `.NeonTube` / `.NeonCool` / `.NeonHalo` — the ramp graded across
# the legendary band's caption, and the halo behind it.
NEON_RAMP = ((255, 61, 240), (77, 240, 255))
NEON_HALO = (200, 28, 224)

#: Filled in by `fit()`: the row, the column count, the cell and how far the card is from the
#: one it was designed as.
ROW_SPAN, COLUMNS, CELLW, SCALE = 1004.0, PHONE_COLUMNS, DESIGN_W, 1.0

# WardStarRow
STAR_GAP = 0.22
STARS_MOST = 5

# WardTier.Opens / .Gates — the shelf rung each band starts at, and its keeper wall.
TIER_OPENS = (1, 11, 18, 21)
TIER_NAMES = {1: "TIER I", 2: "TIER II", 3: "TIER III", 4: "LEGENDARY"}

# GuideDock - the first sortie's foot (invariant 53h). Origin bottom-left of the safe area, y up;
# this mirror has no safe inset, so the safe area is the canvas.
FIGURE_H, MARGIN, FOOT = 300.0, 24.0, 18.0
HEAD_CLEAR, GUN_LINE, VISOR = 0.22, 0.42, 0.56
BUBBLE_MAX_W, LEFT = 720.0, 24.0
PAD_TOP, BODY_GAP, BUTTON_H, BUTTON_W, PAD_BOTTOM = 28.0, 18.0, 92.0, 300.0, 24.0
BODY_INSET = 36.0
BEAK, BEAK_OUT, CORNER = 46.0, 10.0, 28.0
HEADROOM = 20.0
KEY_W, KEY_H, KEY_FOOT = 620.0, 178.0, 30.0

# SortieGuide - the sentence's size, its floor, and the room it shrinks into; the key's caption.
BODY_SIZE, BODY_FLOOR, BODY_ROOM = 34, 24, 132.0
GOT_IT_SIZE = 36
INK = (33, 41, 51)                                          # SortieGuide.Ink (.13, .16, .20)

# LoadoutScreen.Sortie - the shade under the dock: the header's own ink.
FOOT_INK, FOOT_ALPHA = (5, 15, 23), 0.82

GUIDE = REPO / "Assets" / "Game" / "Art" / "Ui" / "Guide" / "idle" / "f00.png"

# Skins
PLATE_BLUE, PLATE_ORANGE = "Hud/plate_blue", "Hud/plate_orange"
PLATE_EDGE = (255, 200, 61)
VEIL = 0.48
SEAT = 0.30
STAR_LIT = (255, 194, 60)
STAR_UNLIT = (255, 255, 255, 115)


def pt(size):
    """`LoadoutScreen.Pt` — a scaled size as a font size."""
    return max(1, int(round(size)))


def fit(canvas, columns=None):
    """Mirrors the screen's own derivation, and is the whole of what `--tablet` changes.

    `CanvasFit` widens a squarer display's canvas rather than scaling a phone's, so the row is
    the canvas less a gutter either side; the column count is a declared pair (two on a phone,
    four on a tablet) because both divide the roster and a ramp off the width would answer three
    on a 16:10 display; and the card is one design drawn at whatever the row leaves it.
    """
    global W, H, ROW_SPAN, COLUMNS, CELLW, SCALE

    W, H = canvas
    K.W, K.H = canvas                                       # hudkit draws against these

    ROW_SPAN = W - ROW_GUTTER * 2
    COLUMNS = columns or (TABLET_COLUMNS if W > PHONE_CANVAS[0] else PHONE_COLUMNS)
    CELLW = (ROW_SPAN - (COLUMNS - 1) * CELL_GAP_X) / COLUMNS
    SCALE = CELLW / DESIGN_W


# --------------------------------------------------------------------------- content
def loc():
    d = json.loads((REPO / "Assets/StreamingAssets/Content/loc/en.json").read_text("utf-8"))
    return {e["key"]: e["text"] for e in d["entries"]}


def progression():
    return json.loads((REPO / "Assets/StreamingAssets/Content/progression.json").read_text("utf-8"))


def wards():
    """The roster in shelf order, which is `order` and not the file's own."""
    models = progression()["wards"]["models"]
    return sorted(models, key=lambda m: m.get("order", 0))


def tier_of(rung):
    """`WardTier.Of` — the band a shelf rung sits in, counted from one."""
    band = 1
    for i, opens in enumerate(TIER_OPENS):
        if rung >= opens:
            band = i + 1
    return band


# --------------------------------------------------------------------------- pieces
def sprite(path, box):
    """A sprite fitted into a square box, or `None` if it is not on disk.

    `hudkit.load` answers a `(sprite, border)` pair and raises on a missing file — both right
    for the kit it was written for, and neither right for art this mirror reaches outside it.
    """
    try:
        im, _ = K.load(path)
    except FileNotFoundError:
        return None
    return K.fit(im, (box, box))


def rounded(w, h, radius, colour, alpha):
    im = Image.new("RGBA", (int(w), int(h)), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle([0, 0, int(w) - 1, int(h) - 1], radius=radius,
                                         fill=colour + (int(alpha * 255),))
    return im


def star_x(index, size):
    """`WardStarRow.XOf`."""
    width = STARS_MOST * size + (STARS_MOST - 1) * size * STAR_GAP
    return -width * .5 + size * .5 + index * size * (1.0 + STAR_GAP)


def price_strip(sheet, cx, cy, w, h, text, size, glyph, ink):
    sheet.alpha_composite(rounded(w, h, pt(22 * SCALE), (0, 0, 0), SEAT),
                          (int(cx - w / 2), int(cy - h / 2)))

    shift = 0.0
    if glyph:
        # `Footer`: the label shifts right by half the glyph and its gap, so the pair is
        # centred rather than the number alone.
        shift = size * .55

    K.text(sheet, text, cx + shift, cy, size, fill=ink, outline=2)

    if glyph:
        run = K.font(size).getlength(text)
        g = sprite(glyph, size)
        if g:
            K.paste(sheet, g, cx + shift - run * .5 - size * .57, cy)


def ward_cell(sheet, model, at, cw, names, colour, level, held, stars, standing):
    """One turret, drawn exactly as `LoadoutScreen.WardCell` builds it."""
    x, y = at                                               # the cell's top-left
    icon_box = cw * ICON_FRAC
    cellh = CELLH * SCALE
    rung = model.get("order", 0)
    wall = model.get("minLevel", 0)
    locked = level < wall
    priced = "coinPrice" in model or "gemPrice" in model

    sheet.alpha_composite(K.skin(PLATE_ORANGE if standing else PLATE_BLUE, cw, cellh),
                          (int(x), int(y)))

    if standing:
        ImageDraw.Draw(sheet).rounded_rectangle(
            [x + 2, y + 2, x + cw - 3, y + cellh - 3], radius=24, outline=PLATE_EDGE, width=6)

    # **A legendary wears no colour** (`WardModel.ArtFor`), so its picture carries no colour
    # letter - and the mirror has to know, or it draws a white box on ten of the thirty cells and
    # reports the shelf as broken. `content.py` is what proves the address is really there.
    worn = "" if model.get("legendary") else "_" + colour
    art = sprite(f"../Siege/Wards/{model['id']}{worn}", icon_box)
    if art:
        K.paste(sheet, art, x + cw / 2, y + ICON_TOP * SCALE + icon_box / 2)

    if not held:
        # The veil covers the plate rather than the picture alone — it says the *cell* is not
        # yours, and it is built after the picture and before the padlock.
        sheet.alpha_composite(rounded(cw, cellh, 24, (0, 0, 0), VEIL), (int(x), int(y)))

        if locked:
            lock = sprite("ic_padlock", icon_box * .62)
            if lock:
                K.paste(sheet, lock, x + cw / 2, y + ICON_TOP * SCALE + icon_box / 2)

    K.text(sheet, names.get(f"ward.{model['id']}.name", model["id"]),
           x + cw / 2, y + NAME_Y * SCALE, pt(NAME_SIZE * SCALE), fill=K.CREAM, outline=2)

    if held:
        # **The stars are this seat's, on every band.** A legendary wore a copy count in this
        # corner while the band was bought outright (invariant 42k) - and this mirror is how
        # that was placed, because the count was drawn as a strip first, which is where every
        # other number on this cell goes: the strip spans 374 to 452 of a 470-tall cell while
        # the stars sit at 395, so it came out straight through them. A legendary is bought per
        # seat now, so the corner is empty again and every cell on this shelf says one thing.
        for i in range(STARS_MOST):
            lit = i < stars
            s = sprite("star_full" if lit else "star_empty", STAR_SIZE * SCALE)
            if s:
                if lit:
                    s = K.tint(s, STAR_LIT)
                else:
                    s = K.tint(s, (255, 255, 255), .45)
                K.paste(sheet, s, x + cw / 2 + star_x(i, STAR_SIZE * SCALE),
                        y + STARS_Y * SCALE)
        return

    if locked:
        text, glyph, ink = f"Level {wall}", None, (255, 243, 220, 140)
    elif not priced:
        text, glyph, ink = "Not for sale", None, (255, 243, 220, 115)
    else:
        gems = "gemPrice" in model
        text = f"{model.get('gemPrice') or model.get('coinPrice'):,}"
        glyph = "ic_gem" if gems else "Coin/f0"
        ink = K.CREAM

    price_strip(sheet, x + cw / 2, y + cellh - FOOT_Y * SCALE, cw - 20 * SCALE,
                FOOT_H * SCALE, text, pt(FOOT_SIZE * SCALE), glyph, ink)


def item_cell(sheet, item, at, cw, names, level):
    """One utility, drawn as `LoadoutScreen.ItemCell` builds it."""
    x, y = at
    icon_box = cw * ICON_FRAC
    cellh = CELLH * SCALE
    open_ = level >= item.get("minLevel", 0)

    sheet.alpha_composite(K.skin(PLATE_BLUE, cw, cellh), (int(x), int(y)))

    # `UtilityItem.Art` is "Ui/Utility/{id}", and `hudkit.load` prepends the Ui folder itself.
    art = sprite("Utility/" + item["id"], icon_box)
    if art:
        K.paste(sheet, art, x + cw / 2, y + ICON_TOP * SCALE + icon_box / 2)

    K.text(sheet, names.get(f"utility.{item['id']}.name", item["id"]),
           x + cw / 2, y + NAME_Y * SCALE, pt(ITEM_NAME_SIZE * SCALE), fill=K.CREAM, outline=2)

    if not open_:
        text, glyph = f"Level {item.get('minLevel')}", None
    elif item.get("gemPrice"):
        text, glyph = f"{item['gemPrice']:,}", "ic_gem"
    else:
        text, glyph = "From chests", None

    price_strip(sheet, x + cw / 2, y + cellh - FOOT_Y * SCALE, cw - 24 * SCALE,
                ITEM_FOOT_H * SCALE, text, pt(ITEM_FOOT_SIZE * SCALE), glyph, K.CREAM)


def tier_badge(sheet, tier, y, cw, columns):
    """`LoadoutScreen.TierBadge` — a caption between two rules, never a plate.

    **The clearance is measured here exactly as the screen measures it** (`Text.preferredWidth`
    against `draw.textlength`), so this is one of the few things the mirror can actually answer:
    whether the rules clear the longest heading the band can say. It could not answer it while
    the number was a constant on both sides — two copies of a guess agree with each other.
    """
    span = columns * cw + (columns - 1) * CELL_GAP_X
    mid = y + TIER_H / 2
    name = TIER_NAMES[tier]
    neon = tier == len(TIER_OPENS)
    d = ImageDraw.Draw(sheet, "RGBA")

    half = d.textlength(name, font=K.font(TIER_SIZE)) / 2 + TIER_PAD
    reach = max(0.0, span / 2 - half)

    if neon:
        # `Art.Glow(128, 1.6f)` stretched to the word's own box, under everything.
        w, h = int(half * 2 + 120), int(TIER_H + 36)
        halo = K.glow(256, 1.6, NEON_HALO, .34).resize((w, h), Image.LANCZOS)
        sheet.alpha_composite(halo, (int(W / 2 - w / 2), int(mid - h / 2)))

    for side in (-1, 1):
        # Each rule takes the end of the ramp that reaches it, as the screen does.
        ink = (*(NEON_RAMP[0] if side < 0 else NEON_RAMP[-1]), 102) if neon             else (255, 243, 220, 56)
        x = W / 2 + side * (half + reach * .5)
        d.rounded_rectangle([x - reach / 2, mid - 1, x + reach / 2, mid + 2],
                            radius=1, fill=ink)

    if neon:
        # Unity's `Outline` draws the glyphs again at the four corners; opaque and in the
        # neon's own hue that is the sheath round a white-hot core, which is what a tube is.
        # The ramp that used to run *through* the letters is gone — it is in the rules and the
        # halo now, and the word is white (`LoadoutScreen.TierBadge`).
        f = K.font(TIER_SIZE)
        w = d.textlength(name, font=f)
        x, ty = W / 2 - w / 2, mid - TIER_SIZE * 0.62
        for dx in (-3, 3):
            for dy in (-3, 3):
                d.text((x + dx, ty + dy), name, font=f, fill=(*NEON_HALO, 255))

        d.text((x, ty), name, font=f, fill=(255, 255, 255, 255))
    else:
        K.text(sheet, name, W / 2, mid, TIER_SIZE, fill=K.CREAM, outline=2)


def draw(shelf, level, held, stars, colour, standing, scroll):
    cw, columns = CELLW, COLUMNS
    span = columns * cw + (columns - 1) * CELL_GAP_X
    left = W / 2 - span / 2
    cellh = CELLH * SCALE

    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    K.plain(sheet)

    # **The grid is drawn on a layer of its own and clipped to the viewport**, because the
    # screen puts a `RectMask2D` there: a mirror that let a scrolled card draw over the header
    # would be hiding the one fault this geometry can actually have (invariant 37an - a host
    # inset is a hard edge, not a margin, and nothing anywhere says so).
    grid = Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # The chrome this mirror does not draw, marked out as the space it takes.
    top = HEADER_H + LINE_H + TABS_H
    d = ImageDraw.Draw(sheet, "RGBA")
    d.rectangle([0, 0, W, top], fill=(0, 0, 0, 70))
    for band, (name, h) in enumerate([("banner", HEADER_H), ("the line", LINE_H),
                                      ("tabs", TABS_H)]):
        y0 = sum([HEADER_H, LINE_H, TABS_H][:band])
        d.rectangle([28, y0 + 6, W - 28, y0 + h - 6], outline=(255, 243, 220, 60), width=2)
        K.text(sheet, name, W / 2, y0 + h / 2, 26, fill=(255, 243, 220, 120), outline=0)

    names = loc()

    # The shelf is far taller than its viewport, so a mirror that can only draw the
    # first screen can only ever judge tier one. `scroll` is the content's own offset.
    y = top + CELL_GAP_Y - scroll

    if shelf == "kit":
        items = progression()["utilities"]["items"]
        for i, item in enumerate(items):
            at = (left + (i % columns) * (cw + CELL_GAP_X),
                  y + (i // columns) * (cellh + CELL_GAP_Y))
            item_cell(grid, item, at, cw, names, level)
        return clip(sheet, grid, top)

    tier, column = 0, 0
    for model in wards():
        band = tier_of(model.get("order", 0))

        if band != tier:
            if tier != 0:
                if column != 0:
                    y += cellh + CELL_GAP_Y
                y += TIER_GAP
            tier_badge(grid, band, y, cw, columns)
            y += TIER_H
            tier, column = band, 0

        if y > H:
            break

        if y + cellh < top:                                 # scrolled off the top
            column += 1
            if column == columns:
                column = 0
                y += cellh + CELL_GAP_Y
            continue

        ward_cell(grid, model, (left + column * (cw + CELL_GAP_X), y), cw, names, colour,
                  level, model["id"] in held, stars, model["id"] == standing)

        column += 1
        if column == columns:
            column = 0
            y += cellh + CELL_GAP_Y

    return clip(sheet, grid, top)


#: How much of the foot the dock takes, which the viewport is raised by (`LoadoutScreen.FootNow`).
FOOT_BAND = 0.0


def clip(sheet, grid, top):
    """`RectMask2D` on the viewport: everything outside it is not drawn at all."""
    view = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    box = (0, int(top), W, int(H - max(VIEWPORT_FOOT, FOOT_BAND)))
    view.paste(grid.crop(box), box[:2])
    sheet.alpha_composite(view)
    return sheet


# --------------------------------------------------------------------------- the first sortie
def table(lang):
    rows = json.loads((REPO / "Assets/StreamingAssets/Content/loc" / f"{lang}.json").read_text("utf-8"))
    return {e["key"]: e["text"] for e in rows["entries"]}


def figure_aspect():
    im = Image.open(GUIDE)
    return im.width / im.height


def body_fit(sentence, width):
    """`SortieGuide.Build`'s sizing: shrink from 34 until the sentence fits `BODY_ROOM`, never
    below 24. Returns (size, lines). Greedy wrap at 1.2 lines, `hudkit.shrunk`'s approximation."""
    probe = ImageDraw.Draw(Image.new("RGBA", (8, 8)))
    for px in range(BODY_SIZE, BODY_FLOOR - 1, -1):
        f = K.font(px)
        lines = []
        # Each authored line break starts a fresh line, as uGUI's does (invariant 6c keeps the
        # breaks the same in every table).
        for paragraph in sentence.split("\n"):
            line = ""
            for word in paragraph.split(" "):
                trial = word if not line else line + " " + word
                if probe.textlength(trial, font=f) <= width or not line:
                    line = trial
                else:
                    lines.append(line)
                    line = word
            if line:
                lines.append(line)
        if len(lines) * px * 1.2 <= BODY_ROOM or px == BODY_FLOOR:
            return px, lines
    return BODY_FLOOR, []


def dock(width, aspect, body_h):
    """`GuideDock.For`, line for line. Centres in the dock's frame: origin bottom-left, y up."""
    fw = FIGURE_H * aspect
    figure = (width - MARGIN - fw / 2, FOOT + FIGURE_H / 2, fw, FIGURE_H)
    right = width - MARGIN - fw + fw * HEAD_CLEAR
    bw = min(BUBBLE_MAX_W, right - LEFT)
    bh = PAD_TOP + body_h + BODY_GAP + BUTTON_H + PAD_BOTTOM
    gun = FOOT + FIGURE_H * GUN_LINE
    visor = FOOT + FIGURE_H * VISOR
    bottom = max(gun, visor - bh + BEAK)
    bubble = (right - bw / 2, bottom + bh / 2, bw, bh)
    beak_y = min(max(visor, bottom + CORNER + BEAK / 2), bottom + bh - CORNER - BEAK / 2)
    beak = (right + BEAK_OUT, beak_y)
    band = max(FOOT + FIGURE_H, bottom + bh) + HEADROOM
    return figure, bubble, beak, band


def shade(sheet, band):
    """The dark fade the dock stands on (`Art.FadeUp` in the header's ink, dark at the foot)."""
    h = int(band + 70)
    fade = Image.new("RGBA", (W, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(fade)
    for y in range(h):
        d.line([(0, y), (W, y)], fill=(*FOOT_INK, int(255 * FOOT_ALPHA * y / max(1, h - 1))))
    sheet.alpha_composite(fade, (0, int(H - h)))


def guide(sheet, lang, report=None):
    """The guide standing and speaking: `SortieGuide` at rest. Returns the band it takes."""
    s = table(lang)
    aspect = figure_aspect()
    _, (_, _, bw, _), _, _ = dock(W, aspect, 0)

    size, lines = body_fit(s["ui.guide.upgrade"], bw - BODY_INSET * 2)
    body_h = len(lines) * size * 1.2
    figure, bubble, beak, band = dock(W, aspect, body_h)

    if report is not None:
        widest = max(K.font(size).getlength(ln) for ln in lines)
        report.append((lang, size, len(lines), widest, bw - BODY_INSET * 2, body_h))

    shade(sheet, band)

    def down(y):                                            # the dock's y-up into the sheet's
        return H - y

    fx, fy, fw, fh = figure
    K.paste(sheet, K.glow(int(fw * .78), 1.6, (0, 0, 0), .38).resize((int(fw * .78), 40)),
            fx, down(FOOT + 8 + 20))

    bx, by, bw, bh = bubble
    diamond = Image.new("RGBA", (int(BEAK), int(BEAK)), (0, 0, 0, 0))
    ImageDraw.Draw(diamond).polygon([(BEAK / 2, 0), (BEAK, BEAK / 2), (BEAK / 2, BEAK), (0, BEAK / 2)],
                                    fill=(255, 255, 255, 255))
    K.paste(sheet, diamond, beak[0], down(beak[1]))
    K.paste(sheet, K.round_rect(bw, bh, CORNER, (255, 255, 255)), bx, down(by))
    K.paste(sheet, K.round_rect(bw, bh, CORNER, (26, 33, 43), alpha=.16, width=3), bx, down(by))

    top = down(by + bh / 2)
    for i, ln in enumerate(lines):
        K.text(sheet, ln, bx, top + PAD_TOP + size * 1.2 * (i + .5), size, fill=INK, outline=0)

    gy = down(by - bh / 2 + PAD_BOTTOM + BUTTON_H / 2)
    K.paste(sheet, K.skin("btn_green", BUTTON_W, BUTTON_H), bx, gy)
    K.one_line(sheet, s["ui.common.got_it"], bx, gy - BUTTON_H * K.PILL_FACE_LIFT,
               BUTTON_W - 40, GOT_IT_SIZE, GOT_IT_SIZE // 2)

    K.paste(sheet, K.fit(Image.open(GUIDE).convert("RGBA"), (fw, fh)), fx, down(fy))
    return band


def battle(sheet, lang):
    """The BATTLE key risen into the foot: the hub's own key (`HomeScreen.BuildPlay`)."""
    s = table(lang)
    shade(sheet, KEY_FOOT + KEY_H + HEADROOM)

    cy = H - (KEY_FOOT + KEY_H / 2)
    face = cy - KEY_H * K.PILL_FACE_LIFT
    K.paste(sheet, K.skin("Hud/btn_gold", KEY_W, KEY_H), W / 2, cy)

    glyph = K.fit(K.load("ic_battle")[0], (112, 112))
    label = s["ui.home.battle"]
    lw = K.font(62).getlength(label)
    block = glyph.width + 18 + lw
    K.paste(sheet, glyph, W / 2 - block / 2 + glyph.width / 2, face)
    K.text(sheet, label, W / 2 - block / 2 + glyph.width + 18 + lw / 2, face, 62)


def measure():
    """The guide's sentence in every Latin-script table, against the room the bubble gives it.

    Arabic is skipped - this font has no Arabic forms, and the game shapes it through
    `ArabicText` at runtime - so `ArabicTextTests` carries that table, as it does for every
    other mirror here."""
    report = []
    for lang in ("en", "es", "pt", "fr", "de", "it", "tr", "pl"):
        guide(Image.new("RGBA", (W, H), (0, 0, 0, 255)), lang, report)

    bad = []
    print("%-6s %4s %5s %8s %8s %8s" % ("lang", "at", "lines", "widest", "room", "height"))
    for lang, size, lines, widest, room, height in report:
        over = widest > room + .5 or height > BODY_ROOM + .5
        if over:
            bad.append(lang)
        print("%s %-4s %4d %5d %8.1f %8.1f %8.1f%s" % ("<-" if over else "  ", lang, size, lines,
                                                       widest, room, height,
                                                       "   AT FLOOR" if size == BODY_FLOOR else ""))
    if bad:
        print("\nthe guide's sentence does not fit in: " + ", ".join(bad))
        return False
    print("\nthe guide's sentence fits in every table.")
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--shelf", choices=("wards", "kit"), default="wards")
    ap.add_argument("--tablet", action="store_true",
                    help="draw a 4:3 tablet's canvas (1620x2160 units, per `CanvasFit`), which "
                         "is where the shelf stands four across")
    ap.add_argument("--tall-tablet", action="store_true",
                    help="draw the squarest tablet (1350x2160), the tightest four columns get")
    ap.add_argument("--columns", type=int, default=None,
                    help="override the column count this canvas would choose, to see what "
                         "another would look like on it")
    ap.add_argument("--level", type=int, default=9,
                    help="keeper level, which decides every wall (today's content pays for 9)")
    ap.add_argument("--held", default="bolt",
                    help="comma-separated turret ids drawn as owned on the seat being filled "
                         "(every turret is bought per seat, the legendary band included)")
    ap.add_argument("--standing", default="bolt", help="the turret on this seat")
    ap.add_argument("--stars", type=int, default=1, help="stars on every held turret")
    ap.add_argument("--colour", default="r", choices=tuple("rgby"),
                    help="the seat being filled, which is the colour every turret is worn in")
    ap.add_argument("--scroll", type=float, default=0.0,
                    help="how far down the shelf has been scrolled, in canvas units")
    ap.add_argument("--guide", action="store_true",
                    help="the first sortie's guide standing at the foot, speaking (invariant 53h)")
    ap.add_argument("--battle", action="store_true",
                    help="the BATTLE key that rises in the guide's place")
    ap.add_argument("--lang", default="en", help="the table the guide's sentence is drawn from")
    ap.add_argument("--measure", action="store_true",
                    help="measure the guide's sentence in every Latin table; error on a spill")
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    if a.tablet and a.tall_tablet:
        sys.exit("--tablet and --tall-tablet are two displays; draw one at a time")

    fit(TABLET_CANVAS if a.tablet else
        TALL_TABLET_CANVAS if a.tall_tablet else PHONE_CANVAS, a.columns)

    # Held **on this seat**, which is what the shelf draws: the grid is a different grid on
    # every slot, so what is owned on red says nothing about blue (`WardHolding`).
    held = {x.strip() for x in a.held.split(",") if x.strip()}

    if a.measure:
        sys.exit(0 if measure() else 1)

    global FOOT_BAND
    if a.guide and a.battle:
        sys.exit("--guide and --battle are two moments; draw one at a time")
    if a.guide or a.battle:
        probe = Image.new("RGBA", (W, H))
        FOOT_BAND = guide(probe, a.lang) if a.guide else KEY_FOOT + KEY_H + HEADROOM

    sheet = draw(a.shelf, a.level, held, a.stars, a.colour, a.standing, a.scroll)

    if a.guide:
        guide(sheet, a.lang)
    elif a.battle:
        battle(sheet, a.lang)

    kind = "tablet" if a.tablet else "tall" if a.tall_tablet else "phone"
    if a.guide or a.battle:
        kind += "_guide" if a.guide else "_battle"
    out = Path(a.out) if a.out else REPO / "Tools" / "out" / f"loadout_{a.shelf}_{kind}.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    print(f"{out}   canvas {W:.0f}x{H:.0f}, {COLUMNS} across, "
          f"cell {CELLW:.0f} x {CELLH * SCALE:.0f}, scale {SCALE:.3f}")


if __name__ == "__main__":
    main()
